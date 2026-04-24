using FastExpressionCompiler;
using Hangfire;
using Medallion.Threading;
using Medallion.Threading.WaitHandles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Workflows.Handler.Attributes;
using Workflows.Handler.Core;
using Workflows.Handler.Core.Abstraction;
using Workflows.Handler.DataAccess;
using Workflows.Handler.Expressions;
using Workflows.Handler.Helpers;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.Linq.Expressions;
using System.Reflection;

namespace Workflows.Handler.Testing
{
    public class TestShell : IDisposable
    {
        public IHost CurrentApp { get; private set; }

        private IDistributedSynchronizationHandle _lock;
        private readonly HostApplicationBuilder _builder;
        private readonly Type[] _types;
        private readonly TestSettings _settings;
        private readonly string _testName;
        private IDistributedLockProvider _lockProvider = new WaitHandleDistributedSynchronizationProvider();
        public TestShell(string testName, params Type[] scanTypes)
        {
            _testName = testName;
            _settings = new TestSettings(testName);
            _builder = Host.CreateApplicationBuilder();
            _types = scanTypes;
        }

        public async Task DeleteDb(string dbName)
        {
            var dbConfig = new DbContextOptionsBuilder()
               .UseSqlServer(
                   $"Server=(localdb)\\MSSQLLocalDB;Database={dbName};Trusted_Connection=True;TrustServerCertificate=True;");
            var context = new DbContext(dbConfig.Options);
            try
            {
                await context.Database.EnsureDeletedAsync();
                await context.Database.EnsureCreatedAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public IServiceCollection RegisteredServices => _builder.Services;
        public async Task ScanTypes(params string[] workflowsUrnsToIncludeInTest)
        {
            await DeleteDb(_testName);
            _builder.Services.AddWorkflowsCore(_settings);
            CurrentApp = _builder.Build();
            _lock = await _lockProvider.AcquireLockAsync("Test827556");
            //_lock = await _lockProvider.AcquireLockAsync(Guid.NewGuid().ToString());
            GlobalConfiguration.Configuration.UseActivator(new HangfireActivator(CurrentApp.Services));

            using var scope = CurrentApp.Services.CreateScope();
            var serviceData = new ServiceData
            {
                AssemblyName = _types[0].Assembly.GetName().Name,
                ParentId = -1,
            };
            await using var context = scope.ServiceProvider.GetService<WaitsDataContext>();
            context.ServicesData.Add(serviceData);
            await context.SaveChangesAsync();
            _settings.CurrentServiceId = serviceData.Id;
            var scanner = scope.ServiceProvider.GetService<Scanner>();
            foreach (var type in _types)
                await scanner.RegisterMethodsInType(type, serviceData);
            await scanner.RegisterMethodsInType(typeof(LocalRegisteredMethods), serviceData);
            await context.SaveChangesAsync();

            foreach (var type in _types)
                if (type.IsSubclassOf(typeof(WorkflowContainer)))
                {
                    await scanner.RegisterWorkflows(typeof(SubWorkflowAttribute), type, serviceData);
                    await context.SaveChangesAsync();
                    await RegisterWorkflows(workflowsUrnsToIncludeInTest, serviceData, scanner, type);
                }
            await context.SaveChangesAsync();
            await context.DisposeAsync();
        }

        private static async Task RegisterWorkflows(string[] workflowsToIncludeInTest, ServiceData serviceData, Scanner scanner, Type type)
        {
            var workflows =
                type.GetMethods(CoreExtensions.DeclaredWithinTypeFlags())
                .Where(method => method
                    .GetCustomAttributes()
                    .Any(attribute =>
                        attribute is WorkflowAttribute entryPointAttribute &&
                        (workflowsToIncludeInTest.Length == 0 || workflowsToIncludeInTest.Contains(entryPointAttribute.MethodUrn))
                        )
                    );
            foreach (var resumableWorkflowInfo in workflows)
            {
                if (scanner.ValidateWorkflowSignature(resumableWorkflowInfo, serviceData))
                    await scanner.RegisterWorkflow(resumableWorkflowInfo, serviceData);
                else
                    serviceData.AddError($"Can't register resumable workflow [{resumableWorkflowInfo.GetFullName()}].", StatusCodes.MethodValidation, null);
            }
        }

        public async Task<string> RoundCheck(
            int signalsCount,
            int waitsCount = -1,
            int completedInstancesCount = -1)
        {

            if (await HasErrors())
            {
                throw new Exception(Context.Logs.First(x => x.LogType == LogType.Error).Message);
            }

            int callsCount = await GetSignalsCount();
            if (callsCount != signalsCount)
                throw new Exception($"Pushed calls count [{callsCount}] not equal [{signalsCount}]");

            if (waitsCount != -1 && await GetWaitsCount() is int existWaitsCount && existWaitsCount != waitsCount)
                throw new Exception($"Waits count [{existWaitsCount}] not equal [{waitsCount}]");


            if (completedInstancesCount != -1)
            {
                int instnacesCount = await GetCompletedInstancesCount();
                if (instnacesCount != completedInstancesCount)
                    throw new Exception($"Completed instances [{instnacesCount}] count not equal [{completedInstancesCount}]");
            }

            return string.Empty;
        }

        public async Task<long> SimulateMethodCall<TClassType>(
           Expression<Func<TClassType, object>> methodSelector,
           object output)
        {
            object input = null;
            var inputVisitor = new GenericVisitor();
            inputVisitor.OnVisitMethodCall(call =>
            {
                input = Expression.Lambda(call.Arguments[0]).CompileFast().DynamicInvoke();
                return call;
            });
            inputVisitor.Visit(methodSelector);
            if (input != null)
                return await SimulateMethodCall(methodSelector, input, output);

            throw new Exception("Can't get input");
        }

        public async Task<long> SimulateMethodCall<TClassType>(Expression<Func<TClassType, object>> methodSelector,
            object input,
            object output)
        {
            var methodInfo = CoreExtensions.GetMethodInfo(methodSelector);
            var pusher = CurrentApp.Services.GetService<ISignalDispatcher>();
            var pushResultAttribute = methodInfo.GetCustomAttribute<EmitSignalAttribute>();
            var signalId = await pusher.EnqueueLocalSignalWork(
                new SignalEntity
                {
                    Data =
                    {
                        Input= input,
                        Output= output//may be async task
                    },
                    MethodData = new MethodData(methodInfo)
                    {
                        MethodUrn = pushResultAttribute.MethodUrn,
                        CanPublishFromExternal = pushResultAttribute.FromExternal,
                        IsLocalOnly = pushResultAttribute.IsLocalOnly,
                    },
                    Created = DateTime.UtcNow,
                });
            await Context.SaveChangesAsync();
            return signalId;
        }

        private WaitsDataContext Context => CurrentApp.Services.GetService<WaitsDataContext>();
        public async Task<List<WorkflowInstance>> GetInstances<T>(bool includeNew = false)
        {
            var query = Context.WorkflowInstances.AsQueryable().AsNoTracking();
            if (includeNew is false)
            {
                query = query.Where(x => x.Status != WorkflowInstanceStatus.New);
            }
            var instances = await query.ToListAsync();
            foreach (var instance in instances)
            {
                //await Context.Entry(instance).ReloadAsync();
                instance.LoadUnmappedProps(typeof(T));

            }
            return instances;
        }

        public async Task<T> GetFirstInstance<T>()
        {
            var instance =
                await Context.WorkflowInstances
                .AsQueryable()
                .AsNoTracking()
                .Where(x => x.Status == WorkflowInstanceStatus.Completed)
                .FirstOrDefaultAsync();

            instance?.LoadUnmappedProps(typeof(T));
            return (T)instance.StateObject;
        }

        public async Task<int> GetCompletedInstancesCount()
        {
            return await Context.WorkflowInstances.CountAsync(x => x.Status == WorkflowInstanceStatus.Completed);
        }

        public async Task<List<SignalEntity>> GetSignals()
        {
            var calls = await Context.Signals.AsNoTracking().ToListAsync();
            foreach (var call in calls)
            {
                call.LoadUnmappedProps();
            }
            return calls;
        }

        public async Task<int> GetSignalsCount()
        {
            return await Context.Signals.CountAsync();
        }


        public async Task<List<WaitEntity>> GetWaits(int? instanceId = null, bool includeFirst = false)
        {
            var query = Context.Waits.AsQueryable().AsNoTracking();
            if (instanceId != null)
                query = query.Where(x => x.WorkflowInstanceId == instanceId);
            if (includeFirst is false)
                query = query.Where(x => !x.IsFirst);
            return await query.OrderBy(x => x.Id).ToListAsync();
        }

        public async Task<int> GetWaitsCount(Expression<Func<WaitEntity, bool>> expression = null)
        {
            expression ??= x => !x.IsFirst;
            return await Context.Waits.CountAsync(expression);
        }

        public async Task<List<LogRecord>> GetLogs(LogType logType = LogType.Error)
        {
            return
                await Context.Logs
                    .Where(x => x.LogType == logType)
                    .AsNoTracking()
                    .ToListAsync();
        }

        public async Task<bool> HasErrors()
        {
            return
                await Context.Logs
                    .Where(x => x.LogType == LogType.Error)
                    .AsNoTracking()
                    .AnyAsync();
        }
        public async Task<int> GetTemplatesCount()
        {
            return await Context.WaitTemplates.CountAsync();
        }
        public void Dispose()
        {
            _lock?.Dispose();
            Context?.Dispose();
            CurrentApp?.Dispose();
        }
        public async Task<List<WaitEntity>> GetWaitsCanceledByCall(long callId, string waitName = null)
        {
            return await Context
               .Waits
               .Include(x => x.ClosureData)
               .Include(x => x.Locals)
               .Where(x =>
                   x.Status == WaitStatus.Canceled &&
                   x.SignalId == callId &&
                   (waitName == null || x.Name == waitName))
               .AsNoTracking()
               .ToListAsync();
        }
        public async Task<List<WaitEntity>> GetWaitsMatchedByCall(long callId, string waitName = null)
        {
            return await Context
               .Waits
               .Include(x => x.ClosureData)
               .Include(x => x.Locals)
               .Where(x =>
                   x.Status == WaitStatus.Completed &&
                   x.SignalId == callId &&
                   (waitName == null || x.Name == waitName))
               .AsNoTracking()
               .ToListAsync();
        }

        public async Task<List<WaitEntity>> GetWaitsCreateAfterCall(long callId, string waitName = null)
        {
            var callIdCreated = await Context
                .Signals
                .Where(x => x.Id == callId)
                .AsNoTracking()
                .Select(x => x.Created)
                .FirstAsync();
            return await Context
                .Waits
                .Include(x => x.ClosureData)
                .Include(x => x.Locals)
                .Where(x =>
                    x.Created > callIdCreated &&
                    x.Status == WaitStatus.Waiting &&
                    (waitName == null || x.Name == waitName))
                .AsNoTracking()
                .ToListAsync();
        }
    }
}
