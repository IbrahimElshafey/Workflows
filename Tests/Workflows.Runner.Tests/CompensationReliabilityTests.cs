using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Schema.Generation;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Runner;
using Workflows.Abstraction.Persistence;
using Workflows.Definition;
using Workflows.Definition.Registration;
using Workflows.Admin.UI.Services;
using Workflows.Hosting.InProcess;
using Workflows.Primitives;
using Workflows.Runner;
using Workflows.Runner.Tests.Infrastructure;
using Workflows.Runner.Tests.TestWorkflows;
using Workflows.Storage.EntityFrameworkCore;
using Workflows.Shared;
using Xunit;

namespace Workflows.Runner.Tests
{
    /// <summary>
    /// Saga reliability tests: retry/backoff, DLQ emission on exhaustion, manual retry idempotency and the
    /// distinction between a rollback that stopped mid-way (CompensationFailed) and one that finished (Canceled).
    /// </summary>
    public class CompensationReliabilityTests
    {
        private const string WorkflowType = "CompensationTestWorkflow";
        private const string SagaToken = "OrderSaga";

        private sealed class RecordingDlqPublisher : IDlqPublisher
        {
            public List<DlqMessage> Messages { get; } = new();

            public Task PublishAsync(DlqMessage message)
            {
                Messages.Add(message);
                return Task.CompletedTask;
            }
        }

        private sealed class Harness : IDisposable
        {
            private readonly SqliteConnection _connection;

            public ServiceProvider Provider { get; }
            public RecordingDlqPublisher Publisher { get; } = new();

            public Harness(CompensationWorkerOptions options)
            {
                var dbName = $"CompensationReliability_{Guid.NewGuid():N}";
                var services = new ServiceCollection();

                services.AddWorkflowsShared();
                services.AddWorkflowsRunner();
                services.AddSingleton<JSchemaGenerator, MockSchemaGenerator>();
                services.AddSingleton<ICommandHandlerFactory, InMemoryCommandHandlerFactory>();

                // Registered before the host so the TryAddSingleton defaults do not win.
                services.AddSingleton(options);
                services.AddSingleton<IDlqPublisher>(Publisher);

                var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
                _connection = new SqliteConnection(connectionString);
                _connection.Open();

                services.AddWorkflowsInProcessHost(connectionString);

                Provider = services.BuildServiceProvider();

                using (var scope = Provider.CreateScope())
                {
                    scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>().Database.EnsureCreated();
                }

                var builder = Provider.GetRequiredService<IWorkflowBuilder>();
                builder.RegisterWorkflow<CompensationTestWorkflow>(WorkflowType, 1);
            }

            public CompensationWorker Worker => Provider.GetServices<IHostedService>().OfType<CompensationWorker>().Single();

            /// <summary>
            /// Creates an InError instance that has one completed command wait (with a compensation callback)
            /// and one waiting compensation wait, i.e. the state the compensation worker expects.
            /// </summary>
            public async Task<Guid> SeedInErrorInstanceAsync(string handlerKey, string? commandCompensationToken = null)
            {
                var instanceId = Guid.NewGuid();

                var now = DateTime.UtcNow;
                var commandWait = new CommandWaitDto
                {
                    Id = "cmd-" + Guid.NewGuid().ToString("N"),
                    WaitName = handlerKey,
                    WaitType = WaitType.Command,
                    Status = WaitStatus.Completed,
                    HandlerKey = handlerKey,
                    CommandResult = "{}",
                    CompensationTokens = new[] { commandCompensationToken ?? SagaToken },
                    Created = now
                };

                var compensationWait = new CompensationWaitDto
                {
                    Id = "comp-" + Guid.NewGuid().ToString("N"),
                    WaitName = SagaToken,
                    WaitType = WaitType.Compensation,
                    Status = WaitStatus.Waiting,
                    Token = SagaToken,
                    Created = now
                };

                var state = new WorkflowStateDto
                {
                    Id = instanceId,
                    Created = now,
                    Status = WorkflowInstanceStatus.InError,
                    WorkflowType = WorkflowType,
                    WorkflowVersion = 1,
                    StateObject = new WorkflowStateObject
                    {
                        Instance = new CompensationTestWorkflow(),
                        Locals = new Dictionary<string, object?>()
                    },
                    Waits = new List<WaitInfrastructureDto> { commandWait, compensationWait },
                    CancellationHistory = new List<CancellationHistoryEntry>()
                };

                using var scope = Provider.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
                await store.SaveContextSyncAsync(state, Array.Empty<string>());

                return instanceId;
            }

