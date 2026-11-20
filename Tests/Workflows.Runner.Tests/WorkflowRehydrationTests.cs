using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Runner.Tests.TestData;
using Workflows.Runner.Tests.TestWorkflows;
using Workflows.TestShell;
using Xunit;

namespace Workflows.Runner.Tests
{
    /// <summary>
    /// Runs the shell-driven test workloads twice: with one runner alive for the whole workflow
    /// (<c>direct</c>) and with a runner that is thrown away and rebuilt from the serialized state
    /// after every yield (<c>rehydrating</c>). The rehydrating mode is what a worker restart or a
    /// provider failover looks like, so it is the mode that catches state which cannot survive
    /// serialization.
    /// </summary>
    public class WorkflowRehydrationTests
    {
        private const string Direct = "direct";
        private const string Rehydrating = "rehydrating";

        public static TheoryData<string> Modes => new() { Direct, Rehydrating };

        private static WorkflowTestShell CreateShell(string mode)
        {
            var rehydrationMode = string.Equals(mode, Rehydrating, StringComparison.Ordinal)
                ? RehydrationMode.Rehydrating
                : RehydrationMode.Direct;

            var shell = new WorkflowTestShell(rehydrationMode);
            shell.Mode.Should().Be(rehydrationMode);
            return shell;
        }

        private static IEnumerable<WaitInfrastructureDto> Flatten(IEnumerable<WaitInfrastructureDto>? waits)
        {
            if (waits == null) yield break;

            foreach (var wait in waits)
            {
                yield return wait;

                foreach (var child in Flatten(wait.ChildWaits))
                {
                    yield return child;
                }

                if (wait is ExternalGroupWaitDto externalGroup)
                {
                    foreach (var child in Flatten(externalGroup.ExternalChildWaits))
                    {
                        yield return child;
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task FirstWaitAndResume_CompletesWithFullHistory(string mode)
        {
            using var shell = CreateShell(mode);
            shell.RegisterWorkflow<FirstWaitAndResumeWorkflow>("FirstWaitTest", 1)
                 .RegisterSignal<OrderReceivedSignal>("OrderReceived")
                 .RegisterSignal<PaymentConfirmedSignal>("Payment1")
                 .RegisterSignal<PaymentConfirmedSignal>("Payment2")
                 .RegisterSignal<ShipmentSignal>("FinalShipment")
                 .RegisterCommand<ProcessPaymentCommand, ProcessPaymentResult>("ProcessPayment");

            (await shell.StartWorkflowAsync("FirstWaitTest")).Should().NotBeNull();
            shell.CurrentStatus.Should().Be(WorkflowInstanceStatus.Running);
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "First wait");

            await shell.SimulateSignalAsync("OrderReceived", new OrderReceivedSignal
            {
                OrderId = "ORD-001",
                Amount = 1500
            });
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "ProcessPayment");

            await shell.SimulateCommandResultAsync("ProcessPayment", new ProcessPaymentResult
            {
                Success = true,
                TransactionId = "TX-001"
            });
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "PaymentGroup");

            await shell.SimulateSignalAsync("Payment1", new PaymentConfirmedSignal
            {
                TransactionId = "TX-PAY-01"
            });
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "DelayWait");

            var delayWait = shell.ActiveWaits.Single();
            await shell.SimulateCommandResultByWaitIdAsync(delayWait.Id, null!);
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "Final wait");

            await shell.SimulateSignalAsync("FinalShipment", new ShipmentSignal
            {
                TrackingNumber = "TRACK-999"
            });

            shell.CurrentStatus.Should().Be(WorkflowInstanceStatus.Completed);

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
                "Execution6: Completed - Total resumes: 4");
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task NestedGroups_AdvanceThroughEveryLevel(string mode)
        {
            using var shell = CreateShell(mode);
            shell.RegisterWorkflow<NestedGroupsTestWorkflow>("NestedGroupsTest", 1)
                 .RegisterSignal<PaymentConfirmedSignal>("PaymentConfirmed")
                 .RegisterSignal<PaymentConfirmedSignal>("PaymentBackup")
                 .RegisterSignal<ShipmentSignal>("ShipmentReady")
                 .RegisterSignal<ShipmentSignal>("ShipmentBackup");

            await shell.StartWorkflowAsync("NestedGroupsTest");
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "OrderCompletionGroup");

            await shell.SimulateSignalAsync("PaymentConfirmed", new PaymentConfirmedSignal
            {
                TransactionId = "TX-PAY-1"
            });
            shell.CurrentStatus.Should().Be(WorkflowInstanceStatus.Running);

