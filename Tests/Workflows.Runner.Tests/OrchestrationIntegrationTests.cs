using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FluentAssertions;
using Newtonsoft.Json.Schema.Generation;
using Xunit;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Registration;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Definition.Registration;
using Workflows.Hosting.InProcess;
using Workflows.Orchestrator;
using Workflows.Storage.EntityFrameworkCore;
using Workflows.Primitives;
using Workflows.Runner;
using Workflows.Runner.Tests.TestData;
using Workflows.Runner.Tests.TestWorkflows;
using Workflows.Runner.Tests.Infrastructure;
using Workflows.Shared;

namespace Workflows.Runner.Tests
{
    public class MockSchemaGenerator : JSchemaGenerator
    {
        public override Newtonsoft.Json.Schema.JSchema Generate(Type type)
        {
            return Newtonsoft.Json.Schema.JSchema.Parse("{}");
        }

        public override Newtonsoft.Json.Schema.JSchema Generate(Type type, bool rootSchemaNullable)
        {
            return Newtonsoft.Json.Schema.JSchema.Parse("{}");
        }
    }

    public class OrchestrationIntegrationTests
    {
        private ServiceProvider CreateServiceProvider(string dbName, out SqliteConnection connection)
        {
            var services = new ServiceCollection();

            // 1. Shared / Common / Runner dependencies
            services.AddWorkflowsShared();
            services.AddWorkflowsRunner();
            services.AddSingleton<JSchemaGenerator, MockSchemaGenerator>();
            services.AddSingleton<ICommandHandlerFactory, InMemoryCommandHandlerFactory>();

            // 2. In-Process Host dependencies
            var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
            connection = new SqliteConnection(connectionString);
            connection.Open();

            services.AddWorkflowsInProcessHost(connectionString);

            var provider = services.BuildServiceProvider();

            // Ensure DB schema is created
            using (var scope = provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
                context.Database.EnsureCreated();
            }

            return provider;
        }

        private async Task SyncDefinitions(ServiceProvider provider)
        {
            var registry = provider.GetRequiredService<IWorkflowBuilder>();
            registry.RegisterWorkflow<FirstWaitAndResumeWorkflow>("FirstWaitTest", 1);
            registry.RegisterWorkflow<ShortDelayWorkflow>("ShortDelayWorkflow", 1);
            registry.RegisterWorkflow<EnumMatchingWorkflow>("EnumMatchingWorkflow", 1);
            registry.RegisterSignal<OrderReceivedSignal>("OrderReceived");
            registry.RegisterSignal<OrderReceivedSignal>("DummyOrderReceived");
            registry.RegisterSignal<PaymentConfirmedSignal>("Payment1");
            registry.RegisterSignal<PaymentConfirmedSignal>("Payment2");
            registry.RegisterSignal<ShipmentSignal>("FinalShipment");
            registry.RegisterCommand<ProcessPaymentCommand, ProcessPaymentResult>("ProcessPayment", default, CommandExecutionMode.Deferred);

            // Extract the registration package using reflection
            var packageField = typeof(WorkflowBuilder).GetField("registrationPackage", BindingFlags.NonPublic | BindingFlags.Instance);
            var package = (BulkRegistrationPackage)packageField!.GetValue(registry)!;

            // Sync definitions to EF DB
            using (var scope = provider.CreateScope())
            {
                var defRepo = scope.ServiceProvider.GetRequiredService<IDefinitionRepository>();
                var syncResult = await defRepo.SyncDefinitionsAsync(package);
                syncResult.Success.Should().BeTrue();
            }
        }

