using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Persistence;
using Workflows.Definition;
using Workflows.Storage.EntityFrameworkCore;
using Workflows.Communication.Abstraction;
using Workflows.Runner;
using Workflows.Runner.Migration;
using Xunit;

namespace Workflows.Runner.Tests
{
    // Strongly-typed state POCOs
    public class OrderWorkflowAdvancedState_V1
    {
        public Guid OrderId { get; set; }
        public decimal Amount { get; set; }
        public string CustomerId { get; set; } = "";
    }

    public class OrderWorkflowAdvancedState_V2
    {
        public Guid OrderId { get; set; }
        public double Amount { get; set; }
        public string CustomerId { get; set; } = "";
        public int AmountInCents { get; set; }
    }

    // Strongly-typed wrappers
    public class AdvancedOrderWorkflowV1Wrapper : WorkflowStateWrapper<OrderWorkflowAdvancedState_V1>
    {
        public AdvancedOrderWorkflowV1Wrapper(WorkflowStateDto dto, OrderWorkflowAdvancedState_V1 instance)
            : base(dto, instance) { }
    }

    public class AdvancedOrderWorkflowV2Wrapper : WorkflowStateWrapper<OrderWorkflowAdvancedState_V2>
    {
        public AdvancedOrderWorkflowV2Wrapper(WorkflowStateDto dto, OrderWorkflowAdvancedState_V2 instance)
            : base(dto, instance) { }
    }

    // Strongly-typed WorkflowMigration class (NO dynamic types)
    [WorkflowMigration("AdvancedOrderWorkflow", fromVersion: 1, toVersion: 2)]
    public class AdvancedOrderWorkflowMigration_V1_To_V2 
        : WorkflowMigration<AdvancedOrderWorkflowV1Wrapper, AdvancedOrderWorkflowV2Wrapper>
    {
        public override void MigrateState(AdvancedOrderWorkflowV1Wrapper old, AdvancedOrderWorkflowV2Wrapper _new)
        {
            _new.Instance.OrderId = old.Instance.OrderId;
            _new.Instance.CustomerId = old.Instance.CustomerId;
            _new.Instance.Amount = (double)old.Instance.Amount;
            _new.Instance.AmountInCents = (int)(old.Instance.Amount * 100);
        }

        public override void MigrateInstance(AdvancedOrderWorkflowV1Wrapper old, AdvancedOrderWorkflowV2Wrapper _new)
        {
            MigrateState(old, _new);
        }

        public override Wait MigrateActiveWait(WaitInfrastructureDto oldWait, AdvancedOrderWorkflowV2Wrapper _new)
        {
            switch (oldWait.WaitName)
            {
                case "ApprovalWait":
                    return RecreateWait(oldWait.WaitName);

                case "PaymentCallbackWait":
                    return RecreateWait(oldWait.WaitName);

                default:
                    return RecreateWait(oldWait.WaitName);
            }
        }

        public override Wait MigrateSubWorkflowState(SubWorkflowWaitDto oldSubWait, AdvancedOrderWorkflowV2Wrapper _new)
        {
            switch (oldSubWait.MethodFullPath)
            {
                case "PaymentSubWorkflow":
                    return SubWorkflow_RecreateWait(oldSubWait.MethodFullPath, oldSubWait.WaitName);

                default:
                    return SubWorkflow_RecreateWait(oldSubWait.MethodFullPath, oldSubWait.WaitName);
            }
        }
    }

    public class AdvancedMigrationTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<WorkflowsDbContext> _options;

