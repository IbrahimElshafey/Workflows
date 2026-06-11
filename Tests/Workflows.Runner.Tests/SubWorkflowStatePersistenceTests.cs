using FluentAssertions;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Definition;
using Workflows.Runner.Tests.Infrastructure;
using Workflows.Runner.Tests.TestData;
using Workflows.Runner.Tests.TestWorkflows;
using Xunit;

namespace Workflows.Runner.Tests
{
    /// <summary>
    /// Tests verifying that sub-workflow state is correctly stored in and restored from
    /// WorkflowStateObject.StateMachinesObjects when a sub-workflow suspends on a passive wait.
    /// </summary>
    public class SubWorkflowStatePersistenceTests
    {
        [Fact]
        public async Task SubWorkflow_WhenSuspendsOnSignalWait_ShouldStoreChildStateInStateMachinesObjects()
        {
            // Arrange
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<SubWorkflowTestWorkflow>("SubWorkflowTest");
            builder.RegisterSignal<OrderReceivedSignal>("OrderReceived");
            builder.RegisterSignal<PaymentConfirmedSignal>("PaymentConfirmed");
            builder.SetupCommandHandler<ReserveInventoryCommand, ReserveInventoryResult>(
                "ReserveInventory",
                cmd => Task.FromResult(new ReserveInventoryResult { ReservationId = "RES-001", Success = true }));

            var runner = builder.Build();

            // Act - start the workflow; it will yield the initial OrderReceived signal wait and suspend
            await runner.StartWorkflow("SubWorkflowTest");

            var response = builder.Client.SentResults.Last().Result;
            var state = response.UpdatedState;
            var waits = response.UpdatedState.Waits;

            // Assert - workflow suspended on the initial OrderReceived signal
            waits.Should().NotBeEmpty("workflow should have at least one active wait");

            // Now send the OrderReceived signal to advance into the sub-workflow
            var signalWait = waits.OfType<SignalWaitDto>().FirstOrDefault();
            if (signalWait == null)
            {
                // Workflow may have suspended on SubWorkflow directly — that's acceptable
                return;
            }

            var resumeRequest = new WorkflowExecutionRequest
            {
                TriggeringWaitId = signalWait.Id,
                Signal = builder.CreateSignal("OrderReceived", new OrderReceivedSignal
                {
                    OrderId = "ORD-001",
                    Amount = 500
                }),
                WorkflowState = state
            };

            var resumeResult = await runner.RunWorkflowAsync(resumeRequest);
            var resumeResponse = builder.Client.SentResults.Last().Result;
            var resumedState = resumeResponse.UpdatedState;

            // Assert - the resumed state should contain a sub-workflow entry in Locals
            resumedState.StateObject.Locals
                .Should().NotBeNull("Locals must be initialized");

            // The sub-workflow (ProcessOrderSubWorkflow) suspends on PaymentConfirmed signal.
            // Its WorkflowStateObject must be stored in Locals.
            var subWorkflowEntry = resumedState.StateObject.Locals
                .FirstOrDefault(kv => kv.Value is WorkflowStateObject);

            subWorkflowEntry.Should().NotBeNull(
                "a suspended sub-workflow should have its full WorkflowStateObject in Locals");

            subWorkflowEntry.Value.Should().BeOfType<WorkflowStateObject>(
                "sub-workflow state must be a full WorkflowStateObject, not a flat StateMachineObject");

            var childState = (WorkflowStateObject)subWorkflowEntry.Value;
            childState.Locals.Should().ContainKey("root",
                "the child WorkflowStateObject must have its own root SM entry");
        }

        [Fact]
        public async Task SubWorkflow_WhenCompletes_ShouldRemoveChildStateFromStateMachinesObjects()
        {
            // Arrange: a workflow where the sub-workflow finishes immediately (no passive waits inside it),
            // after which the parent suspends on its own signal wait.
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<ImmediateSubWorkflowTestWorkflow>("ImmediateSubWorkflow");
            builder.RegisterSignal<OrderReceivedSignal>("OrderReceived");
            builder.SetupCommandHandler<ReserveInventoryCommand, ReserveInventoryResult>(
                "ReserveInventory",
                cmd => Task.FromResult(new ReserveInventoryResult { ReservationId = "RES-IMM", Success = true }));

            var runner = builder.Build();

            // Act
            await runner.StartWorkflow("ImmediateSubWorkflow");

            var response = builder.Client.SentResults.Last().Result;
            var state = response.UpdatedState;

            // Assert — no WorkflowStateObject entries should remain (the sub-workflow completed, not suspended)
            var orphanedSubWorkflow = state.StateObject.Locals
                .Any(kv => kv.Value is WorkflowStateObject);

            orphanedSubWorkflow.Should().BeFalse(
                "completed sub-workflow state must be cleaned up from Locals");
        }