        [Fact]
        public async Task FullWorkflowLifecycle_ShouldPersist_Hydrate_Prune_AndSucceed()
        {
            var dbName = $"OrchestratorIntegration_{Guid.NewGuid():N}";
            using var provider = CreateServiceProvider(dbName, out var connection);
            await SyncDefinitions(provider);

            using var mainScope = provider.CreateScope();
            var orchestrator = mainScope.ServiceProvider.GetRequiredService<IOrchestrator>();
            var workflowStore = mainScope.ServiceProvider.GetRequiredService<IWorkflowStore>();
            var dbContext = mainScope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();

            // Step 1: Start Workflow with reflection-mapped input
            var instanceId = await orchestrator.StartWorkflowAsync("FirstWaitTest", 1, new { ResumeCount = 7 });
            instanceId.Should().NotBeEmpty();

            dbContext.ChangeTracker.Clear();

            // Verify state is persisted in DB
            var dbState = await dbContext.WorkflowInstances.FindAsync(instanceId);
            dbState.Should().NotBeNull();
            dbState!.Status.Should().Be((int)WorkflowInstanceStatus.Running);

            // Hydrate state and verify the input was mapped correctly via reflection
            var state = await workflowStore.GetInstanceStateAsync(instanceId);
            state.Should().NotBeNull();
            var instance = state!.StateObject.Instance as FirstWaitAndResumeWorkflow;
            instance.Should().NotBeNull();
            instance!.ResumeCount.Should().Be(7);
            state.Waits.Should().HaveCount(1);
            state.Waits.First().Should().BeOfType<SignalWaitDto>();
            state.Waits.First().WaitName.Should().Be("First wait");
            var firstWaitId = state.Waits.First().Id;

            // Step 2: Send Signal to advance past First wait
            await orchestrator.ProcessSignalAsync(new SignalDto
            {
                SignalIdentifier = "OrderReceived",
                Data = new OrderReceivedSignal { OrderId = "ORD-999", Amount = 1500 }
            });

            dbContext.ChangeTracker.Clear();

            // Verify first wait is completed and second wait (Command) is created
            state = await workflowStore.GetInstanceStateAsync(instanceId);
            state.Should().NotBeNull();
            state!.Waits.Should().HaveCount(2);
            var commandWait = (CommandWaitDto)state.Waits.First(w => w.Status == WaitStatus.Waiting);
            commandWait.WaitName.Should().Be("ProcessPayment");

            // Verify the local state of ResumeCount incremented in the runner
            var instanceStep2 = state.StateObject.Instance as FirstWaitAndResumeWorkflow;
            instanceStep2!.ResumeCount.Should().Be(8, because: $"ExecutionLog: {string.Join(" | ", instanceStep2.ExecutionLog)}");

            // Verify DB has the new command wait (TPC: look up in CommandWaits)
            var newWaitInDb = await dbContext.CommandWaits.FindAsync(commandWait.Id);
            newWaitInDb.Should().NotBeNull();
            // Verify the old wait we completed is STILL in DB with Completed status
            var firstWaitRecord = await dbContext.SignalWaits.FindAsync(firstWaitId);
            firstWaitRecord.Should().NotBeNull();
            firstWaitRecord!.Status.Should().Be((int)WaitStatus.Completed);


            // Step 3: Send Command Result to advance past second wait to Group wait
            await orchestrator.ProcessCommandResultAsync(new CommandResultDto
            {
                CommandWaitId = commandWait.Id,
                Result = new ProcessPaymentResult { Success = true, TransactionId = "TX-ABCD" }
            });

            dbContext.ChangeTracker.Clear();

            // Verify we advanced to GroupWaitDto containing two children
            state = await workflowStore.GetInstanceStateAsync(instanceId);
            state.Should().NotBeNull();
            state!.Waits.Should().HaveCount(3);
            var groupWait = (GroupWaitDto)state.Waits.First(w => w.Status == WaitStatus.Waiting);
            groupWait.WaitName.Should().Be("PaymentGroup");
            groupWait.ChildWaits.Should().HaveCount(2);
            groupWait.ChildWaits[0].ParentWaitId.Should().Be(groupWait.Id);
            groupWait.ChildWaits[1].ParentWaitId.Should().Be(groupWait.Id);
            var groupWaitId = groupWait.Id;
            var child1Id = groupWait.ChildWaits[0].Id;
            var child2Id = groupWait.ChildWaits[1].Id;

            var instanceStep3 = state.StateObject.Instance as FirstWaitAndResumeWorkflow;
            instanceStep3!.ResumeCount.Should().Be(9);

            // Step 4: Fire matching signal for MatchAny group wait and check sibling pruning
            await orchestrator.ProcessSignalAsync(new SignalDto
            {
                SignalIdentifier = "Payment1",
                Data = new PaymentConfirmedSignal { TransactionId = "TX-CONFIRMED-1" }
            });

            dbContext.ChangeTracker.Clear();

            // Verify group wait and child waits are in the DB with correct statuses, and we advanced to delay wait
            state = await workflowStore.GetInstanceStateAsync(instanceId);
            state.Should().NotBeNull();
            state!.Waits.Should().HaveCount(4);
            var timeWait = (TimeWaitDto)state.Waits.First(w => w.Status == WaitStatus.Waiting);
            timeWait.WaitName.Should().Be("DelayWait");

            var instanceStep4 = state.StateObject.Instance as FirstWaitAndResumeWorkflow;
            instanceStep4!.ResumeCount.Should().Be(10);

            // Verify all group/child wait records were updated with correct terminal status in the database.
            // With TPC there is no shared WorkflowWaits base table; check each concrete set.
            var dbChild1 = await dbContext.SignalWaits.FindAsync(child1Id);
            var dbChild2 = await dbContext.SignalWaits.FindAsync(child2Id);
            dbChild1.Should().NotBeNull();
            dbChild2.Should().NotBeNull();
            var childStatuses = new[] { dbChild1!.Status, dbChild2!.Status };
            childStatuses.Should().BeEquivalentTo(new[] { (int)WaitStatus.Completed, (int)WaitStatus.Canceled });




            connection.Close();
        }

