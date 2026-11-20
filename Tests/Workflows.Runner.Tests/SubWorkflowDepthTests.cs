using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Runner.Tests.Infrastructure;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class SubWorkflowDepthTests
    {
        [Fact]
        public async Task SelfRecursiveSubWorkflow_ExceedingMaxDepth_ShouldFailLoudly()
        {
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<RecursiveDepthWorkflow>("RecursiveDepthWorkflow");

            var runner = builder.Build(services =>
                services.AddSingleton(new WorkflowRunnerOptions { MaxSubWorkflowDepth = 3 }));

            Func<Task> act = () => runner.StartWorkflow("RecursiveDepthWorkflow");

            var exception = await act.Should().ThrowAsync<InvalidOperationException>();
            exception.Which.Message.Should().Contain("MaxSubWorkflowDepth");
        }

        [Fact]
        public async Task BoundedSubWorkflow_WithinMaxDepth_ShouldNotThrow()
        {
            var builder = new WorkflowTestBuilder();
            builder.RegisterWorkflow<BoundedDepthWorkflow>("BoundedDepthWorkflow");

            var runner = builder.Build(services =>
                services.AddSingleton(new WorkflowRunnerOptions { MaxSubWorkflowDepth = 3 }));

            Func<Task> act = () => runner.StartWorkflow("BoundedDepthWorkflow");

            await act.Should().NotThrowAsync();
        }
    }

    [Workflow("RecursiveDepthWorkflow", 1)]
    public sealed class RecursiveDepthWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run(RecursiveDepthState state = null!)
        {
            yield return WaitSubWorkflow(Child(), Guid.NewGuid().ToString());
        }

        [SubWorkflow]
        private async IAsyncEnumerable<Wait> Child(RecursiveDepthState state = null!)
        {
            yield return WaitSubWorkflow(Child(), Guid.NewGuid().ToString());
        }
    }

    [Workflow("BoundedDepthWorkflow", 1)]
    public sealed class BoundedDepthWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run(RecursiveDepthState state = null!)
        {
            yield return WaitSubWorkflow(Child(state), "Child");
        }

        [SubWorkflow]
        private async IAsyncEnumerable<Wait> Child(RecursiveDepthState state = null!)
        {
            yield return WaitSignal<RecursiveDepthState>("ChildSignal", "Child signal");
        }
    }

    public class RecursiveDepthState
    {
    }
}