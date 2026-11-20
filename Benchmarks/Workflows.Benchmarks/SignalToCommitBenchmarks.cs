using System.Diagnostics;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Schema.Generation;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.DTOs.Registration;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;
using Workflows.Definition.Registration;
using Workflows.Hosting.InProcess;
using Workflows.Runner;
using Workflows.Shared;
using Workflows.Storage.EntityFrameworkCore;
using Workflows.TestShell;

namespace Workflows.Benchmarks
{
    /// <summary>
    /// Signal-to-commit round trip: an external signal is injected through the orchestrator, matched against
    /// the persisted instance, executed by the runner, and committed back to the store in one transaction.
    /// This is the path the <c>WorkflowStore</c> N+1 fixes target.
    /// </summary>
    [MemoryDiagnoser]
    public class SignalToCommitBenchmarks
    {
        private ServiceProvider _provider = null!;
        private SqliteConnection? _connection;
        private IServiceScope _scope = null!;
        private IOrchestrator _orchestrator = null!;

        /// <summary>
        /// SQLite always runs. Postgres joins the matrix when <c>WORKFLOWS_TEST_POSTGRES</c> holds a
        /// connection string that points at a disposable database.
        /// </summary>
        public IEnumerable<string> Providers
        {
            get
            {
                yield return "Sqlite";
                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresVariable)))
                {
                    yield return "Postgres";
                }
            }
        }

        [ParamsSource(nameof(Providers))]
        public string Provider { get; set; } = "Sqlite";

        [GlobalSetup]
        public void Setup()
        {
            _provider = CreateServiceProvider(Provider, out _connection);
            SyncDefinitions(_provider).GetAwaiter().GetResult();

            _scope = _provider.CreateScope();
            _orchestrator = _scope.ServiceProvider.GetRequiredService<IOrchestrator>();

            _orchestrator.StartWorkflowAsync("BenchmarkLoopWorkflow", 1, null!).GetAwaiter().GetResult();

            ReportQueriesPerSignal();
        }

        /// <summary>
        /// Diagnostic only: logs the number of SQL commands one signal round trip issues. The N+1 fixes in
        /// <c>WorkflowStore</c> were about keeping this constant instead of growing with the instance count,
        /// so the figure is printed with the benchmark results.
        /// </summary>
        private void ReportQueriesPerSignal()
        {
            const int signals = 5;
            var commands = 0;
            using var counter = new CommandCounter(() => commands++);

            // EF Core publishes through DiagnosticListener.AllListeners, not a listener instance.
            using var allListeners = DiagnosticListener.AllListeners.Subscribe(counter);

            for (var i = 0; i < signals; i++)
            {
                ProcessSignal();
            }

            Console.WriteLine($"[BenchmarkDiagnostics] SQL commands per signal round trip: {commands / (double)signals:0.0}");
        }

        private sealed class CommandCounter : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
        {
            private readonly Action _onCommand;
            private IDisposable? _inner;

            public CommandCounter(Action onCommand) => _onCommand = onCommand;

            public void OnCompleted() { }

            public void OnError(Exception error) { }

            public void OnNext(DiagnosticListener value)
            {
                if (value.Name == "Microsoft.EntityFrameworkCore")
                {
                    _inner = value.Subscribe(this);
                }
            }

            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value.Key == "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted")
                {
                    _onCommand();
                }
            }

            public void Dispose() => _inner?.Dispose();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _scope?.Dispose();
            _provider?.Dispose();
            _connection?.Dispose();
        }

        /// <summary>
        /// A real deployment prunes finished wait rows and drained inbox rows in its maintenance sweeps.
        /// Doing the same before every iteration keeps the measurement at steady state instead of measuring
        /// an ever-growing wait table.
        /// </summary>
        [IterationSetup]
        public void ResetHistory()
        {
            using var scope = _provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();

            var finishedWaits = context.SignalWaits
                .Where(w => w.Status != (int)WaitStatus.Waiting)
                .ToList();

            context.SignalWaits.RemoveRange(finishedWaits);
            context.SignalInbox.RemoveRange(context.SignalInbox);

            // The store keeps every completed wait in the instance snapshot as well; a long-running loop
            // would otherwise grow the JSON column on every signal and the measurement would be dominated
            // by re-reading that history.
            foreach (var instance in context.WorkflowInstances.ToList())
            {
                instance.Waits.RemoveAll(w => w.Status != WaitStatus.Waiting);
            }

            context.SaveChanges();
        }

        [Benchmark(Description = "Signal -> match -> run -> commit")]
        public void ProcessSignal()
        {
            _orchestrator.ProcessSignalAsync(new SignalDto
            {
                SignalIdentifier = "BenchTick",
                Data = new BenchmarkSignal { Value = 1 }
            }).GetAwaiter().GetResult();
        }

        private const string PostgresVariable = "WORKFLOWS_TEST_POSTGRES";

        private static ServiceProvider CreateServiceProvider(string providerName, out SqliteConnection? connection)
        {
            var services = new ServiceCollection();
            services.AddWorkflowsShared();
            services.AddWorkflowsRunner();
            services.AddSingleton<JSchemaGenerator, MockSchemaGenerator>();
            services.AddSingleton<ICommandHandlerFactory>(new TestShellCommandHandlerFactory());

            if (string.Equals(providerName, "Postgres", StringComparison.OrdinalIgnoreCase))
            {
                connection = null;
                var postgres = Environment.GetEnvironmentVariable(PostgresVariable)
                    ?? throw new InvalidOperationException($"{PostgresVariable} is not set.");
                services.AddWorkflowsInProcessHost(postgres, static (s, cs) => s.AddWorkflowsPostgres(cs));
            }
            else
            {
                var connectionString = $"Data Source=benchmark_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
                connection = new SqliteConnection(connectionString);
                connection.Open();
                services.AddWorkflowsInProcessHost(connectionString);
            }

            var provider = services.BuildServiceProvider();

            using (var scope = provider.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>().Database.EnsureCreated();
            }

            provider.GetService<InboxOptions>()!.BypassInbox = true;

            foreach (var service in provider.GetServices<IHostedService>())
            {
                service.StartAsync(default).GetAwaiter().GetResult();
            }

            return provider;
        }

        private static async Task SyncDefinitions(ServiceProvider provider)
        {
            var registry = provider.GetRequiredService<IWorkflowBuilder>();
            registry.RegisterWorkflow<BenchmarkLoopWorkflow>("BenchmarkLoopWorkflow", 1);
            registry.RegisterSignal<BenchmarkSignal>("BenchTick");

            var packageField = registry.GetType()
                .GetField("registrationPackage", BindingFlags.NonPublic | BindingFlags.Instance);
            if (packageField == null)
            {
                throw new InvalidOperationException("IWorkflowBuilder no longer exposes the registration package field.");
            }

            var package = (BulkRegistrationPackage)packageField.GetValue(registry)!;

            using var scope = provider.CreateScope();
            var definitions = scope.ServiceProvider.GetRequiredService<IDefinitionRepository>();
            var result = await definitions.SyncDefinitionsAsync(package);
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    "Definition sync failed: " + string.Join("; ", result.Errors.Select(e => e.Message)));
            }
        }
    }
}