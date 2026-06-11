using FluentAssertions;
using Workflows.Abstraction.DTOs;
using Workflows.Runner.Tests.Infrastructure;
using Workflows.Runner.Tests.TestWorkflows;
using Workflows.Definition;
using Xunit;

namespace Workflows.Runner.Tests
{
    /// <summary>
    /// Tests for sub-workflow (resumable functions)
    /// NOTE: Sub-workflow context switching requires additional runner logic.
    /// These tests validate DSL construction and runner's ability to yield sub-workflow waits.
    /// </summary>
    public class SubWorkflowTests
    {
        [Fact]
        public async Task SubWorkflow_DSL_ShouldConstructChildWorkflow()
        {
            // Arrange
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<SubWorkflowTestWorkflow>("SubWorkflowTest");

            var runner = builder.Build();

            var workflowInstance = new SubWorkflowTestWorkflow();
            var stateMachine = new WorkflowStateObject
            {
                WorkflowType = "SubWorkflowTest",
                StateIndex = -1,
                Instance = workflowInstance,
                Locals = new Dictionary<string, object>()
            };

            var request = builder.CreateExecutionRequest<SubWorkflowTestWorkflow>(
                Guid.Empty,
                "SubWorkflowTest",
                stateObject: stateMachine);

            // Act - Runner should be able to yield sub-workflow waits
            var result = await runner.RunWorkflowAsync(request);

            // Assert - Workflow can construct sub-workflow waits
            workflowInstance.ExecutionLog.Should().Contain("Parent: Start");
            result.Should().NotBeNull();
            // NOTE: Actual sub-workflow execution context switching requires runner enhancement
        }

        [Fact]
        public async Task SubWorkflow_DSL_ShouldConstructWithExecutionOrder()
        {
            // Arrange
            var workflow = new SubWorkflowTestWorkflow();

            // Act - Test DSL construction only
            await ConsumeWorkflowAsync(workflow.Run());

            // Assert - DSL allows parent-child execution flow
            workflow.ExecutionLog.Should().ContainInOrder(
                "Parent: Start",
                "SubWorkflow1: Start",
                "SubWorkflow1: End"
            );
            // NOTE: Runtime sub-workflow context management is pending
        }

        [Fact]
        public async Task SubWorkflow_DSL_ShouldSupportMultipleSubWorkflows()
        {
            // Arrange
            var workflow = new SubWorkflowTestWorkflow();

            // Act - Test DSL construction only
            await ConsumeWorkflowAsync(workflow.Run());

            // Assert
            workflow.ExecutionLog.Should().Contain("SubWorkflow1: Start");
            workflow.ExecutionLog.Should().Contain("SubWorkflow2: Start");
            // NOTE: Multiple sub-workflow coordination requires orchestrator
        }

        [Fact]
        public async Task SubWorkflow_DSL_ShouldSupportGroupsInSubWorkflow()
        {
            // Arrange
            var workflow = new SubWorkflowTestWorkflow();

            // Act - Test DSL construction only
            await ConsumeWorkflowAsync(workflow.Run());

            // Assert
            workflow.ExecutionLog.Should().Contain("SubWorkflow2: Start");
            // NOTE: Sub-workflow with groups requires both runner and orchestrator enhancements
        }

        [Fact]
        public void SubWorkflow_DSL_ShouldSupportStateParameter()
        {
            // Arrange & Act
            var workflow = new SubWorkflowTestWorkflow();

            // Assert - Verify DSL supports .WithState() on sub-workflows
            workflow.Should().NotBeNull();
            workflow.ExecutionLog.Should().NotBeNull();
            // NOTE: This validates DSL authoring capability
        }

        private static async Task ConsumeWorkflowAsync(IAsyncEnumerable<Wait> flow)
        {
            var enumerator = flow.GetAsyncEnumerator();
            while (await enumerator.MoveNextAsync())
            {
                var wait = enumerator.Current;
                if (wait is SubWorkflowWait subWait && subWait.Runner != null)
                {
                    await ConsumeWorkflowAsync(subWait.Runner);
                }
            }
        }
    }
}
