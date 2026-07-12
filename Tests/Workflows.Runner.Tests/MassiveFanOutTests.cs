using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Primitives;
using Workflows.Runner.Tests.Infrastructure;
using Workflows.Runner.Tests.TestWorkflows;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class MassiveFanOutTests
    {
        [Fact]
        public async Task WaitMany_ShouldCompleteOnlyAfterAllChildrenReceiveSignals()
        {
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<FanOutTestWorkflow>("FanOutTestWorkflow");
            builder.RegisterSignal<FanOutSignal>("FanOutSignal");

            var runner = builder.Build();

            // Start the workflow
            var startResult = await runner.StartWorkflow("FanOutTestWorkflow", 1, null);
            startResult.Status.Should().Be("Accepted");
            var instanceId = startResult.Id;
            instanceId.Should().NotBeEmpty();

            var state = builder.Client.SentResults.Last().Result.UpdatedState;
            state.Waits.Should().ContainSingle();
            var parent = state.Waits.Single().Should().BeOfType<ExternalGroupWaitDto>().Subject;
            parent.WaitType.Should().Be(WaitType.WaitMany);
            parent.ExternalChildWaits.Should().HaveCount(3);

            // Send first signal
            var firstChild = parent.ExternalChildWaits.First();
            var request1 = builder.CreateExecutionRequest<FanOutTestWorkflow>(
                firstChild.Id,
                "FanOutTestWorkflow",
                state.StateObject,
                builder.CreateSignal("FanOutSignal", new FanOutSignal { Value = "1" }),
                state.Waits);
            var result1 = await runner.RunWorkflowAsync(request1);
            result1.Status.Should().Be("Accepted");

            var state1 = builder.Client.SentResults.Last().Result.UpdatedState;
            var parent1 = state1.Waits.Single().Should().BeOfType<ExternalGroupWaitDto>().Subject;
            parent1.Status.Should().Be(WaitStatus.Waiting);
            parent1.ExternalChildWaits.Count(c => c.Status == WaitStatus.Completed).Should().Be(1);

            // Send second signal
            var secondChild = parent1.ExternalChildWaits.First(c => c.Status == WaitStatus.Waiting);
            var request2 = builder.CreateExecutionRequest<FanOutTestWorkflow>(
                secondChild.Id,
                "FanOutTestWorkflow",
                state1.StateObject,
                builder.CreateSignal("FanOutSignal", new FanOutSignal { Value = "2" }),
                state1.Waits);
            var result2 = await runner.RunWorkflowAsync(request2);
            result2.Status.Should().Be("Accepted");

            var state2 = builder.Client.SentResults.Last().Result.UpdatedState;
            var parent2 = state2.Waits.Single().Should().BeOfType<ExternalGroupWaitDto>().Subject;
            parent2.Status.Should().Be(WaitStatus.Waiting);
            parent2.ExternalChildWaits.Count(c => c.Status == WaitStatus.Completed).Should().Be(2);

            // Send third signal - should complete the workflow
            var thirdChild = parent2.ExternalChildWaits.First(c => c.Status == WaitStatus.Waiting);
            var request3 = builder.CreateExecutionRequest<FanOutTestWorkflow>(
                thirdChild.Id,
                "FanOutTestWorkflow",
                state2.StateObject,
                builder.CreateSignal("FanOutSignal", new FanOutSignal { Value = "3" }),
                state2.Waits);
            var result3 = await runner.RunWorkflowAsync(request3);
            result3.Status.Should().Be("Accepted");

            var state3 = builder.Client.SentResults.Last().Result.UpdatedState;
            state3.Status.Should().Be(WorkflowInstanceStatus.Completed);
            var instance = state3.StateObject.Instance as FanOutTestWorkflow;
            instance.Should().NotBeNull();
            instance!.Completed.Should().BeTrue();
        }

        [Fact]
        public async Task WaitAny_ShouldCompleteAfterFirstChildSignal()
        {
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<FanOutAnyTestWorkflow>("FanOutAnyTestWorkflow");
            builder.RegisterSignal<FanOutSignal>("FanOutSignal");

            var runner = builder.Build();

            var startResult = await runner.StartWorkflow("FanOutAnyTestWorkflow", 1, null);
            startResult.Status.Should().Be("Accepted");

            var state = builder.Client.SentResults.Last().Result.UpdatedState;
            var parent = state.Waits.Single().Should().BeOfType<ExternalGroupWaitDto>().Subject;
            parent.WaitType.Should().Be(WaitType.WaitAny);
            parent.ExternalChildWaits.Should().HaveCount(3);

            var firstChild = parent.ExternalChildWaits.First();
            var request = builder.CreateExecutionRequest<FanOutAnyTestWorkflow>(
                firstChild.Id,
                "FanOutAnyTestWorkflow",
                state.StateObject,
                builder.CreateSignal("FanOutSignal", new FanOutSignal { Value = "1" }),
                state.Waits);
            var result = await runner.RunWorkflowAsync(request);
            result.Status.Should().Be("Accepted");

            var finalState = builder.Client.SentResults.Last().Result.UpdatedState;
            finalState.Status.Should().Be(WorkflowInstanceStatus.Completed);
            var instance = finalState.StateObject.Instance as FanOutAnyTestWorkflow;
            instance.Should().NotBeNull();
            instance!.Completed.Should().BeTrue();
        }
    }
}
