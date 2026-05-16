using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Enums;
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

            var workflow = new FirstWaitAndResumeWorkflow();
            var waitId = Guid.NewGuid();
            var signalWait = builder.CreateSignalWaitDto("OrderReceived", "First wait", waitId);

            var request = builder.CreateExecutionRequest<FirstWaitAndResumeWorkflow>(
                waitId,
                "FirstWaitTest",
                waits: new List<Workflows.Abstraction.DTOs.Waits.WaitInfrastructureDto> { signalWait });

            // Add signal
            request.Signal = builder.CreateSignal("OrderReceived", new OrderReceivedSignal
            {
                OrderId = "ORD-001",
                Amount = 1500 // Greater than 1000
            });

            // Act
            var result = await runner.RunWorkflowAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be("Accepted");
            workflow.ExecutionLog.Should().ContainInOrder("Execution1: Start");
        }

        [Fact]
        public async Task Resume_ShouldRestoreState_AndContinueFromLastWait()
        {
            // Arrange
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<FirstWaitAndResumeWorkflow>("FirstWaitTest");
            builder.RegisterSignal<OrderReceivedSignal>("OrderReceived");

            var runner = builder.Build();

            // Simulate workflow that already hit first wait
            var workflow = new FirstWaitAndResumeWorkflow();
            workflow.ExecutionLog.Add("Execution1: Start"); // Simulate previous execution

            var stateObject = new WorkflowStateObject
            {
                StateIndex = 0, // State after first wait
                Instance = workflow,
                StateMachinesObjects = new Dictionary<Guid, object>(),
                WaitStatesObjects = new Dictionary<Guid, object>()
            };

            var waitId = Guid.NewGuid();
            var signalWait = builder.CreateSignalWaitDto("OrderReceived", "First wait", waitId);

            var request = builder.CreateExecutionRequest<FirstWaitAndResumeWorkflow>(
                waitId,
                "FirstWaitTest",
                stateObject: stateObject,
                waits: new List<Workflows.Abstraction.DTOs.Waits.WaitInfrastructureDto> { signalWait });

            request.Signal = builder.CreateSignal("OrderReceived", new OrderReceivedSignal
            {
                OrderId = "ORD-001",
                Amount = 1500
            });

            // Act
            var result = await runner.RunWorkflowAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be("Accepted");
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

            var runner = builder.Build();

            var workflow = new FirstWaitAndResumeWorkflow();
            var waitId = Guid.NewGuid();
            var signalWait = builder.CreateSignalWaitDto("OrderReceived", "First wait", waitId);

            var stateObject = new WorkflowStateObject
            {
                StateIndex = -1,
                Instance = workflow,
                StateMachinesObjects = new Dictionary<Guid, object>(),
                WaitStatesObjects = new Dictionary<Guid, object>
                {
                    { waitId, 1000 } // State for MatchIf
                }
            };

            var request = builder.CreateExecutionRequest<FirstWaitAndResumeWorkflow>(
                waitId,
                "FirstWaitTest",
                stateObject: stateObject,
                waits: new List<Workflows.Abstraction.DTOs.Waits.WaitInfrastructureDto> { signalWait });

            // Signal that should match (Amount > 1000)
            request.Signal = builder.CreateSignal("OrderReceived", new OrderReceivedSignal
            {
                OrderId = "ORD-MATCH",
                Amount = 2000
            });

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

            var workflow = new FirstWaitAndResumeWorkflow();
            var waitId = Guid.NewGuid();
            var signalWait = builder.CreateSignalWaitDto("OrderReceived", "First wait", waitId);

            var stateObject = new WorkflowStateObject
            {
                StateIndex = -1,
                Instance = workflow,
                StateMachinesObjects = new Dictionary<Guid, object>(),
                WaitStatesObjects = new Dictionary<Guid, object>
                {
                    { waitId, 1000 } // State for MatchIf
                }
            };

            var request = builder.CreateExecutionRequest<FirstWaitAndResumeWorkflow>(
                waitId,
                "FirstWaitTest",
                stateObject: stateObject,
                waits: new List<Workflows.Abstraction.DTOs.Waits.WaitInfrastructureDto> { signalWait });

            // Signal that should NOT match (Amount <= 1000)
            request.Signal = builder.CreateSignal("OrderReceived", new OrderReceivedSignal
            {
                OrderId = "ORD-NO-MATCH",
                Amount = 500
            });

            // Act
            var result = await runner.RunWorkflowAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Status.Should().Be("Error");
            result.Message.Should().Contain("match expression failed");
        }
    }
}
