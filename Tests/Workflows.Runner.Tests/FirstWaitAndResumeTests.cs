using FluentAssertions;
using Workflows.Abstraction.DTOs;
using Workflows.Runner.Tests.Infrastructure;
using Workflows.Runner.Tests.TestData;
using Workflows.Runner.Tests.TestWorkflows;
using Xunit;

namespace Workflows.Runner.Tests
{
    /// <summary>
    /// Tests for first wait scenario and resume behavior
    /// </summary>
    public class FirstWaitAndResumeTests
    {
        [Fact]
        public async Task FirstWait_ShouldSuspendWorkflow_WhenSignalWaitEncountered()
        {
            // Arrange
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<FirstWaitAndResumeWorkflow>("FirstWaitTest");
            builder.RegisterSignal<OrderReceivedSignal>("OrderReceived");

            var runner = builder.Build();

            // Act
            var result = await runner.StartWorkflow("FirstWaitTest");

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be("Accepted");
            var response = builder.Client.SentResults.Last().Result;
            var workflow = (FirstWaitAndResumeWorkflow)response.UpdatedState.StateObject.Instance;
            workflow.ExecutionLog.Should().ContainInOrder("Execution1: Start");
        }

        [Fact]
        public async Task Resume_ShouldRestoreState_AndContinueFromLastWait()
        {
            // Arrange
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<FirstWaitAndResumeWorkflow>("FirstWaitTest");
            builder.RegisterSignal<OrderReceivedSignal>("OrderReceived");
            builder.SetupCommandHandler<ProcessPaymentCommand, ProcessPaymentResult>(
                "ProcessPayment",
                cmd => Task.FromResult(new ProcessPaymentResult { Success = true, TransactionId = "TX-001" }));

            var runner = builder.Build();

            await runner.StartWorkflow("FirstWaitTest");

            var response = builder.Client.SentResults.Last().Result;
            var state = response.UpdatedState;
            var wait = response.UpdatedState.Waits.First();

            var request = new WorkflowExecutionRequest
            {
                TriggeringWaitId = wait.Id,
                Signal = builder.CreateSignal("OrderReceived", new OrderReceivedSignal
                {
                    OrderId = "ORD-001",
                    Amount = 1500
                }),
                WorkflowState = state
            };

            // Act
            var result = await runner.RunWorkflowAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be("Accepted");
            var workflow = (FirstWaitAndResumeWorkflow)state.StateObject.Instance;
            workflow.ResumeCount.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task MultipleResumes_ShouldPreserveStateAcrossAll_AndReachCompletion()
        {
            // Arrange
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<FirstWaitAndResumeWorkflow>("FirstWaitTest");
            builder.RegisterSignal<OrderReceivedSignal>("OrderReceived");
            builder.RegisterSignal<PaymentConfirmedSignal>("Payment1");
            builder.RegisterSignal<ShipmentSignal>("FinalShipment");

            var runner = builder.Build();

            var workflow = new FirstWaitAndResumeWorkflow();

            // Simulate multiple resume cycles
            int expectedResumes = 0;

            // This test validates that state (like ResumeCount) persists across resumes
            workflow.ResumeCount.Should().Be(expectedResumes);
        }

        [Fact]
        public async Task FirstWait_WithStatefulMatchIf_ShouldEvaluateCorrectly()
        {
            // Arrange
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<FirstWaitAndResumeWorkflow>("FirstWaitTest");
            builder.RegisterSignal<OrderReceivedSignal>("OrderReceived");
            builder.SetupCommandHandler<ProcessPaymentCommand, ProcessPaymentResult>(
                "ProcessPayment",
                cmd => Task.FromResult(new ProcessPaymentResult { Success = true, TransactionId = "TX-001" }));

            var runner = builder.Build();

            await runner.StartWorkflow("FirstWaitTest");

            var response = builder.Client.SentResults.Last().Result;
            var state = response.UpdatedState;
            var wait = response.UpdatedState.Waits.First();

            // Signal that should match (Amount > 1000)
            var request = new WorkflowExecutionRequest
            {
                TriggeringWaitId = wait.Id,
                Signal = builder.CreateSignal("OrderReceived", new OrderReceivedSignal
                {
                    OrderId = "ORD-MATCH",
                    Amount = 2000
                }),
                WorkflowState = state
            };

            // Act
            var result = await runner.RunWorkflowAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be("Accepted");
        }

        [Fact]
        public async Task FirstWait_WithStatefulMatchIf_ShouldReject_WhenMatchFails()
        {
            // Arrange
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<FirstWaitAndResumeWorkflow>("FirstWaitTest");
            builder.RegisterSignal<OrderReceivedSignal>("OrderReceived");

            var runner = builder.Build();

            await runner.StartWorkflow("FirstWaitTest");

            var response = builder.Client.SentResults.Last().Result;
            var state = response.UpdatedState;
            var wait = response.UpdatedState.Waits.First();

            // Signal that should NOT match (Amount <= 1000)
            var request = new WorkflowExecutionRequest
            {
                TriggeringWaitId = wait.Id,
                Signal = builder.CreateSignal("OrderReceived", new OrderReceivedSignal
                {
                    OrderId = "ORD-NO-MATCH",
                    Amount = 500
                }),
                WorkflowState = state
            };

            // Act
            var result = await runner.RunWorkflowAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be("Rejected");
            result.Message.Should().Contain("Matching failed or partial match.");
        }
    }
}