        public AdvancedMigrationTests()
        {
            _connection = new SqliteConnection($"Data Source=InMemory_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            _connection.Open();

            _options = new DbContextOptionsBuilder<WorkflowsDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var context = new WorkflowsDbContext(_options);
            context.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _connection.Close();
            _connection.Dispose();
        }

        [Fact]
        public async Task StronglyTypedMigrationExecutor_ShouldCorrectlyRemapStateIndexAndWaits()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddDbContext<WorkflowsDbContext>(opt => opt.UseSqlite(_connection));
            services.AddScoped<IWorkflowStore, WorkflowStore>();
            services.AddSingleton<IObjectSerializer, Infrastructure.TestObjectSerializer>();
            services.AddSingleton<IExpressionSerializer, Infrastructure.TestExpressionSerializer>();
            services.AddWorkflowsRunner();

            var mockDispatcher = new MockMessageDispatcher();
            services.AddSingleton<IMessageDispatcher>(mockDispatcher);

            // Register strongly typed POCO migration
            services.AddWorkflowMigration<AdvancedOrderWorkflowV1Wrapper, AdvancedOrderWorkflowV2Wrapper, AdvancedOrderWorkflowMigration_V1_To_V2>();

            var sp = services.BuildServiceProvider();

            // Set up a V1 workflow instance paused at "ApprovalWait" (StateIndex in V1 = 1)
            var instanceId = Guid.NewGuid();
            var waitId = Guid.NewGuid().ToString();

            var oldState = new WorkflowStateDto
            {
                Id = instanceId,
                Created = DateTime.UtcNow,
                Status = WorkflowInstanceStatus.Running,
                WorkflowType = "AdvancedOrderWorkflow",
                WorkflowVersion = 1,
                StateObject = new WorkflowStateObject
                {
                    WorkflowType = "AdvancedOrderWorkflow",
                    Instance = new object(),
                    StateIndex = 1, // V1 StateIndex was 1
                    Locals = new Dictionary<string, object>
                    {
                        {
                            "state",
                            new OrderWorkflowAdvancedState_V1
                            {
                                OrderId = instanceId,
                                CustomerId = "CUST-999",
                                Amount = 250.75m
                            }
                        }
                    }
                },
                Waits = new List<WaitInfrastructureDto>
                {
                    new SignalWaitDto
                    {
                        Id = waitId,
                        Status = WaitStatus.Waiting,
                        WaitName = "ApprovalWait",
                        SignalIdentifier = "ApprovalSignal",
                        IsPersisted = false
                    }
                }
            };

            var store = sp.GetRequiredService<IWorkflowStore>();
            await store.SaveContextSyncAsync(oldState, Enumerable.Empty<string>());

            // Write V2 compiled schema sidecar manifest where "ApprovalWait" yield ordinal shifted to 99!
            var schema = new WorkflowVersionManifest
            {
                SchemaVersion = 2,
                WorkflowName = "AdvancedOrderWorkflow",
                MainCfg = new List<BasicBlockSchema>
                {
                    new BasicBlockSchema
                    {
                        BlockIndex = 1,
                        YieldWaitType = "SignalWait",
                        YieldWaitName = "ApprovalWait",
                        YieldOrdinal = 99 // New YieldOrdinal in V2 state machine!
                    }
                }
            };
            var schemaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AdvancedOrderWorkflow_Schema.json");
            File.WriteAllText(schemaPath, JsonConvert.SerializeObject(schema));

            try
            {
                // Resolve executor
                var migrationKey = "AdvancedOrderWorkflow:1:2";
                var executor = sp.GetKeyedService<IWorkflowMigrationExecutor>(migrationKey);
                executor.Should().NotBeNull();

                // Act
                await executor!.MigrateAsync(instanceId, CancellationToken.None);

                // Assert
                var migratedState = await store.GetInstanceStateAsync(instanceId);
                migratedState.Should().NotBeNull();
                migratedState!.WorkflowVersion.Should().Be(2);

                // 1. Verify StateIndex was 100% remapped to V2's YieldOrdinal (99)!
                migratedState.StateObject.StateIndex.Should().Be(99);

                // 2. Verify V2 state property transformations
                migratedState.StateObject.Locals.TryGetValue("state", out var stateObj).Should().BeTrue();
                var v2Json = JsonConvert.SerializeObject(stateObj);
                var v2State = JsonConvert.DeserializeObject<OrderWorkflowAdvancedState_V2>(v2Json);
                v2State.Should().NotBeNull();
                v2State!.OrderId.Should().Be(instanceId);
                v2State.CustomerId.Should().Be("CUST-999");
                v2State.Amount.Should().Be(250.75);
                v2State.AmountInCents.Should().Be(25075);

                // 3. Verify active wait resolution
                migratedState.Waits.Should().ContainSingle();
                var activeWait = migratedState.Waits.Single();
                activeWait.WaitName.Should().Be("ApprovalWait");
                activeWait.StateAfterWait.Should().Be(99);
            }
            finally
            {
                if (File.Exists(schemaPath))
                    File.Delete(schemaPath);
            }
        }

        private class MockMessageDispatcher : IMessageDispatcher
        {
            public List<object> DispatchedMessages { get; } = new();

            public Task DispatchAsync<T>(T message)
            {
                DispatchedMessages.Add(message!);
                return Task.CompletedTask;
            }

            public Task<TResponse> DispatchAndReceiveAsync<TRequest, TResponse>(TRequest message)
            {
                throw new NotImplementedException();
            }
        }
    }
}