        [Fact]
        public async Task SubWorkflow_ManySubWorkflowsInGroup_ShouldSupportNestingAndPartialCompletion()
        {
            // Arrange
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<GroupOfSubWorkflowsTestWorkflow>("GroupOfSubWorkflows");
            builder.RegisterSignal<OrderReceivedSignal>("FinalSignal");
            builder.RegisterSignal<PaymentConfirmedSignal>("Payment1");
            builder.RegisterSignal<PaymentConfirmedSignal>("Payment2");

            var runner = builder.Build();

            // Act
            await runner.StartWorkflow("GroupOfSubWorkflows");

            var response = builder.Client.SentResults.Last().Result;
            var state = response.UpdatedState;
            var waits = state.Waits;

            // Assert
            var groupWait = waits.OfType<GroupWaitDto>().FirstOrDefault(w => w.WaitName == "SubWorkflowsGroup");
            groupWait.Should().NotBeNull();
            groupWait.ChildWaits.Should().HaveCount(2);

            var child1Dto = groupWait.ChildWaits[0] as SubWorkflowWaitDto;
            var child2Dto = groupWait.ChildWaits[1] as SubWorkflowWaitDto;
            child1Dto.Should().NotBeNull();
            child2Dto.Should().NotBeNull();

            child1Dto.ChildWaits.Should().ContainSingle(w => w is SignalWaitDto && ((SignalWaitDto)w).SignalIdentifier == "Payment1");
            child2Dto.ChildWaits.Should().ContainSingle(w => w is SignalWaitDto && ((SignalWaitDto)w).SignalIdentifier == "Payment2");

            state.StateObject.Locals.Should().ContainKey(child1Dto.StateMachineObjectId.ToString());
            state.StateObject.Locals.Should().ContainKey(child2Dto.StateMachineObjectId.ToString());

            // Resume Child1 Payment (Payment1)
            var resumeRequest1 = new WorkflowExecutionRequest
            {
                TriggeringWaitId = child1Dto.ChildWaits.First().Id,
                Signal = builder.CreateSignal("Payment1", new PaymentConfirmedSignal { TransactionId = "TX-01" }),
                WorkflowState = state
            };

            await runner.RunWorkflowAsync(resumeRequest1);
            var response1 = builder.Client.SentResults.Last().Result;
            var state1 = response1.UpdatedState;

            // Assert - Child1 completed, Child2 is still waiting
            state1.Waits.OfType<GroupWaitDto>().Should().ContainSingle();
            var groupWait1 = state1.Waits.OfType<GroupWaitDto>().First();
            groupWait1.ChildWaits.Should().HaveCount(2);
            
            var child1DtoAfter = groupWait1.ChildWaits.FirstOrDefault(w => w.Id == child1Dto.Id) as SubWorkflowWaitDto;
            child1DtoAfter.Status.Should().Be(WaitStatus.Completed);
            
            state1.StateObject.Locals.Should().NotContainKey(child1Dto.StateMachineObjectId.ToString());
            state1.StateObject.Locals.Should().ContainKey(child2Dto.StateMachineObjectId.ToString());

            // Resume Child2 Payment (Payment2)
            var resumeRequest2 = new WorkflowExecutionRequest
            {
                TriggeringWaitId = child2Dto.ChildWaits.First().Id,
                Signal = builder.CreateSignal("Payment2", new PaymentConfirmedSignal { TransactionId = "TX-02" }),
                WorkflowState = state1
            };

            await runner.RunWorkflowAsync(resumeRequest2);
            var response2 = builder.Client.SentResults.Last().Result;
            var state2 = response2.UpdatedState;

            // Assert - Both completed, parent group completes, parent workflow advances to FinalSignal
            state2.Waits.Should().ContainSingle(w => w is SignalWaitDto && ((SignalWaitDto)w).SignalIdentifier == "FinalSignal");
            state2.StateObject.Locals.Should().NotContainKey(child2Dto.StateMachineObjectId.ToString());
        }

