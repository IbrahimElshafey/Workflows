using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Runner.Pipeline;
using Workflows.Runner.Pipeline.Matchers;
using Workflows.TestShell;
using Workflows.Runner.Tests.TestData;
using Workflows.Runner.Tests.TestWorkflows;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class TestShellTests
    {
        [Fact]
        public async Task TestShell_ShouldManageCompleteWorkflowLifecycle()
        {
            // Arrange
            using var shell = new WorkflowTestShell();
            shell.RegisterWorkflow<FirstWaitAndResumeWorkflow>("FirstWaitTest", "1.0")
                 .RegisterSignal<OrderReceivedSignal>("OrderReceived")
                 .RegisterSignal<PaymentConfirmedSignal>("Payment1")
                 .RegisterSignal<PaymentConfirmedSignal>("Payment2")
                 .RegisterSignal<ShipmentSignal>("FinalShipment")
                 .RegisterCommand<ProcessPaymentCommand, ProcessPaymentResult>("ProcessPayment");

            // Act: Start the workflow
            var state = await shell.StartWorkflowAsync("FirstWaitTest");

            // Assert starts and yields First wait
            state.Should().NotBeNull();
            shell.CurrentStatus.Should().Be(WorkflowInstanceStatus.Running);
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "First wait");

            // Act: Resume first wait (Signal)
            state = await shell.SimulateSignalAsync("OrderReceived", new OrderReceivedSignal
            {
                OrderId = "ORD-001",
                Amount = 1500
            });

            // Assert second wait (Deferred Command) is yielded
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "ProcessPayment");

            // Act: Resume second wait (CommandResult)
            state = await shell.SimulateCommandResultAsync("ProcessPayment", new ProcessPaymentResult
            {
                Success = true,
                TransactionId = "TX-001"
            });

            // Assert third wait (GroupWait) is yielded
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "PaymentGroup");

            // Act: Resume third wait (Signal inside GroupWait)
            state = await shell.SimulateSignalAsync("Payment1", new PaymentConfirmedSignal
            {
                TransactionId = "TX-PAY-01"
            });

            // Assert fourth wait (TimeWait / Delay) is yielded
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "DelayWait");

            // Act: Resume fourth wait (TimeWait by ID)
            var delayWait = shell.ActiveWaits.First();
            state = await shell.SimulateCommandResultAsync(delayWait.Id, null);

            // Assert fifth wait (Final Shipment Wait)
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "Final wait");

            // Act: Resume final wait
            state = await shell.SimulateSignalAsync("FinalShipment", new ShipmentSignal
            {
                TrackingNumber = "TRACK-999"
            });

            // Assert completion
            shell.CurrentStatus.Should().Be(WorkflowInstanceStatus.Completed);
            
            // Verify execution log of the container instance is preserved and correct
            var workflow = (FirstWaitAndResumeWorkflow)shell.CurrentState!.StateObject.Instance;
            workflow.ExecutionLog.Should().ContainInOrder(
                "Execution1: Start",
                "Execution1: First wait completed - Order: ORD-001, MinAmount: 1000",
                "Execution2: After first resume",
                "Execution2: Payment processed - TxId: TX-001, State: PaymentState",
                "Execution3: After second resume",
                "Execution3: Payment1 - TX-PAY-01",
                "Execution4: After third resume",
                "Execution5: After delay resume",
                "Execution5: Final wait - Tracking: TRACK-999, Resumes: 4",
                "Execution6: Completed - Total resumes: 4"
            );
        }

        [Fact]
        public async Task TestShell_ShouldHandleGroupWaits()
        {
            // Arrange
            using var shell = new WorkflowTestShell();
            shell.RegisterWorkflow<NestedGroupsTestWorkflow>("NestedGroupsTest", "1.0")
                 .RegisterSignal<PaymentConfirmedSignal>("PaymentConfirmed")
                 .RegisterSignal<PaymentConfirmedSignal>("PaymentBackup")
                 .RegisterSignal<ShipmentSignal>("ShipmentReady")
                 .RegisterSignal<ShipmentSignal>("ShipmentBackup");

            // Act: Start
            var state = await shell.StartWorkflowAsync("NestedGroupsTest");

            // Assert we are waiting on OrderCompletionGroup (the outer group wait)
            state.Should().NotBeNull();
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "OrderCompletionGroup");

            // Act: Simulate first payment signal (completes paymentGroup)
            state = await shell.SimulateSignalAsync("PaymentConfirmed", new PaymentConfirmedSignal
            {
                TransactionId = "TX-PAY-1"
            });

            // The workflow is still running, waiting for shipmentGroup to complete
            shell.CurrentStatus.Should().Be(WorkflowInstanceStatus.Running);

            // Act: Simulate shipment signal (completes shipmentGroup, which completes OrderCompletionGroup)
            state = await shell.SimulateSignalAsync("ShipmentReady", new ShipmentSignal
            {
                TrackingNumber = "TRACK-SHIP-1"
            });

            // The outer group completes, and workflow advances to WarehouseFulfillmentGroup
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "WarehouseFulfillmentGroup");

            // Verify execution log reflects the steps completed
            var workflow = (NestedGroupsTestWorkflow)shell.CurrentState!.StateObject.Instance;
            workflow.ExecutionLog.Should().ContainInOrder(
                "Start",
                "Payment1: TX-PAY-1",
                "Shipment1: TRACK-SHIP-1",
                "Both payment and shipment completed"
            );
        }

        [Fact]
        public async Task TestShell_ShouldHandleSubWorkflows()
        {
            // Arrange
            using var shell = new WorkflowTestShell();
            shell.RegisterWorkflow<SubWorkflowTestWorkflow>("SubWorkflowTest", "1.0")
                 .RegisterSignal<OrderReceivedSignal>("OrderReceived")
                 .RegisterCommand<ReserveInventoryCommand, ReserveInventoryResult>("ReserveInventory")
                 .SetupCommandHandler<ReserveInventoryCommand, ReserveInventoryResult>("ReserveInventory",
                     cmd => Task.FromResult(new ReserveInventoryResult { ReservationId = "RES-001", Success = true }));

            // Act: Start
            var state = await shell.StartWorkflowAsync("SubWorkflowTest");

            // Assert starting the parent workflow yields the first WaitSignal
            state.Should().NotBeNull();
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "Initial order");

            // Act: Resume the initial signal wait
            state = await shell.SimulateSignalAsync("OrderReceived", new OrderReceivedSignal
            {
                OrderId = "ORD-ABC"
            });

            // Since running the child workflow requires coordination of parent-child context
            // switching (usually orchestrated), starting/yielding is correctly simulated in test shell.
            // Assert that it yielding sub-workflow wait works
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "ProcessOrder");
        }

        [Fact]
        public async Task TestShell_ShouldSupportIsolatedMatcherTesting()
        {
            // Arrange
            using var shell = new WorkflowTestShell();
            shell.RegisterWorkflow<FirstWaitAndResumeWorkflow>("FirstWaitTest", "1.0")
                 .RegisterSignal<OrderReceivedSignal>("OrderReceived");

            // Act: Start workflow to generate a first wait DTO
            var state = await shell.StartWorkflowAsync("FirstWaitTest");
            var signalWaitDto = shell.ActiveWaits.First() as SignalWaitDto;
            signalWaitDto.Should().NotBeNull();

            // Matcher test 1: Signal with amount <= 1000 should NOT match
            var signal1 = new SignalDto
            {
                SignalIdentifier = "OrderReceived",
                Data = new OrderReceivedSignal { OrderId = "ORD-1", Amount = 500 }
            };
            bool matchResult1 = await shell.SimulateMatchAsync(signalWaitDto!, signal1);
            matchResult1.Should().BeFalse();

            // Matcher test 2: Signal with amount > 1000 SHOULD match
            var signal2 = new SignalDto
            {
                SignalIdentifier = "OrderReceived",
                Data = new OrderReceivedSignal { OrderId = "ORD-2", Amount = 1500 }
            };
            bool matchResult2 = await shell.SimulateMatchAsync(signalWaitDto!, signal2);
            matchResult2.Should().BeTrue();
        }

        [Fact]
        public async Task TestShell_ShouldLogStepHistoryAndAllowDI()
        {
            // Arrange
            using var shell = new WorkflowTestShell();
            shell.RegisterWorkflow<FirstWaitAndResumeWorkflow>("FirstWaitTest", "1.0")
                 .RegisterSignal<OrderReceivedSignal>("OrderReceived");

            // Act: Resolve internal services via ServiceProvider
            var hydrator = shell.ServiceProvider.GetService<IWorkflowHydrator>();
            hydrator.Should().NotBeNull();

            // Act: Start workflow
            await shell.StartWorkflowAsync("FirstWaitTest");
            await shell.SimulateSignalAsync("OrderReceived", new OrderReceivedSignal
            {
                OrderId = "ORD-1",
                Amount = 1500
            });

            // Assert execution log was populated with step histories
            shell.ExecutionLog.Should().HaveCount(2);

            var startLog = shell.ExecutionLog[0];
            startLog.TriggeringWaitId.Should().Be(Guid.Empty);
            startLog.ConsumedWaitIds.Should().BeEmpty();
            startLog.NewWaitIds.Should().HaveCount(1);
            startLog.SerializedStateSnapshot.Should().NotBeNullOrEmpty();

            var resumeLog = shell.ExecutionLog[1];
            resumeLog.TriggeringWaitId.Should().NotBe(Guid.Empty);
            resumeLog.ConsumedWaitIds.Should().ContainSingle();
            resumeLog.NewWaitIds.Should().HaveCount(1); // The next wait is ProcessPayment
        }
    }
}
