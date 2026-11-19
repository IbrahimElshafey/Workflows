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
using Workflows.Hosting.InProcess;
using Workflows.Primitives;
using Workflows.Storage.EntityFrameworkCore;
using Workflows.Communication.Abstraction;
using Workflows.Runner;
using Workflows.Runner.Migration;
using Xunit;

namespace Workflows.Runner.Tests
{
    // Mock layout classes for Version 1
    public sealed class OrderWorkflowV1Instance
    {
        public Guid OrderId { get; set; }
        public string? CustomerName { get; set; }
        public decimal Amount { get; set; }
    }

    public sealed class OrderWorkflowV1 : WorkflowStateWrapper<OrderWorkflowV1Instance>
    {
        public OrderWorkflowV1(WorkflowStateDto dto, OrderWorkflowV1Instance instance)
            : base(dto, instance) { }

        public Guid OrderId => Instance.OrderId;
        public string? CustomerName => Instance.CustomerName;
        public decimal Amount => Instance.Amount;
    }

    // Mock layout classes for Version 2 (with added fields/renamed fields)
    public sealed class OrderWorkflowV2Instance
    {
        public Guid OrderId { get; set; }
        public string? CustomerName { get; set; }
        public decimal Amount { get; set; }
        public string? OrderNumber { get; set; } // new field
    }

    public sealed class OrderWorkflowV2 : WorkflowStateWrapper<OrderWorkflowV2Instance>
    {
        public OrderWorkflowV2(WorkflowStateDto dto, OrderWorkflowV2Instance instance)
            : base(dto, instance) { }

        public Guid OrderId => Instance.OrderId;
        public string? CustomerName => Instance.CustomerName;
        public decimal Amount => Instance.Amount;
        public string? OrderNumber => Instance.OrderNumber;
    }

    // Mock migration class
    [WorkflowMigration("OrderWorkflow", fromVersion: 1, toVersion: 2)]
    public class OrderWorkflowMigration_V1_To_V2 : WorkflowMigration<OrderWorkflowV1, OrderWorkflowV2>
    {
        public override void MigrateState(OrderWorkflowV1 old, OrderWorkflowV2 _new)
        {
            _new.Instance.OrderId = old.Instance.OrderId;
            _new.Instance.CustomerName = old.Instance.CustomerName;
            _new.Instance.Amount = old.Instance.Amount;
            _new.Instance.OrderNumber = $"ORD-{old.OrderId:N}".ToUpper().Substring(0, 16);
            ScheduleCommand("test-dispatched-command");
        }

        public override MigratedWait MigrateActiveWait(WaitInfrastructureDto oldWait, OrderWorkflowV2 _new)
        {
            // Recreate wait by name
            return RecreateWait(oldWait.WaitName);
        }
    }

    public class WorkflowVersioningTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<WorkflowsDbContext> _options;