            public void RegisterCompensation(string handlerKey, Func<object, ValueTask> callback)
            {
                Provider.GetRequiredService<ICallbackRegistry>()
                    .Register(handlerKey + ":Compensation", callback);
            }

            public async Task<InstanceSnapshot> ReadInstanceAsync(Guid instanceId)
            {
                using var scope = Provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();

                var instance = await db.WorkflowInstances.AsNoTracking().FirstAsync(i => i.Id == instanceId);
                var wait = instance.Waits.OfType<CompensationWaitDto>().Single();

                return new InstanceSnapshot(
                    (WorkflowInstanceStatus)instance.Status,
                    wait.Status,
                    wait.Attempts,
                    wait.LastError,
                    wait.NextAttemptUtc,
                    instance.ErrorMessage);
            }

            public async Task<int> CountDlqRowsAsync(Guid instanceId)
            {
                using var scope = Provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkflowsDbContext>();
                return await db.DlqMessages.CountAsync(d => d.WorkflowInstanceId == instanceId);
            }

            public void Dispose()
            {
                Provider.Dispose();
                _connection.Dispose();
            }
        }

        private sealed record InstanceSnapshot(
            WorkflowInstanceStatus Status,
            WaitStatus CompensationWaitStatus,
            int Attempts,
            string? LastError,
            DateTime? NextAttemptUtc,
            string? ErrorMessage);

        [Fact]
        public async Task ThrowingCompensation_ShouldMarkInstanceCompensationFailed_AndPublishSingleDlqMessage()
        {
            var options = new CompensationWorkerOptions { MaxAttempts = 1, InitialDelay = TimeSpan.FromMilliseconds(1) };
            using var harness = new Harness(options);
            var instanceId = await harness.SeedInErrorInstanceAsync("ReserveInventory");

            harness.RegisterCompensation("ReserveInventory", _ => throw new InvalidOperationException("refund gateway down"));

            var worker = harness.Worker;
            await worker.ProcessCompensationAsync(instanceId);

            // A second cycle must be a no-op: the instance is parked and the DLQ is not duplicated.
            await worker.ProcessCompensationAsync(instanceId);

            var snapshot = await harness.ReadInstanceAsync(instanceId);
            snapshot.Status.Should().Be(WorkflowInstanceStatus.CompensationFailed);
            snapshot.CompensationWaitStatus.Should().Be(WaitStatus.InError);
            snapshot.Attempts.Should().Be(1);
            snapshot.LastError.Should().Contain("refund gateway down");
            snapshot.ErrorMessage.Should().NotBeNull();

            harness.Publisher.Messages.Should().HaveCount(1);
            var message = harness.Publisher.Messages[0];
            message.InstanceId.Should().Be(instanceId);
            message.WorkflowType.Should().Be(WorkflowType);
            message.FailureCount.Should().Be(1);
            message.ErrorMessage.Should().NotBeNull();
            message.PayloadJson.Should().NotBeNull();
            message.FailedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

            (await harness.CountDlqRowsAsync(instanceId)).Should().Be(1);
        }

