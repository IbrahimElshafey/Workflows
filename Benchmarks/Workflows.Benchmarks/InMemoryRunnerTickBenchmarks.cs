using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Schema.Generation;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;
using Workflows.Definition.Registration;
using Workflows.Runner;
using Workflows.Shared;
using Workflows.TestShell;

namespace Workflows.Benchmarks
{
    /// <summary>
    /// Pure in-memory runner tick: no database, no network. The container instance is built once and every
    /// benchmark operation resumes the looping workflow by one signal, so the measured cost is the runner's
    /// own bookkeeping plus the state serialization round trip a real store performs between runs.
    /// </summary>
    [MemoryDiagnoser]
    public class InMemoryRunnerTickBenchmarks
    {
        private readonly CapturingRunnerClient _client = new();
        private ServiceProvider _provider = null!;
        private IWorkflowRunner _runner = null!;
        private IObjectSerializer _serializer = null!;

        [GlobalSetup]
        public void Setup()
        {
            var services = new ServiceCollection();
            services.AddWorkflowsShared();
            services.AddWorkflowsRunner();
            services.AddSingleton<JSchemaGenerator, MockSchemaGenerator>();
            services.AddSingleton<ICommandHandlerFactory>(new TestShellCommandHandlerFactory());
            services.AddSingleton<IWorkflowRunnerClient>(_client);
            services.AddSingleton<ITemplateRepository>(new InMemoryTemplateRepository());

            _provider = services.BuildServiceProvider();
            _serializer = _provider.GetRequiredService<IObjectSerializer>();
            _runner = _provider.GetRequiredService<IWorkflowRunner>();

            var builder = _provider.GetRequiredService<IWorkflowBuilder>();
            builder.RegisterWorkflow<BenchmarkLoopWorkflow>("BenchmarkLoopWorkflow", 1);
            builder.RegisterSignal<BenchmarkSignal>("BenchTick");

            _runner.StartWorkflow("BenchmarkLoopWorkflow").GetAwaiter().GetResult();
        }

        [GlobalCleanup]
        public void Cleanup() => _provider?.Dispose();

        [Benchmark(Description = "Resume one signal (in-memory, no SQLite)")]
        public void ResumeAfterSignal()
        {
            var state = _client.CurrentState ?? throw new InvalidOperationException("The workflow was not started.");

            var serialized = _serializer.Serialize(state, SerializationScope.CompilerGeneratedClass);
            var resumeState = _serializer.Deserialize<WorkflowStateDto>(serialized!, SerializationScope.CompilerGeneratedClass)!;

            if (_client.ConsumedWaitIds.Count > 0)
            {
                var consumed = new HashSet<string>(_client.ConsumedWaitIds);
                for (var i = resumeState.Waits.Count - 1; i >= 0; i--)
                {
                    if (consumed.Contains(resumeState.Waits[i].Id))
                    {
                        resumeState.Waits.RemoveAt(i);
                    }
                }
            }

            var wait = resumeState.Waits
                .OfType<SignalWaitDto>()
                .First(w => w.Status == WaitStatus.Waiting);

            _runner.RunWorkflowAsync(new WorkflowExecutionRequest
            {
                TriggeringWaitId = wait.Id,
                Signal = new SignalDto
                {
                    SignalIdentifier = wait.SignalIdentifier,
                    Data = new BenchmarkSignal { Value = 1 }
                },
                WorkflowState = resumeState
            }).GetAwaiter().GetResult();
        }
    }
}