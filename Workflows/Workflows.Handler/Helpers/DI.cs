using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Handler.Abstraction.Abstraction;
using Workflows.Handler.Core;
using Workflows.Handler.Core.Abstraction;
using Workflows.Handler.DataAccess;
using Workflows.Handler.UiService;

namespace Workflows.Handler.Helpers
{
    public static class DI
    {
        //internal static IServiceProvider GetServiceProvider() => _ServiceProvider;
        public static void AddWorkflowsCore(this IServiceCollection services, IWorkflowsSettings settings)
        {


            // ReSharper disable once RedundantAssignment
            ResolveDbInterfaces(services, settings);

            services.AddScoped<IFirstWaitProcessor, FirstWaitProcessor>();
            services.AddScoped<ISignalsProcessor, WaitsProcessor>();
            services.AddScoped<IServiceQueue, ServiceQueue>();
            services.AddScoped<ISignalDispatcher, DbSignalDispatcher>();
            services.AddScoped<ICleaningJob, CleaningJob>();
            services.AddScoped<Scanner>();
            services.AddScoped<BackgroundJobExecutor>();




            services.AddHttpClient();
            services.AddSingleton(settings);
            services.AddSingleton(settings.DistributedLockProvider);


            services.AddScoped<IUiService, UiService.UiService>();
            if (settings.HangfireConfig != null)
            {
                // ReSharper disable once RedundantAssignment
                services.AddHangfire(x => x = settings.HangfireConfig);
                services.AddSingleton<IBackgroundProcess, HangfireBackgroundProcess>();
                services.AddHangfireServer();
            }
            else
                services.AddSingleton<IBackgroundProcess, NoBackgroundProcess>();
        }

        private static void ResolveDbInterfaces(IServiceCollection services, IWorkflowsSettings settings)
        {
            services.AddDbContext<WaitsDataContext>(optionsBuilder => optionsBuilder = settings.WaitsDbConfig);
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IMethodIdentifiersStore, MethodIdentifiersStore>();
            services.AddScoped<IWorkflowIdentifiersStore, MethodIdentifiersStore>();
            services.AddScoped<IPrivateDataStore, PrivateDataStore>();
            services.AddScoped<IWaitsStore, WaitsStore>();
            services.AddScoped<IServiceRepo, ServiceRepo>();//todo: why AddTransient?
            services.AddScoped<IWaitTemplatesStore, WaitTemplatesStore>();
            services.AddScoped<ISignalsStore, SignalsStore>();
            services.AddScoped<IDatabaseCleaning, DatabaseCleaning>();
            services.AddScoped<ISignalWaitMatchStore, WaitProcessingRecordsRepo>();

            services.AddTransient<ILogsRepo, LogsRepo>();//todo: why AddTransient?
            services.AddTransient<IScanLocksRepo, ScanLocksRepo>();//todo: why AddTransient?
        }

        public static void UseWorkflows(this IServiceProvider services)
        {
            GlobalConfiguration.Configuration.UseActivator(new HangfireActivator(services));
            CreateScanAndCleanBackgroundTasks(services);
        }
        private static void CreateScanAndCleanBackgroundTasks(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var backgroundJobClient = scope.ServiceProvider.GetService<IBackgroundProcess>();

            var scanner = scope.ServiceProvider.GetService<Scanner>();
            backgroundJobClient.Enqueue(() => scanner.Start());

            var cleaningJob = scope.ServiceProvider.GetService<ICleaningJob>();
            cleaningJob.CleanDataJob();
        }
    }
}