        [Fact]
        public async Task Compensation_ShouldSucceed_OnLastAllowedAttempt_WithoutCompensationFailedOrDlq()
        {
            var options = new CompensationWorkerOptions
            {
                MaxAttempts = 2,
                InitialDelay = TimeSpan.FromMilliseconds(700),
                BackoffMultiplier = 2,
                MaxDelay = TimeSpan.FromSeconds(1)
            };
            using var harness = new Harness(options);
            var instanceId = await harness.SeedInErrorInstanceAsync("ReserveInventory");

            var invocations = 0;
            harness.RegisterCompensation("ReserveInventory", _ =>
            {
                invocations++;
                if (invocations < 2)
                {
                    throw new InvalidOperationException("transient refund failure");
                }
                return ValueTask.CompletedTask;
            });

            var worker = harness.Worker;

            // Attempt 1 fails but is retryable.
            await worker.ProcessCompensationAsync(instanceId);
            var afterFirst = await harness.ReadInstanceAsync(instanceId);
            afterFirst.Status.Should().Be(WorkflowInstanceStatus.InError);
            afterFirst.CompensationWaitStatus.Should().Be(WaitStatus.Waiting);
            afterFirst.Attempts.Should().Be(1);
            afterFirst.NextAttemptUtc.Should().NotBeNull();
            harness.Publisher.Messages.Should().BeEmpty();

            // Backoff still active: the sweeper must skip the wait.
            await worker.ProcessCompensationAsync(instanceId);
            invocations.Should().Be(1);

            await Task.Delay(1100);

            // Attempt 2 succeeds.
            await worker.ProcessCompensationAsync(instanceId);
            invocations.Should().Be(2);

            var snapshot = await harness.ReadInstanceAsync(instanceId);
            snapshot.Status.Should().Be(WorkflowInstanceStatus.Canceled);
            snapshot.CompensationWaitStatus.Should().Be(WaitStatus.Completed);
            snapshot.LastError.Should().BeNull();
            snapshot.NextAttemptUtc.Should().BeNull();
            harness.Publisher.Messages.Should().BeEmpty();
            (await harness.CountDlqRowsAsync(instanceId)).Should().Be(0);
        }

        [Fact]
        public async Task RetryCompensation_ShouldBeIdempotent_AndNotDuplicateDlqEntries()
        {
            var options = new CompensationWorkerOptions { MaxAttempts = 1, InitialDelay = TimeSpan.FromMilliseconds(1) };
            using var harness = new Harness(options);
            var instanceId = await harness.SeedInErrorInstanceAsync("ReserveInventory");

            harness.RegisterCompensation("ReserveInventory", _ => throw new InvalidOperationException("permanent refund failure"));

            await harness.Worker.ProcessCompensationAsync(instanceId);

            (await harness.ReadInstanceAsync(instanceId)).Status.Should().Be(WorkflowInstanceStatus.CompensationFailed);
            harness.Publisher.Messages.Should().HaveCount(1);

            using var scope = harness.Provider.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();
            var commandService = new AdminCommandService(store: store);

            commandService.CanRetryCompensation.Should().BeTrue();

            (await commandService.RetryCompensationAsync(instanceId)).Should().BeTrue();
            (await commandService.RetryCompensationAsync(instanceId)).Should().BeFalse();

            var snapshot = await harness.ReadInstanceAsync(instanceId);
            snapshot.Status.Should().Be(WorkflowInstanceStatus.InError);
            snapshot.CompensationWaitStatus.Should().Be(WaitStatus.Waiting);
            snapshot.Attempts.Should().Be(0);
            snapshot.NextAttemptUtc.Should().BeNull();
            snapshot.ErrorMessage.Should().BeNull();

            harness.Publisher.Messages.Should().HaveCount(1);
            (await harness.CountDlqRowsAsync(instanceId)).Should().Be(1);

            // The retry requeued exactly one compensation cycle: it runs again and fails again.
            await harness.Worker.ProcessCompensationAsync(instanceId);
            var afterRetryFailure = await harness.ReadInstanceAsync(instanceId);
            afterRetryFailure.Status.Should().Be(WorkflowInstanceStatus.CompensationFailed);
            afterRetryFailure.Attempts.Should().Be(1);

            // Two retry calls queued one cycle, so only one additional DLQ entry (from the new failure).
            harness.Publisher.Messages.Should().HaveCount(2);
            (await harness.CountDlqRowsAsync(instanceId)).Should().Be(2);
        }

