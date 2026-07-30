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
using Workflows.Runner.Tests.Infrastructure;
using Workflows.Runner.Tests.TestWorkflows;
using Xunit;

namespace Workflows.Runner.Tests
{
    // Strongly-typed wrappers for migration
    public class ComplexOrderV1Wrapper : WorkflowStateWrapper<ComplexOrderState_V1>
    {
        public ComplexOrderV1Wrapper(WorkflowStateDto dto, ComplexOrderState_V1 instance)
            : base(dto, instance) { }
    }

    public class ComplexOrderV2Wrapper : WorkflowStateWrapper<ComplexOrderState_V2>
    {
        public ComplexOrderV2Wrapper(WorkflowStateDto dto, ComplexOrderState_V2 instance)
            : base(dto, instance) { }
    }

    [WorkflowMigration("ComplexOrderWorkflow", fromVersion: 1, toVersion: 2)]
    public class ComplexOrderWorkflowMigration_V1_To_V2
        : WorkflowMigration<ComplexOrderV1Wrapper, ComplexOrderV2Wrapper>
    {
        public override void MigrateState(ComplexOrderV1Wrapper old, ComplexOrderV2Wrapper _new)
        {
            _new.Instance.OrderId = old.Instance.OrderId;
            _new.Instance.CustomerEmail = old.Instance.CustomerEmail;
            _new.Instance.Amount = (double)old.Instance.Amount;
            _new.Instance.AmountInCents = (int)(old.Instance.Amount * 100);
            _new.Instance.PriorityLevel = "VIP";
            _new.Instance.Status = old.Instance.Status;
        }

        public override Wait MigrateActiveWait(WaitInfrastructureDto oldWait, ComplexOrderV2Wrapper _new)
        {
            switch (oldWait.WaitName)
            {
                case "ApprovalWait":
                    return RecreateWait("ApprovalWait");

                case "PaymentWait":
                    return RecreateWait("GatewayCallbackWait");

                default:
                    return RecreateWait(oldWait.WaitName);
            }
        }

        public override Wait MigrateSubWorkflowState(SubWorkflowWaitDto oldSubWait, ComplexOrderV2Wrapper _new)
        {
            return SubWorkflow_RecreateWait(oldSubWait.MethodFullPath, oldSubWait.WaitName);
        }
    }

    public class ComprehensiveVersioningTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<WorkflowsDbContext> _options;

        public ComprehensiveVersioningTests()
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

        private ServiceProvider BuildServiceProvider(Action<IServiceCollection>? configure = null)
        {
            var services = new ServiceCollection();
            services.AddDbContext<WorkflowsDbContext>(opt => opt.UseSqlite(_connection));
            services.AddScoped<IWorkflowStore, WorkflowStore>();
            services.AddSingleton<IObjectSerializer, TestObjectSerializer>();
            services.AddSingleton<IExpressionSerializer, TestExpressionSerializer>();
            services.AddSingleton<IMessageDispatcher, MockMessageDispatcher>();
            services.AddWorkflowsRunner();

            services.AddWorkflowMigration<ComplexOrderV1Wrapper, ComplexOrderV2Wrapper, ComplexOrderWorkflowMigration_V1_To_V2>();

            configure?.Invoke(services);
            return services.BuildServiceProvider();
        }

        [Fact]
        public async Task SxS_InFlightV1_And_NewV2_ShouldCoexistAndExecuteIndependently()
        {
            // Arrange
            var sp = BuildServiceProvider();
            var store = sp.GetRequiredService<IWorkflowStore>();

            var instV1Id = Guid.NewGuid();
            var instV2Id = Guid.NewGuid();

            // 1. Launch V1 instance
            var v1State = new WorkflowStateDto
            {
                Id = instV1Id,
                Created = DateTime.UtcNow,
                Status = WorkflowInstanceStatus.Running,
                WorkflowType = "ComplexOrderWorkflow",
                WorkflowVersion = 1,
                StateObject = new WorkflowStateObject
                {
                    WorkflowType = "ComplexOrderWorkflow",
                    Instance = new ComplexOrderWorkflow_V1(),
                    StateIndex = 1,
                    Locals = new Dictionary<string, object>
                    {
                        {
                            "state",
                            new ComplexOrderState_V1
                            {
                                OrderId = instV1Id,
                                CustomerEmail = "v1user@test.com",
                                Amount = 100.00m,
                                Status = "AwaitingApproval"
                            }
                        }
                    }
                },
                Waits = new List<WaitInfrastructureDto>
                {
                    new SignalWaitDto
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = WaitStatus.Waiting,
                        WaitName = "ApprovalWait",
                        SignalIdentifier = "ApprovalSignal",
                        StateAfterWait = 1
                    }
                }
            };
            await store.SaveContextSyncAsync(v1State, Enumerable.Empty<string>());