        [Fact]
        public async Task Scheduler_ShouldFireBackgroundTimer_AndResumeWorkflow()
        {
            var dbName = $"OrchestratorIntegration_{Guid.NewGuid():N}";
            using var provider = CreateServiceProvider(dbName, out var connection);
            await SyncDefinitions(provider);

            using var mainScope = provider.CreateScope();
            var orchestrator = mainScope.ServiceProvider.GetRequiredService<IOrchestrator>();
            var scheduler = mainScope.ServiceProvider.GetRequiredService<Scheduler>();
            var workflowStore = mainScope.ServiceProvider.GetRequiredService<IWorkflowStore>();

            ShortDelayWorkflow.Completed = false;

            // Start the background scheduler service
            await scheduler.StartAsync(default);

            try
            {
                // Start the ShortDelayWorkflow
                var instanceId = await orchestrator.StartWorkflowAsync("ShortDelayWorkflow", 1, null);
                instanceId.Should().NotBeEmpty();

                // Wait for the scheduler loop to process the 50ms timer wait and persist to DB
                int attempts = 0;
                WorkflowStateDto? state = null;
                while (attempts < 30)
                {
                    using (var scope = provider.CreateScope())
                    {
                        var freshStore = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
                        state = await freshStore.GetInstanceStateAsync(instanceId);
                    }
                    if (state != null && state.Status == WorkflowInstanceStatus.Completed)
                    {
                        break;
                    }
                    await Task.Delay(100);
                    attempts++;
                }

                ShortDelayWorkflow.Completed.Should().BeTrue();

                // Hydrate from database and verify status is Completed
                state.Should().NotBeNull();
                state!.Status.Should().Be(WorkflowInstanceStatus.Completed);
                state.Waits.Should().NotBeEmpty();
                state.Waits.Should().OnlyContain(w => w.Status == WaitStatus.Completed);
            }
            finally
            {
                await scheduler.StopAsync(default);
                connection.Close();
            }
        }

        [Fact]
        public async Task ProcessSignal_WithJsonEnumPayload_ShouldConvertIntegerEnumAndSucceed()
        {
            var dbName = $"OrchestratorIntegration_{Guid.NewGuid():N}";
            using var provider = CreateServiceProvider(dbName, out var connection);
            await SyncDefinitions(provider);

            using var mainScope = provider.CreateScope();
            var orchestrator = mainScope.ServiceProvider.GetRequiredService<IOrchestrator>();
            var workflowStore = mainScope.ServiceProvider.GetRequiredService<IWorkflowStore>();
            var dbContext = mainScope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();

            // Start the EnumMatchingWorkflow
            var instanceId = await orchestrator.StartWorkflowAsync("EnumMatchingWorkflow", 1, null);
            instanceId.Should().NotBeEmpty();

            // Verify a wait is registered
            var state = await workflowStore.GetInstanceStateAsync(instanceId);
            state.Should().NotBeNull();
            state!.Waits.Should().HaveCount(1);
            state.Waits.First().Should().BeOfType<SignalWaitDto>();

            // Send signal with "Status" as an integer (3 for TaskStatus.Running)
            string jsonPayload = "{\"OrderId\":\"ORD-ENUM-TEST\",\"Amount\":500,\"Status\":3}";
            await orchestrator.ProcessSignalAsync(new SignalDto
            {
                SignalIdentifier = "OrderReceived",
                Data = jsonPayload
            });

            dbContext.ChangeTracker.Clear();

            // Verify the workflow completed successfully
            state = await workflowStore.GetInstanceStateAsync(instanceId);
            state.Should().NotBeNull();
            state!.Status.Should().Be(WorkflowInstanceStatus.Completed);
            state.Waits.Should().NotBeEmpty();
            state.Waits.Should().OnlyContain(w => w.Status == WaitStatus.Completed);

            var instance = state.StateObject.Instance as EnumMatchingWorkflow;
            instance.Should().NotBeNull();
            instance!.Completed.Should().BeTrue();
            instance.ReceivedStatus.Should().Be(System.Threading.Tasks.TaskStatus.Running);

            connection.Close();
        }