        [Fact]
        public async Task MidRollbackFailure_ShouldBeDistinguishable_FromFullyRolledBackInstance()
        {
            var options = new CompensationWorkerOptions { MaxAttempts = 1, InitialDelay = TimeSpan.FromMilliseconds(1) };
            using var harness = new Harness(options);

            var rolledBackId = await harness.SeedInErrorInstanceAsync("ReserveInventory");
            var stuckId = await harness.SeedInErrorInstanceAsync("ProcessPayment");

            harness.RegisterCompensation("ReserveInventory", _ => ValueTask.CompletedTask);
            harness.RegisterCompensation("ProcessPayment", _ => throw new InvalidOperationException("refund rejected"));

            var worker = harness.Worker;
            await worker.ProcessCompensationAsync(rolledBackId);
            await worker.ProcessCompensationAsync(stuckId);

            var rolledBack = await harness.ReadInstanceAsync(rolledBackId);
            rolledBack.Status.Should().Be(WorkflowInstanceStatus.Canceled);
            rolledBack.CompensationWaitStatus.Should().Be(WaitStatus.Completed);

            var stuck = await harness.ReadInstanceAsync(stuckId);
            stuck.Status.Should().Be(WorkflowInstanceStatus.CompensationFailed);
            stuck.CompensationWaitStatus.Should().Be(WaitStatus.InError);
            stuck.ErrorMessage.Should().Contain("refund rejected");

            rolledBack.Status.Should().NotBe(stuck.Status);
            harness.Publisher.Messages.Should().ContainSingle(m => m.InstanceId == stuckId);
            harness.Publisher.Messages.Should().NotContain(m => m.InstanceId == rolledBackId);
        }

        /// <summary>
        /// A completed command whose compensation handler was never registered (worker restarted, handler
        /// removed in a later version) must not be reported as a successful rollback: the instance has to be
        /// parked as CompensationFailed with a DLQ row, never as Canceled.
        /// </summary>
        [Fact]
        public async Task UnregisteredCompensationHandler_ShouldEndInCompensationFailed_NotCanceled()
        {
            var options = new CompensationWorkerOptions { MaxAttempts = 1, InitialDelay = TimeSpan.FromMilliseconds(1) };
            using var harness = new Harness(options);
            var instanceId = await harness.SeedInErrorInstanceAsync("ReserveInventory");

            // Deliberately do not register a compensation callback for ReserveInventory.
            var worker = harness.Worker;
            await worker.ProcessCompensationAsync(instanceId);
            await worker.ProcessCompensationAsync(instanceId);

            var snapshot = await harness.ReadInstanceAsync(instanceId);
            snapshot.Status.Should().Be(WorkflowInstanceStatus.CompensationFailed);
            snapshot.Status.Should().NotBe(WorkflowInstanceStatus.Canceled);
            snapshot.CompensationWaitStatus.Should().Be(WaitStatus.InError);
            snapshot.LastError.Should().Contain("ReserveInventory");
            snapshot.ErrorMessage.Should().NotBeNull();

            harness.Publisher.Messages.Should().HaveCount(1);
            (await harness.CountDlqRowsAsync(instanceId)).Should().Be(1);
        }

        /// <summary>
        /// A compensation wait whose token never matched a completed command has nothing to undo, so the
        /// rollback legitimately completes as Canceled without a DLQ entry.
        /// </summary>
        [Fact]
        public async Task CompensationWithoutMatchingCommands_ShouldCompleteAsCanceled()
        {
            var options = new CompensationWorkerOptions { MaxAttempts = 1, InitialDelay = TimeSpan.FromMilliseconds(1) };
            using var harness = new Harness(options);

            // The command carries a different token, so nothing matches the saga token.
            var instanceId = await harness.SeedInErrorInstanceAsync("ReserveInventory", commandCompensationToken: "SomeOtherSaga");
            harness.RegisterCompensation("ReserveInventory", _ => throw new InvalidOperationException("must not be invoked"));

            await harness.Worker.ProcessCompensationAsync(instanceId);

            var snapshot = await harness.ReadInstanceAsync(instanceId);
            snapshot.Status.Should().Be(WorkflowInstanceStatus.Canceled);
            snapshot.CompensationWaitStatus.Should().Be(WaitStatus.Completed);
            harness.Publisher.Messages.Should().BeEmpty();
            (await harness.CountDlqRowsAsync(instanceId)).Should().Be(0);
        }
    }
}