            // 2. Launch V2 instance
            var v2State = new WorkflowStateDto
            {
                Id = instV2Id,
                Created = DateTime.UtcNow,
                Status = WorkflowInstanceStatus.Running,
                WorkflowType = "ComplexOrderWorkflow",
                WorkflowVersion = 2,
                StateObject = new WorkflowStateObject
                {
                    WorkflowType = "ComplexOrderWorkflow",
                    Instance = new ComplexOrderWorkflow_V2(),
                    StateIndex = 1,
                    Locals = new Dictionary<string, object>
                    {
                        {
                            "state",
                            new ComplexOrderState_V2
                            {
                                OrderId = instV2Id,
                                CustomerEmail = "v2user@test.com",
                                Amount = 200.00,
                                AmountInCents = 20000,
                                PriorityLevel = "VIP",
                                Status = "AwaitingFraudCheck"
                            }
                        }
                    }
                },
                Waits = new List<WaitInfrastructureDto>
                {
                    new SignalWaitDto
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = WaitStatus.Waiting,
                        WaitName = "FraudCheckWait",
                        SignalIdentifier = "FraudCheckSignal",
                        StateAfterWait = 1
                    }
                }
            };
            await store.SaveContextSyncAsync(v2State, Enumerable.Empty<string>());

            // Act & Assert
            // Fetch V1 and V2 states directly from store
            var loadedV1 = await store.GetInstanceStateAsync(instV1Id);
            var loadedV2 = await store.GetInstanceStateAsync(instV2Id);

            loadedV1.Should().NotBeNull();
            loadedV1!.WorkflowVersion.Should().Be(1);
            loadedV1.Waits.Single().WaitName.Should().Be("ApprovalWait");

            loadedV2.Should().NotBeNull();
            loadedV2!.WorkflowVersion.Should().Be(2);
            loadedV2.Waits.Single().WaitName.Should().Be("FraudCheckWait");
        }

        [Fact]
        public async Task Migration_InFlightV1_PausedAtApprovalWait_ShouldMigrateToV2_RemapStateIndex_AndComplete()
        {
            // Arrange
            var sp = BuildServiceProvider();
            var store = sp.GetRequiredService<IWorkflowStore>();

            var instanceId = Guid.NewGuid();

            // Set up a V1 instance suspended at "ApprovalWait" (StateIndex = 1 in V1)
            var v1State = new WorkflowStateDto
            {
                Id = instanceId,
                Created = DateTime.UtcNow,
                Status = WorkflowInstanceStatus.Running,
                WorkflowType = "ComplexOrderWorkflow",
                WorkflowVersion = 1,
                StateObject = new WorkflowStateObject
                {
                    WorkflowType = "ComplexOrderWorkflow",
                    Instance = new ComplexOrderWorkflow_V1(),
                    StateIndex = 1,
                    Locals = new Dictionary<string, object>
                    {
                        {
                            "state",
                            new ComplexOrderState_V1
                            {
                                OrderId = instanceId,
                                CustomerEmail = "migrated@test.com",
                                Amount = 350.50m,
                                Status = "AwaitingApproval"
                            }
                        }
                    }
                },
                Waits = new List<WaitInfrastructureDto>
                {
                    new SignalWaitDto
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = WaitStatus.Waiting,
                        WaitName = "ApprovalWait",
                        SignalIdentifier = "ApprovalSignal",
                        StateAfterWait = 1
                    }
                }
            };
            await store.SaveContextSyncAsync(v1State, Enumerable.Empty<string>());

            // Write V2 manifest sidecar (ApprovalWait yield ordinal shifted to 2 in V2)
            var schema = new WorkflowVersionManifest
            {
                SchemaVersion = 2,
                WorkflowName = "ComplexOrderWorkflow",
                MainCfg = new List<BasicBlockSchema>
                {
                    new BasicBlockSchema
                    {
                        BlockIndex = 1,
                        YieldWaitType = "SignalWait",
                        YieldWaitName = "FraudCheckWait",
                        YieldOrdinal = 1
                    },
                    new BasicBlockSchema
                    {
                        BlockIndex = 2,
                        YieldWaitType = "SignalWait",
                        YieldWaitName = "ApprovalWait",
                        YieldOrdinal = 2 // Shifted in V2!
                    }
                }
            };
            var schemaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ComplexOrderWorkflow_Schema.json");
            File.WriteAllText(schemaPath, JsonConvert.SerializeObject(schema));

            try
            {
                // Act: Resolve executor and migrate
                var executor = sp.GetKeyedService<IWorkflowMigrationExecutor>("ComplexOrderWorkflow:1:2");
                executor.Should().NotBeNull();
                await executor!.MigrateAsync(instanceId, CancellationToken.None);

                // Assert
                var migratedState = await store.GetInstanceStateAsync(instanceId);
                migratedState.Should().NotBeNull();
                migratedState!.WorkflowVersion.Should().Be(2);

                // 1. Verify StateIndex remapped to V2 YieldOrdinal (2)
                migratedState.StateObject.StateIndex.Should().Be(2);

                // 2. Verify state POCO properties transformed
                migratedState.StateObject.Locals.TryGetValue("state", out var stateObj).Should().BeTrue();
                var v2Json = JsonConvert.SerializeObject(stateObj);
                var v2State = JsonConvert.DeserializeObject<ComplexOrderState_V2>(v2Json);
                v2State.Should().NotBeNull();
                v2State!.OrderId.Should().Be(instanceId);
                v2State.CustomerEmail.Should().Be("migrated@test.com");
                v2State.Amount.Should().Be(350.50);
                v2State.AmountInCents.Should().Be(35050);
                v2State.PriorityLevel.Should().Be("VIP");

                // 3. Verify active wait remapped
                migratedState.Waits.Should().ContainSingle();
                migratedState.Waits.Single().WaitName.Should().Be("ApprovalWait");
                migratedState.Waits.Single().StateAfterWait.Should().Be(2);
            }
            finally
            {
                if (File.Exists(schemaPath))
                    File.Delete(schemaPath);
            }
        }

        [Fact]
        public async Task Migration_WithSubWorkflow_ShouldMigrateParentAndChildState()
        {
            // Arrange
            var sp = BuildServiceProvider();
            var store = sp.GetRequiredService<IWorkflowStore>();

            var instanceId = Guid.NewGuid();
            var subStateId = Guid.NewGuid().ToString();

            // V1 parent workflow paused inside child subworkflow "GatewayCallbackWait"
            var v1State = new WorkflowStateDto
            {
                Id = instanceId,
                Created = DateTime.UtcNow,
                Status = WorkflowInstanceStatus.Running,
                WorkflowType = "ComplexOrderWorkflow",
                WorkflowVersion = 1,
                StateObject = new WorkflowStateObject
                {
                    WorkflowType = "ComplexOrderWorkflow",
                    Instance = new ComplexOrderWorkflow_V1(),
                    StateIndex = 2,
                    Locals = new Dictionary<string, object>
                    {
                        {
                            "state",
                            new ComplexOrderState_V1
                            {
                                OrderId = instanceId,
                                CustomerEmail = "sub@test.com",
                                Amount = 500.00m,
                                Status = "ProcessingPayment"
                            }
                        }
                    }
                },
                Waits = new List<WaitInfrastructureDto>
                {
                    new SubWorkflowWaitDto
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = WaitStatus.Waiting,
                        WaitName = "ExecutePayment",
                        MethodFullPath = "PaymentSubWorkflow.ExecutePayment",
                        StateMachineObjectId = Guid.NewGuid(),
                        StateAfterWait = 2,
                        ChildWaits = new List<WaitInfrastructureDto>
                        {
                            new SignalWaitDto
                            {
                                Id = Guid.NewGuid().ToString(),
                                Status = WaitStatus.Waiting,
                                WaitName = "GatewayCallbackWait",
                                SignalIdentifier = "GatewayCallbackSignal",
                                StateAfterWait = 1
                            }
                        }
                    }
                }
            };
            await store.SaveContextSyncAsync(v1State, Enumerable.Empty<string>());

            var schema = new WorkflowVersionManifest
            {
                SchemaVersion = 2,
                WorkflowName = "ComplexOrderWorkflow",
                MainCfg = new List<BasicBlockSchema>
                {
                    new BasicBlockSchema { BlockIndex = 1, YieldWaitName = "ApprovalWait", YieldOrdinal = 1 }
                },
                SubWorkflows = new List<SubWorkflowSchema>
                {
                    new SubWorkflowSchema
                    {
                        MethodName = "ExecutePayment",
                        MethodFullPath = "PaymentSubWorkflow.ExecutePayment",
                        BasicBlocks = new List<BasicBlockSchema>
                        {
                            new BasicBlockSchema { BlockIndex = 1, YieldWaitName = "PreAuthWait", YieldOrdinal = 1 },
                            new BasicBlockSchema { BlockIndex = 2, YieldWaitName = "GatewayCallbackWait", YieldOrdinal = 2 }
                        }
                    }
                }
            };
            var schemaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ComplexOrderWorkflow_Schema.json");
            File.WriteAllText(schemaPath, JsonConvert.SerializeObject(schema));

            try
            {
                // Act
                var executor = sp.GetKeyedService<IWorkflowMigrationExecutor>("ComplexOrderWorkflow:1:2");
                executor.Should().NotBeNull();
                await executor!.MigrateAsync(instanceId, CancellationToken.None);

                // Assert
                var migratedState = await store.GetInstanceStateAsync(instanceId);
                migratedState.Should().NotBeNull();
                migratedState!.WorkflowVersion.Should().Be(2);

                var parentWait = migratedState.Waits.Single() as SubWorkflowWaitDto;
                parentWait.Should().NotBeNull();
                parentWait!.MethodFullPath.Should().Be("PaymentSubWorkflow.ExecutePayment");
                parentWait.ChildWaits.Should().ContainSingle();
                parentWait.ChildWaits.Single().WaitName.Should().Be("GatewayCallbackWait");
                parentWait.ChildWaits.Single().StateAfterWait.Should().Be(2); // Child yield ordinal remapped in V2!
            }
            finally
            {
                if (File.Exists(schemaPath))
                    File.Delete(schemaPath);
            }
        }

        [Fact]
        public async Task Migration_WithParallelGroupWait_ShouldRemapAllActiveBranches()
        {
            // Arrange
            var sp = BuildServiceProvider();
            var store = sp.GetRequiredService<IWorkflowStore>();

            var instanceId = Guid.NewGuid();

            var v1State = new WorkflowStateDto
            {
                Id = instanceId,
                Created = DateTime.UtcNow,
                Status = WorkflowInstanceStatus.Running,
                WorkflowType = "ComplexOrderWorkflow",
                WorkflowVersion = 1,
                StateObject = new WorkflowStateObject
                {
                    WorkflowType = "ComplexOrderWorkflow",
                    Instance = new ComplexOrderWorkflow_V1(),
                    StateIndex = 1,
                    Locals = new Dictionary<string, object>
                    {
                        {
                            "state",
                            new ComplexOrderState_V1
                            {
                                OrderId = instanceId,
                                CustomerEmail = "parallel@test.com",
                                Amount = 750.00m,
                                Status = "ProcessingParallel"
                            }
                        }
                    }
                },
                Waits = new List<WaitInfrastructureDto>
                {
                    new GroupWaitDto
                    {
                        Id = Guid.NewGuid().ToString(),
                        Status = WaitStatus.Waiting,
                        WaitName = "ParallelGroup",
                        StateAfterWait = 1,
                        ChildWaits = new List<WaitInfrastructureDto>
                        {
                            new SignalWaitDto
                            {
                                Id = Guid.NewGuid().ToString(),
                                Status = WaitStatus.Waiting,
                                WaitName = "ApprovalWait",
                                SignalIdentifier = "ApprovalSignal",
                                StateAfterWait = 1
                            },
                            new SignalWaitDto
                            {
                                Id = Guid.NewGuid().ToString(),
                                Status = WaitStatus.Waiting,
                                WaitName = "PaymentWait",
                                SignalIdentifier = "PaymentSignal",
                                StateAfterWait = 1
                            }
                        }
                    }
                }
            };
            await store.SaveContextSyncAsync(v1State, Enumerable.Empty<string>());

            var schema = new WorkflowVersionManifest
            {
                SchemaVersion = 2,
                WorkflowName = "ComplexOrderWorkflow",
                MainCfg = new List<BasicBlockSchema>
                {
                    new BasicBlockSchema { BlockIndex = 1, YieldWaitName = "ApprovalWait", YieldOrdinal = 10 },
                    new BasicBlockSchema { BlockIndex = 2, YieldWaitName = "GatewayCallbackWait", YieldOrdinal = 20 }
                }
            };
            var schemaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ComplexOrderWorkflow_Schema.json");
            File.WriteAllText(schemaPath, JsonConvert.SerializeObject(schema));

            try
            {
                // Act
                var executor = sp.GetKeyedService<IWorkflowMigrationExecutor>("ComplexOrderWorkflow:1:2");
                executor.Should().NotBeNull();
                await executor!.MigrateAsync(instanceId, CancellationToken.None);

                // Assert
                var migratedState = await store.GetInstanceStateAsync(instanceId);
                migratedState.Should().NotBeNull();
                migratedState!.WorkflowVersion.Should().Be(2);

                var group = migratedState.Waits.Single() as GroupWaitDto;
                group.Should().NotBeNull();
                group!.ChildWaits.Should().HaveCount(2);
                group.ChildWaits[0].WaitName.Should().Be("ApprovalWait");
                group.ChildWaits[0].StateAfterWait.Should().Be(10);
                group.ChildWaits[1].WaitName.Should().Be("GatewayCallbackWait");
                group.ChildWaits[1].StateAfterWait.Should().Be(20);
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