        public WorkflowVersioningTests()
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
        public async Task MigrationExecutor_ShouldSuccessfullyMigrateWorkflowState()
        {
            // Arrange
            var services = new ServiceCollection();
            
            // Register infrastructure
            services.AddDbContext<WorkflowsDbContext>(opt => opt.UseSqlite(_connection));
            services.AddScoped<IWorkflowStore, WorkflowStore>();
            services.AddSingleton<IObjectSerializer, Infrastructure.TestObjectSerializer>();
            services.AddSingleton<IExpressionSerializer, Infrastructure.TestExpressionSerializer>();
            
            // Add runner components
            services.AddWorkflowsRunner();

            // Mock message dispatcher
            var mockDispatcher = new MockMessageDispatcher();
            services.AddSingleton<IMessageDispatcher>(mockDispatcher);

            // Register our migration
            services.AddWorkflowMigration<OrderWorkflowV1, OrderWorkflowV2, OrderWorkflowMigration_V1_To_V2>();

            var sp = services.BuildServiceProvider();

            // Set up a V1 instance in the database
            var instanceId = Guid.NewGuid();
            var waitId = Guid.NewGuid().ToString();
            var oldState = new WorkflowStateDto
            {
                Id = instanceId,
                Created = DateTime.UtcNow,
                Status = WorkflowInstanceStatus.Running,
                WorkflowType = "OrderWorkflow",
                WorkflowVersion = 1,
                StateObject = new WorkflowStateObject
                {
                    WorkflowType = "OrderWorkflow",
                    Instance = new object(),
                    StateIndex = 0,
                    Locals = new Dictionary<string, object>
                    {
                        {
                            "state",
                            new OrderWorkflowV1Instance
                            {
                                OrderId = instanceId,
                                CustomerName = "John Doe",
                                Amount = 150.50m
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
                        WaitName = "WaitApproved",
                        SignalIdentifier = "Approved",
                        IsPersisted = false
                    }
                }
            };

            var store = sp.GetRequiredService<IWorkflowStore>();
            await store.SaveContextSyncAsync(oldState, Enumerable.Empty<string>());

            // Write V2 schema sidecar file for manifest resolution
            var schema = new WorkflowVersionManifest
            {
                SchemaVersion = 2,
                WorkflowName = "OrderWorkflow",
                MainCfg = new List<BasicBlockSchema>
                {
                    new BasicBlockSchema
                    {
                        BlockIndex = 1,
                        YieldWaitType = "SignalWait",
                        YieldWaitName = "WaitApproved",
                        YieldOrdinal = 42 // mock yield state after WaitApproved in V2
                    }
                }
            };
            var schemaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OrderWorkflow_Schema.json");
            File.WriteAllText(schemaPath, JsonConvert.SerializeObject(schema));

            try
            {
                // Resolve executor from DI
                var migrationKey = "OrderWorkflow:1:2";
                var executor = sp.GetKeyedService<IWorkflowMigrationExecutor>(migrationKey);
                executor.Should().NotBeNull();

                // Act
                await executor!.MigrateAsync(instanceId, CancellationToken.None);

                // Assert
                var migratedState = await store.GetInstanceStateAsync(instanceId);
                migratedState.Should().NotBeNull();
                migratedState!.WorkflowVersion.Should().Be(2);

                // Check instance state mapping
                migratedState.StateObject.Locals.TryGetValue("state", out var stateObj).Should().BeTrue();
                var v2InstanceJson = JsonConvert.SerializeObject(stateObj);
                var v2Instance = JsonConvert.DeserializeObject<OrderWorkflowV2Instance>(v2InstanceJson);
                v2Instance.Should().NotBeNull();
                v2Instance!.OrderId.Should().Be(instanceId);
                v2Instance.CustomerName.Should().Be("John Doe");
                v2Instance.Amount.Should().Be(150.50m);
                v2Instance.OrderNumber.Should().Be($"ORD-{instanceId:N}".ToUpper().Substring(0, 16));

                // Check resolved waits
                migratedState.Waits.Should().ContainSingle();
                var resolvedWait = migratedState.Waits.Single();
                resolvedWait.WaitName.Should().Be("WaitApproved");
                resolvedWait.StateAfterWait.Should().Be(42); // matched V2 schema index
                resolvedWait.WaitType.Should().Be(WaitType.SignalWait); // mapped from Placeholder

                // Check scheduled command dispatching
                mockDispatcher.DispatchedMessages.Should().Contain("test-dispatched-command");
            }
            finally
            {
                if (File.Exists(schemaPath))
                    File.Delete(schemaPath);
            }
        }

        [Fact]
        public void InMemoryWorkflowRegistry_ShouldStoreMultipleVersionsSideBySide()
        {
            // Arrange
            var registry = new Infrastructure.InMemoryWorkflowRegistry();

            // Act
            registry.AddWorkflow("OrderWorkflow", 1, (typeof(OrderWorkflowV1), typeof(OrderWorkflowV1), typeof(object), "Run"));
            registry.AddWorkflow("OrderWorkflow", 2, (typeof(OrderWorkflowV2), typeof(OrderWorkflowV2), typeof(object), "Run"));

            // Assert
            registry.TryGetWorkflow("OrderWorkflow", 1, out var v1).Should().BeTrue();
            registry.TryGetWorkflow("OrderWorkflow", 2, out var v2).Should().BeTrue();
            v1.WorkflowContainer.Should().Be(typeof(OrderWorkflowV1));
            v2.WorkflowContainer.Should().Be(typeof(OrderWorkflowV2));

            registry.TryGetLatestWorkflow("OrderWorkflow", out var latest).Should().BeTrue();
            latest.WorkflowContainer.Should().Be(typeof(OrderWorkflowV2));

            // Backward compat: Workflows dictionary returns latest
            registry.Workflows.Should().ContainKey("OrderWorkflow");
            registry.Workflows["OrderWorkflow"].WorkflowContainer.Should().Be(typeof(OrderWorkflowV2));
        }

        [Fact]
        public async Task SideBySideExecution_ShouldRunEachInstanceWithItsOwnVersion()
        {
            // Arrange
            var builder = new Infrastructure.WorkflowTestBuilder();
            builder.RegisterWorkflow<TestWorkflows.SxSWorkflowV1>("SxSWorkflow");
            builder.RegisterWorkflow<TestWorkflows.SxSWorkflowV2>("SxSWorkflow");
            builder.RegisterSignal<TestWorkflows.SxSWorkflowSignal>("SxSWorkflowSignal");

            var runner = builder.Build();

            // Act - Start V1 instance explicitly
            var v1StartResult = await runner.StartWorkflow("SxSWorkflow", version: 1);
            v1StartResult.Should().NotBeNull();
            var v1Response = builder.Client.SentResults.Last().Result;
            var v1State = v1Response.UpdatedState;
            v1State.WorkflowVersion.Should().Be(1);
            var v1Workflow = (TestWorkflows.SxSWorkflowV1)v1State.StateObject.Instance;
            v1Workflow.ExecutionLog.Should().Contain("V1: Start");

            // Act - Start V2 instance explicitly
            var v2StartResult = await runner.StartWorkflow("SxSWorkflow", version: 2);
            v2StartResult.Should().NotBeNull();
            var v2Response = builder.Client.SentResults.Last().Result;
            var v2State = v2Response.UpdatedState;
            v2State.WorkflowVersion.Should().Be(2);
            var v2Workflow = (TestWorkflows.SxSWorkflowV2)v2State.StateObject.Instance;
            v2Workflow.ExecutionLog.Should().Contain("V2: Start");

            // Act - Resume V1 instance
            var v1Wait = v1State.Waits.First();
            var v1ResumeRequest = new WorkflowExecutionRequest
            {
                TriggeringWaitId = v1Wait.Id,
                Signal = builder.CreateSignal("SxSWorkflowSignal", new TestWorkflows.SxSWorkflowSignal { Value = "v1" }),
                WorkflowState = v1State
            };
            var v1ResumeResult = await runner.RunWorkflowAsync(v1ResumeRequest);
            v1ResumeResult.Should().NotBeNull();
            v1ResumeResult.Status.Should().Be("Accepted");
            var resumedV1Workflow = (TestWorkflows.SxSWorkflowV1)v1State.StateObject.Instance;
            resumedV1Workflow.ExecutionLog.Should().ContainInOrder("V1: Start", "V1: End");

            // Act - Resume V2 instance
            var v2Wait = v2State.Waits.First();
            var v2ResumeRequest = new WorkflowExecutionRequest
            {
                TriggeringWaitId = v2Wait.Id,
                Signal = builder.CreateSignal("SxSWorkflowSignal", new TestWorkflows.SxSWorkflowSignal { Value = "v2" }),
                WorkflowState = v2State
            };
            var v2ResumeResult = await runner.RunWorkflowAsync(v2ResumeRequest);
            v2ResumeResult.Should().NotBeNull();
            v2ResumeResult.Status.Should().Be("Accepted");
            var resumedV2Workflow = (TestWorkflows.SxSWorkflowV2)v2State.StateObject.Instance;
            resumedV2Workflow.ExecutionLog.Should().ContainInOrder("V2: Start", "V2: End");
        }

        [Fact]
        public async Task DefinitionRepository_ShouldReturnAllVersionsAndLatest()
        {
            // Arrange
            using var context = new WorkflowsDbContext(_options);
            context.WorkflowDefinitions.AddRange(
                new WorkflowDefinitionEntity { WorkflowName = "OrderWorkflow", Version = 1, WorkflowTypeName = "T1", WorkflowTypeSchema = "{}", RegisteredAt = DateTime.UtcNow },
                new WorkflowDefinitionEntity { WorkflowName = "OrderWorkflow", Version = 2, WorkflowTypeName = "T2", WorkflowTypeSchema = "{}", RegisteredAt = DateTime.UtcNow },
                new WorkflowDefinitionEntity { WorkflowName = "OrderWorkflow", Version = 3, WorkflowTypeName = "T3", WorkflowTypeSchema = "{}", RegisteredAt = DateTime.UtcNow }
            );
            await context.SaveChangesAsync();

            using var repoContext = new WorkflowsDbContext(_options);
            var repo = new DefinitionRepository(repoContext, new ServiceCollection().BuildServiceProvider());

            // Act
            var allVersions = await repo.GetAllRegisteredVersionsAsync("OrderWorkflow");
            var latest = await repo.GetLatestRegisteredVersionAsync("OrderWorkflow");

            // Assert
            allVersions.Should().Equal(1, 2, 3);
            latest.Should().Be(3);
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