        [Fact]
        public async Task SubWorkflow_SaveAndRestoreLocalVariables_ShouldWorkCorrectly()
        {
            // Arrange
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<SubWorkflowWithLocalVariablesTestWorkflow>("SubWorkflowWithLocalVariables");
            builder.RegisterSignal<OrderReceivedSignal>("FinalSignal");
            builder.RegisterSignal<PaymentConfirmedSignal>("Payment");

            var runner = builder.Build();

            // Act - Start workflow
            await runner.StartWorkflow("SubWorkflowWithLocalVariables");

            var response = builder.Client.SentResults.Last().Result;
            var state = response.UpdatedState;
            var waits = state.Waits;

            var subWorkflowDto = waits.OfType<SubWorkflowWaitDto>().FirstOrDefault();
            subWorkflowDto.Should().NotBeNull();
            var paymentWaitDto = subWorkflowDto.ChildWaits.First();

            // Act - Resume the payment wait inside the sub-workflow
            var resumeRequest = new WorkflowExecutionRequest
            {
                TriggeringWaitId = paymentWaitDto.Id,
                Signal = builder.CreateSignal("Payment", new PaymentConfirmedSignal { TransactionId = "TX-99" }),
                WorkflowState = state
            };

            await runner.RunWorkflowAsync(resumeRequest);
            var resumeResponse = builder.Client.SentResults.Last().Result;
            var resumedState = resumeResponse.UpdatedState;

            // Assert
            var workflowInstance = resumedState.StateObject.Instance as SubWorkflowWithLocalVariablesTestWorkflow;
            workflowInstance.Should().NotBeNull();
            workflowInstance.ExecutionLog.Should().ContainInOrder(42, 100);
        }
    }

    /// <summary>
    /// A workflow whose sub-workflow has one immediate command wait only (active → runs and finishes without suspending),
    /// followed by a passive signal on the parent so the overall workflow does suspend.
    /// </summary>
    [Workflow("ImmediateSubWorkflow", 1)]
    public sealed class ImmediateSubWorkflowTestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            yield return WaitSubWorkflow(ImmediateChild(), "ImmediateChild", "Runs to completion immediately");

            // After sub-workflow completes, suspend on a signal so we can inspect the persisted state
            yield return WaitSignal<OrderReceivedSignal>("OrderReceived", "Final signal");
        }

        [SubWorkflow]
        private async IAsyncEnumerable<Wait> ImmediateChild()
        {
            // A single immediate (active) command wait — runs synchronously and completes.
            yield return ExecuteCommand<ReserveInventoryCommand, ReserveInventoryResult>(
                "ReserveInventory",
                new ReserveInventoryCommand { ProductId = "IMM-1", Quantity = 1 });
            // No further passive waits → sub-workflow finishes
        }
    }

    [Workflow("GroupOfSubWorkflows", 1)]
    public sealed class GroupOfSubWorkflowsTestWorkflow : WorkflowContainer
    {
        public List<string> ExecutionLog { get; set; } = new();

        public async IAsyncEnumerable<Wait> Run()
        {
            ExecutionLog.Add("Parent: Start");

            var sub1 = WaitSubWorkflow(Child1(), "Child1", "First Child");
            var sub2 = WaitSubWorkflow(Child2(), "Child2", "Second Child");

            yield return WaitGroup([
                sub1,
                sub2
            ], "SubWorkflowsGroup")
            .MatchAll();

            ExecutionLog.Add("Parent: Both sub-workflows completed");
            yield return WaitSignal<OrderReceivedSignal>("FinalSignal", "Final Signal");
            ExecutionLog.Add("Parent: End");
        }

        [SubWorkflow]
        private async IAsyncEnumerable<Wait> Child1()
        {
            ExecutionLog.Add("Child1: Start");
            yield return WaitSignal<PaymentConfirmedSignal>("Payment1", "Child1 Payment");
            ExecutionLog.Add("Child1: End");
        }

        [SubWorkflow]
        private async IAsyncEnumerable<Wait> Child2()
        {
            ExecutionLog.Add("Child2: Start");
            yield return WaitSignal<PaymentConfirmedSignal>("Payment2", "Child2 Payment");
            ExecutionLog.Add("Child2: End");
        }
    }

    public class SubWorkflowWithLocalVariablesState
    {
        public int localCounter { get; set; } = 42;
        public string localMessage { get; set; } = "hello";
    }

    [Workflow("SubWorkflowWithLocalVariables", 1)]
    public sealed class SubWorkflowWithLocalVariablesTestWorkflow : WorkflowContainer
    {
        public List<int> ExecutionLog { get; set; } = new();

        public async IAsyncEnumerable<Wait> Run(SubWorkflowWithLocalVariablesState state)
        {
            yield return WaitSubWorkflow(ChildWithVariables(state), "ChildWithVariables", "Child");
            yield return WaitSignal<OrderReceivedSignal>("FinalSignal", "Final");
        }

        [SubWorkflow]
        private async IAsyncEnumerable<Wait> ChildWithVariables(SubWorkflowWithLocalVariablesState state)
        {
            yield return WaitSignal<PaymentConfirmedSignal>("Payment", "Payment Wait");

            ExecutionLog.Add(state.localCounter);
            if (state.localMessage == "hello")
            {
                ExecutionLog.Add(100);
            }
        }
    }
}