            await shell.SimulateSignalAsync("ShipmentReady", new ShipmentSignal
            {
                TrackingNumber = "TRACK-SHIP-1"
            });
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "WarehouseFulfillmentGroup");

            var workflow = (NestedGroupsTestWorkflow)shell.CurrentState!.StateObject.Instance;
            workflow.ExecutionLog.Should().ContainInOrder(
                "Start",
                "Payment1: TX-PAY-1",
                "Shipment1: TRACK-SHIP-1",
                "Both payment and shipment completed");
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task SubWorkflow_YieldsChildWaitAndKeepsParentLog(string mode)
        {
            using var shell = CreateShell(mode);
            shell.RegisterWorkflow<SubWorkflowTestWorkflow>("SubWorkflowTest", 1)
                 .RegisterSignal<OrderReceivedSignal>("OrderReceived")
                 .RegisterCommand<ReserveInventoryCommand, ReserveInventoryResult>("ReserveInventory")
                 .SetupCommandHandler<ReserveInventoryCommand, ReserveInventoryResult>(
                     "ReserveInventory",
                     _ => Task.FromResult(new ReserveInventoryResult { ReservationId = "RES-001", Success = true }));

            await shell.StartWorkflowAsync("SubWorkflowTest");
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "Initial order");

            await shell.SimulateSignalAsync("OrderReceived", new OrderReceivedSignal
            {
                OrderId = "ORD-ABC"
            });

            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "ProcessOrder");

            var workflow = (SubWorkflowTestWorkflow)shell.CurrentState!.StateObject.Instance;
            workflow.ExecutionLog.Should().ContainInOrder(
                "Parent: Start",
                "Parent: Order received - ORD-ABC");
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task FanOut_AllChildrenMustBeSignalledBeforeCompletion(string mode)
        {
            using var shell = CreateShell(mode);
            shell.RegisterWorkflow<FanOutTestWorkflow>()
                 .RegisterSignal<FanOutSignal>("FanOutSignal");

            await shell.StartWorkflowAsync("FanOutTestWorkflow");
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "WaitForAll");

            for (var value = 1; value <= 3; value++)
            {
                var child = Flatten(shell.CurrentState!.Waits)
                    .OfType<SignalWaitDto>()
                    .First(w => w.Status == WaitStatus.Waiting);

                await shell.SimulateSignalByWaitIdAsync(child.Id, new FanOutSignal { Value = value.ToString() });

                if (value < 3)
                {
                    shell.CurrentStatus.Should().Be(WorkflowInstanceStatus.Running);
                }
            }

            shell.CurrentStatus.Should().Be(WorkflowInstanceStatus.Completed);

            var workflow = shell.CurrentState!.StateObject.Instance as FanOutTestWorkflow;
            workflow.Should().NotBeNull();
            workflow!.Completed.Should().BeTrue();
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task CompensationWorkflow_CompletesWithoutRollback(string mode)
        {
            using var shell = CreateShell(mode);
            shell.RegisterWorkflow<CompensationTestWorkflow>()
                 .RegisterCommand<ReserveInventoryCommand, ReserveInventoryResult>("ReserveInventory")
                 .RegisterCommand<ProcessPaymentCommand, ProcessPaymentResult>("ProcessPayment")
                 .SetupCommandHandler<ReserveInventoryCommand, ReserveInventoryResult>(
                     "ReserveInventory",
                     _ => Task.FromResult(new ReserveInventoryResult { ReservationId = "RES-001", Success = true }));

            await shell.StartWorkflowAsync("CompensationTestWorkflow");
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "ReserveInventory");

            await shell.SimulateCommandResultAsync("ReserveInventory", new ReserveInventoryResult
            {
                ReservationId = "RES-001",
                Success = true
            });
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "ProcessPayment");

            await shell.SimulateCommandResultAsync("ProcessPayment", new ProcessPaymentResult
            {
                Success = true,
                TransactionId = "TX-1"
            });

            shell.CurrentStatus.Should().Be(WorkflowInstanceStatus.Completed);

            var workflow = (CompensationTestWorkflow)shell.CurrentState!.StateObject.Instance;
            workflow.ExecutionLog.Should().ContainInOrder(
                "Start",
                "Inventory reserved: RES-001",
                "Payment processed: TX-1",
                "End");
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Cancellation_WalksEverySignalToCompletion(string mode)
        {
            using var shell = CreateShell(mode);
            shell.RegisterWorkflow<CancellationTestWorkflow>()
                 .RegisterSignal<OrderReceivedSignal>("OrderReceived")
                 .RegisterSignal<ShipmentSignal>("ShipmentReady")
                 .RegisterSignal<PaymentConfirmedSignal>("PaymentConfirmed");

            await shell.StartWorkflowAsync("CancellationTestWorkflow");
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "Wait for order");

            await shell.SimulateSignalAsync("OrderReceived", new OrderReceivedSignal
            {
                OrderId = "ORD-1",
                Amount = 1500
            });
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "Wait for shipment");

            await shell.SimulateSignalAsync("ShipmentReady", new ShipmentSignal
            {
                TrackingNumber = "TRK-1"
            });
            shell.ActiveWaits.Should().ContainSingle(w => w.WaitName == "Wait for payment");

            await shell.SimulateSignalAsync("PaymentConfirmed", new PaymentConfirmedSignal
            {
                TransactionId = "TX-PAY"
            });

            shell.CurrentStatus.Should().Be(WorkflowInstanceStatus.Completed);

            var workflow = (CancellationTestWorkflow)shell.CurrentState!.StateObject.Instance;
            workflow.ExecutionLog.Should().ContainInOrder(
                "Start",
                "Order received: ORD-1",
                "Shipment ready: TRK-1",
                "Payment confirmed",
                "End");
        }
    }
}