        [Fact]
        public async Task Tier15Matching_ShouldFilterOutMismatchedSignals_AndProcessMatchedSignals()
        {
            var dbName = $"OrchestratorIntegration_{Guid.NewGuid():N}";
            using var provider = CreateServiceProvider(dbName, out var connection);
            await SyncDefinitions(provider);

            using var mainScope = provider.CreateScope();
            var orchestrator = mainScope.ServiceProvider.GetRequiredService<IOrchestrator>();
            var workflowStore = mainScope.ServiceProvider.GetRequiredService<IWorkflowStore>();
            var dbContext = mainScope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();

            // Step 1: Start Workflow with minAmount = 1000 in FirstWaitAndResumeWorkflow
            var instanceId = await orchestrator.StartWorkflowAsync("FirstWaitTest", 1, new { ResumeCount = 7 });
            instanceId.Should().NotBeEmpty();

            dbContext.ChangeTracker.Clear();

            // Step 2: Send mismatched signal (Amount = 500 <= 1000)
            await orchestrator.ProcessSignalAsync(new SignalDto
            {
                SignalIdentifier = "OrderReceived",
                Data = new OrderReceivedSignal { OrderId = "ORD-LOW", Amount = 500 }
            });

            dbContext.ChangeTracker.Clear();

            // Verify it was filtered out by Tier 1.5 and is still waiting on First Wait
            var state = await workflowStore.GetInstanceStateAsync(instanceId);
            state.Should().NotBeNull();
            state!.Status.Should().Be(WorkflowInstanceStatus.Running);
            state.Waits.Should().HaveCount(1);
            state.Waits.First().Should().BeOfType<SignalWaitDto>();
            state.Waits.First().WaitName.Should().Be("First wait");

            // Step 3: Send matched signal (Amount = 1500 > 1000)
            await orchestrator.ProcessSignalAsync(new SignalDto
            {
                SignalIdentifier = "OrderReceived",
                Data = new OrderReceivedSignal { OrderId = "ORD-HIGH", Amount = 1500 }
            });

            dbContext.ChangeTracker.Clear();

            // Verify it matched and progressed past First Wait to the command wait
            state = await workflowStore.GetInstanceStateAsync(instanceId);
            state.Should().NotBeNull();
            state!.Waits.Should().HaveCount(2);
            var activeWait = state.Waits.First(w => w.Status == WaitStatus.Waiting);
            activeWait.Should().BeOfType<CommandWaitDto>();
            activeWait.WaitName.Should().Be("ProcessPayment");

            connection.Close();
        }

