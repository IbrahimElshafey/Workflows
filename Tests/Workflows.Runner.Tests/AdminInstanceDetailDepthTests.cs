using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Runner;
using Workflows.Admin.UI.Services;
using Workflows.Primitives;
using Workflows.Storage.EntityFrameworkCore;
using Xunit;

namespace Workflows.Runner.Tests
{
    /// <summary>
    /// The Admin instance detail page reports the sub-workflow nesting recorded in the persisted wait
    /// tree together with the configured MaxSubWorkflowDepth, so an operator can see how close a
    /// running instance is to the runtime guard that throws when the limit is exceeded.
    /// </summary>
    public class AdminInstanceDetailDepthTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<WorkflowsDbContext> _options;
        private readonly List<WorkflowsDbContext> _contexts = new();

        public AdminInstanceDetailDepthTests()
        {
            _connection = new SqliteConnection($"Data Source=AdminDepth_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            _connection.Open();

            _options = new DbContextOptionsBuilder<WorkflowsDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var context = new WorkflowsDbContext(_options);
            context.Database.EnsureCreated();
        }

        public void Dispose()
        {
            foreach (var context in _contexts)
            {
                context.Dispose();
            }

            _connection.Close();
            _connection.Dispose();
        }

        /// <summary>
        /// Builds a two-level nesting: parent sub-workflow -> signal + child sub-workflow -> time wait.
        /// </summary>
        private static List<WaitInfrastructureDto> CreateNestedWaits()
        {
            return new List<WaitInfrastructureDto>
            {
                new SubWorkflowWaitDto
                {
                    Id = Guid.NewGuid().ToString(),
                    Status = WaitStatus.Waiting,
                    WaitType = WaitType.SubWorkflowWait,
                    WaitName = "OuterSubWorkflow",
                    MethodFullPath = "NestedWorkflow.OuterSubWorkflow",
                    StateMachineObjectId = Guid.NewGuid(),
                    ChildWaits = new List<WaitInfrastructureDto>
                    {
                        new SignalWaitDto
                        {
                            Id = Guid.NewGuid().ToString(),
                            Status = WaitStatus.Waiting,
                            WaitName = "ChildSignal",
                            SignalIdentifier = "ChildSignal",
                            StateAfterWait = 1
                        },
                        new SubWorkflowWaitDto
                        {
                            Id = Guid.NewGuid().ToString(),
                            Status = WaitStatus.Waiting,
                            WaitType = WaitType.SubWorkflowWait,
                            WaitName = "InnerSubWorkflow",
                            MethodFullPath = "NestedWorkflow.InnerSubWorkflow",
                            StateMachineObjectId = Guid.NewGuid(),
                            ChildWaits = new List<WaitInfrastructureDto>
                            {
                                new TimeWaitDto
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    Status = WaitStatus.Waiting,
                                    WaitName = "InnerTimer",
                                    ExecutionTime = DateTime.UtcNow.AddMinutes(5),
                                    StateAfterWait = 1
                                }
                            }
                        }
                    }
                }
            };
        }

        private async Task<Guid> SeedInstanceAsync(List<WaitInfrastructureDto> waits)
        {
            var id = Guid.NewGuid();
            using var context = new WorkflowsDbContext(_options);
            context.WorkflowInstances.Add(new WorkflowInstance
            {
                Id = id,
                Created = DateTime.UtcNow,
                Status = (int)WorkflowInstanceStatus.Running,
                WorkflowType = "NestedWorkflow",
                WorkflowVersion = 1,
                ActiveWaitCount = 1,
                StateObject = new WorkflowStateObject(),
                Waits = waits
            });
            await context.SaveChangesAsync();
            return id;
        }

        private AdminQueryService CreateQueryService(int maxSubWorkflowDepth)
        {
            var context = new WorkflowsDbContext(_options);
            _contexts.Add(context);
            return new AdminQueryService(
                context,
                new MetricsAggregatorService(context),
                new TopologyExtractorService(context),
                new TraceAggregatorService(context),
                new WorkflowRunnerOptions { MaxSubWorkflowDepth = maxSubWorkflowDepth });
        }

        [Fact]
        public async Task InstanceDetail_ReportsDeepestSubWorkflowNesting()
        {
            var id = await SeedInstanceAsync(CreateNestedWaits());

            var detail = await CreateQueryService(100).GetInstanceDetailAsync(id, canExecuteActions: false);

            detail.Should().NotBeNull();
            detail!.SubWorkflowDepth.Should().Be(2);
            detail.MaxSubWorkflowDepth.Should().Be(100);
            detail.IsSubWorkflowDepthAtLimit.Should().BeFalse();
            detail.IsSubWorkflowDepthGuardDisabled.Should().BeFalse();
        }

        [Fact]
        public async Task InstanceDetail_FlagsAtLimit_WhenDepthReachesConfiguredMaximum()
        {
            var id = await SeedInstanceAsync(CreateNestedWaits());

            var detail = await CreateQueryService(2).GetInstanceDetailAsync(id, canExecuteActions: false);

            detail!.SubWorkflowDepth.Should().Be(2);
            detail.MaxSubWorkflowDepth.Should().Be(2);
            detail.IsSubWorkflowDepthAtLimit.Should().BeTrue();
        }

        [Fact]
        public async Task InstanceDetail_ReportsDisabledGuard_WhenMaximumIsNotPositive()
        {
            var id = await SeedInstanceAsync(CreateNestedWaits());

            var detail = await CreateQueryService(0).GetInstanceDetailAsync(id, canExecuteActions: false);

            detail!.SubWorkflowDepth.Should().Be(2);
            detail.IsSubWorkflowDepthGuardDisabled.Should().BeTrue();
            detail.IsSubWorkflowDepthAtLimit.Should().BeFalse();
        }

        [Fact]
        public async Task InstanceDetail_ReportsZeroDepth_ForFlatWorkflow()
        {
            var waits = new List<WaitInfrastructureDto>
            {
                new SignalWaitDto
                {
                    Id = Guid.NewGuid().ToString(),
                    Status = WaitStatus.Waiting,
                    WaitName = "FlatSignal",
                    SignalIdentifier = "FlatSignal",
                    StateAfterWait = 1
                }
            };

            var id = await SeedInstanceAsync(waits);

            var detail = await CreateQueryService(100).GetInstanceDetailAsync(id, canExecuteActions: false);

            detail!.SubWorkflowDepth.Should().Be(0);
        }
    }
}