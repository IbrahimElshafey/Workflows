using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Workflows.Abstraction.Enums;
using Workflows.Definition;
using Workflows.TestShell;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class GroupFilterTests
    {
        public class OrderReceivedSignal
        {
            public string OrderId { get; set; }
            public int Amount { get; set; }
        }

        public sealed class GroupFilterTestWorkflow : WorkflowContainer
        {
            public List<string> ExecutionLog { get; set; } = new();
            public int StateValue { get; set; } = 100;

            public override async IAsyncEnumerable<Wait> Run()
            {
                ExecutionLog.Add("Start");

                var sig1 = WaitSignal<OrderReceivedSignal>("Sig1")
                    .AfterMatch(s => ExecutionLog.Add($"Sig1 received: {s.OrderId}"));
                var sig2 = WaitSignal<OrderReceivedSignal>("Sig2")
                    .AfterMatch(s => ExecutionLog.Add($"Sig2 received: {s.OrderId}"));

                var group = WaitGroup(new[] { (Wait)sig1, (Wait)sig2 }, "MyGroup");

                group.WithState(StateValue);
                group.MatchIf<int>(state => state > 50);

                yield return group;

                ExecutionLog.Add("GroupCompleted");
            }
        }

        public sealed class GroupFilterFailTestWorkflow : WorkflowContainer
        {
            public List<string> ExecutionLog { get; set; } = new();
            public int StateValue { get; set; } = 30;

            public override async IAsyncEnumerable<Wait> Run()
            {
                ExecutionLog.Add("Start");

                var sig1 = WaitSignal<OrderReceivedSignal>("Sig1")
                    .AfterMatch(s => ExecutionLog.Add($"Sig1 received: {s.OrderId}"));
                var sig2 = WaitSignal<OrderReceivedSignal>("Sig2")
                    .AfterMatch(s => ExecutionLog.Add($"Sig2 received: {s.OrderId}"));

                var group = WaitGroup(new[] { (Wait)sig1, (Wait)sig2 }, "MyGroup");

                group.WithState(StateValue);
                group.MatchIf<int>(state => state > 50);

                yield return group;

                ExecutionLog.Add("GroupCompleted");
            }
        }

        [Fact]
        public async Task GroupFilter_ShouldEvaluateMatchIfFilter_AndSucceed_WhenConditionMet()
        {
            using var shell = new WorkflowTestShell();
            shell.RegisterWorkflow<GroupFilterTestWorkflow>("GroupFilterTest", "1.0")
                 .RegisterSignal<OrderReceivedSignal>("Sig1")
                 .RegisterSignal<OrderReceivedSignal>("Sig2");

            // Start the workflow
            var state = await shell.StartWorkflowAsync("GroupFilterTest");
            state.Should().NotBeNull();
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "MyGroup");

            // Simulate the first signal
            state = await shell.SimulateSignalAsync("Sig1", new OrderReceivedSignal { OrderId = "ORD-1", Amount = 10 });
            
            // Should complete because state = 100 which is > 50
            shell.CurrentStatus.Should().Be(WorkflowInstanceStatus.Completed);
            
            var workflow = (GroupFilterTestWorkflow)shell.CurrentState!.StateObject.Instance;
            workflow.ExecutionLog.Should().ContainInOrder("Start", "Sig1 received: ORD-1", "GroupCompleted");
        }

        [Fact]
        public async Task GroupFilter_ShouldEvaluateMatchIfFilter_AndNotComplete_WhenConditionNotMet()
        {
            using var shell = new WorkflowTestShell();
            shell.RegisterWorkflow<GroupFilterFailTestWorkflow>("GroupFilterFailTest", "1.0")
                 .RegisterSignal<OrderReceivedSignal>("Sig1")
                 .RegisterSignal<OrderReceivedSignal>("Sig2");

            // Start the workflow
            var state = await shell.StartWorkflowAsync("GroupFilterFailTest");
            state.Should().NotBeNull();
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "MyGroup");

            // Simulate the first signal
            state = await shell.SimulateSignalAsync("Sig1", new OrderReceivedSignal { OrderId = "ORD-1", Amount = 10 });
            
            // Should NOT complete because state = 30 which is NOT > 50
            shell.CurrentStatus.Should().Be(WorkflowInstanceStatus.Running);
            
            var workflow = (GroupFilterFailTestWorkflow)shell.CurrentState!.StateObject.Instance;
            workflow.ExecutionLog.Should().ContainInOrder("Start", "Sig1 received: ORD-1");
            workflow.ExecutionLog.Should().NotContain("GroupCompleted");
        }
    }
}