        [Fact]
        public async Task ProcessSignal_WithMultipleCandidateInstances_ShouldEvaluateSequentially_AndStopOnFirstMatch()
        {
            var dbName = $"OrchestratorIntegration_{Guid.NewGuid():N}";
            using var provider = CreateServiceProvider(dbName, out var connection);
            
            // Register workflow
            var registry = provider.GetRequiredService<IWorkflowBuilder>();
            registry.RegisterWorkflow<DynamicThresholdWorkflow>("ThresholdWorkflow", 1);
            registry.RegisterSignal<OrderReceivedSignal>("OrderReceived");

            var packageField = typeof(WorkflowBuilder).GetField("registrationPackage", BindingFlags.NonPublic | BindingFlags.Instance);
            var package = (BulkRegistrationPackage)packageField!.GetValue(registry)!;

            using (var scope = provider.CreateScope())
            {
                var defRepo = scope.ServiceProvider.GetRequiredService<IDefinitionRepository>();
                await defRepo.SyncDefinitionsAsync(package);
            }

            using var mainScope = provider.CreateScope();
            var orchestrator = mainScope.ServiceProvider.GetRequiredService<IOrchestrator>();
            var workflowStore = mainScope.ServiceProvider.GetRequiredService<IWorkflowStore>();
            var dbContext = mainScope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();

            // Start Instance 1 (high threshold = 1000)
            var id1 = await orchestrator.StartWorkflowAsync("ThresholdWorkflow", 1, new { Threshold = 1000 });
            
            // Start Instance 2 (low threshold = 100)
            var id2 = await orchestrator.StartWorkflowAsync("ThresholdWorkflow", 1, new { Threshold = 100 });

            dbContext.ChangeTracker.Clear();

            // Act - Send signal (Amount = 500)
            // Amount = 500 <= 1000 (Instance 1 mismatch)
            // Amount = 500 > 100   (Instance 2 match)
            await orchestrator.ProcessSignalAsync(new SignalDto
            {
                SignalIdentifier = "OrderReceived",
                Data = new OrderReceivedSignal { OrderId = "ORD-SEQ-TEST", Amount = 500 }
            });

            dbContext.ChangeTracker.Clear();

            // Assert
            // Instance 1 should still be running (did not match)
            var state1 = await workflowStore.GetInstanceStateAsync(id1);
            state1.Should().NotBeNull();
            state1!.Status.Should().Be(WorkflowInstanceStatus.Running);
            var instance1 = state1.StateObject.Instance as DynamicThresholdWorkflow;
            instance1.Should().NotBeNull();
            instance1!.Completed.Should().BeFalse();

            // Instance 2 should be completed (matched)
            var state2 = await workflowStore.GetInstanceStateAsync(id2);
            state2.Should().NotBeNull();
            state2!.Status.Should().Be(WorkflowInstanceStatus.Completed);
            var instance2 = state2.StateObject.Instance as DynamicThresholdWorkflow;
            instance2.Should().NotBeNull();
            instance2!.Completed.Should().BeTrue();

            connection.Close();
        }
        
    }

    [Workflow("ShortDelayWorkflow", 1)]
    public sealed class ShortDelayWorkflow : WorkflowContainer
    {
        public static bool Completed { get; set; }

        public override async IAsyncEnumerable<Wait> Run()
        {
            var delay = WaitDelay(TimeSpan.FromMilliseconds(50), "ShortDelay", "50ms delay");
            var dummySignal = WaitSignal<OrderReceivedSignal>("DummyOrderReceived", "Dummy");
            yield return WaitGroup(new Wait[] { delay, (SignalWait<OrderReceivedSignal>)dummySignal }, "DelayGroup").MatchAny();
            Completed = true;
        }
    }

    [Workflow("EnumMatchingWorkflow", 1)]
    public sealed class EnumMatchingWorkflow : WorkflowContainer
    {
        public bool Completed { get; set; }
        public System.Threading.Tasks.TaskStatus ReceivedStatus { get; set; }

        public override async IAsyncEnumerable<Wait> Run()
        {
            yield return WaitSignal<OrderReceivedSignal>("OrderReceived", "EnumWait")
                .MatchIf(signal => signal.Status == System.Threading.Tasks.TaskStatus.Running)
                .AfterMatch(signal =>
                {
                    this.ReceivedStatus = signal.Status;
                    this.Completed = true;
                });
        }
    }

    [Workflow("ThresholdWorkflow", 1)]
    public sealed class DynamicThresholdWorkflow : WorkflowContainer
    {
        public bool Completed { get; set; }
        public int Threshold { get; set; }

        public override async IAsyncEnumerable<Wait> Run()
        {
            yield return WaitSignal<OrderReceivedSignal>("OrderReceived", "ThresholdWait")
                .WithState(Threshold)
                .MatchIf((signal, limit) => signal.Amount > limit)
                .AfterMatch(signal =>
                {
                    this.Completed = true;
                });
        }
    }
}
