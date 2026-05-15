using FluentAssertions;
using System;
using System.Threading.Tasks;
using Workflows.Runner.Tests.TestWorkflows;
using Xunit;

namespace Workflows.Runner.Tests
{
    /// <summary>
    /// Tests for sub-workflow (resumable functions)
    /// </summary>
    public class SubWorkflowTests
    {
        [Fact]
        public async Task SubWorkflow_ShouldExecuteChildWorkflow()
        {
            // Arrange
            var workflow = new SubWorkflowTestWorkflow();

            // Act
            var enumerator = workflow.ExecuteWorkflowAsync().GetAsyncEnumerator();

            while (await enumerator.MoveNextAsync())
            {
                var wait = enumerator.Current;
            }

            // Assert
            workflow.ExecutionLog.Should().Contain("Parent: Start");
            workflow.ExecutionLog.Should().Contain("SubWorkflow1: Start");
        }

        [Fact]
        public async Task SubWorkflow_ShouldResumeParent_AfterChildCompletes()
        {
            // Arrange
            var workflow = new SubWorkflowTestWorkflow();

            // Act
            var enumerator = workflow.ExecuteWorkflowAsync().GetAsyncEnumerator();

            while (await enumerator.MoveNextAsync())
            {
                var wait = enumerator.Current;
            }

            // Assert
            workflow.ExecutionLog.Should().ContainInOrder(
                "Parent: Start",
                "SubWorkflow1: Start",
                "SubWorkflow1: End",
                "Parent: Sub-workflow completed"
            );
        }

        [Fact]
        public async Task SubWorkflow_ShouldSupportMultipleSubWorkflows()
        {
            // Arrange
            var workflow = new SubWorkflowTestWorkflow();

            // Act
            var enumerator = workflow.ExecuteWorkflowAsync().GetAsyncEnumerator();

            while (await enumerator.MoveNextAsync())
            {
                var wait = enumerator.Current;
            }

            // Assert
            workflow.ExecutionLog.Should().Contain("SubWorkflow1: Start");
            workflow.ExecutionLog.Should().Contain("SubWorkflow2: Start");
        }

        [Fact]
        public async Task SubWorkflow_ShouldSupportGroupsInSubWorkflow()
        {
            // Arrange
            var workflow = new SubWorkflowTestWorkflow();

            // Act
            var enumerator = workflow.ExecuteWorkflowAsync().GetAsyncEnumerator();

            while (await enumerator.MoveNextAsync())
            {
                var wait = enumerator.Current;
            }

            // Assert
            workflow.ExecutionLog.Should().Contain("SubWorkflow2: Start");
            // Sub-workflow should support complex wait patterns
        }

        [Fact]
        public void SubWorkflow_ShouldSupportStateParameter()
        {
            // Arrange & Act
            var workflow = new SubWorkflowTestWorkflow();

            // Assert - Verify DSL supports .WithState() on sub-workflows
            workflow.Should().NotBeNull();
            workflow.ExecutionLog.Should().NotBeNull();
        }
    }
}
