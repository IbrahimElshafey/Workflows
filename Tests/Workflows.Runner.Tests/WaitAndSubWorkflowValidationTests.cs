using System;
using System.Collections.Generic;
using FluentAssertions;
using Newtonsoft.Json.Schema.Generation;
using Workflows.Definition;
using Workflows.Primitives;
using Workflows.Runner;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class WaitAndSubWorkflowValidationTests
    {
        [Fact]
        public void RegisterWorkflow_ShouldThrow_WhenDuplicateWaitNamesExist()
        {
            var builder = new WorkflowBuilder(new MockSchemaGenerator());
            Action act = () => builder.RegisterWorkflow<DuplicateNameWorkflow>();
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Wait name 'WaitA' is duplicate*");
        }

        [Fact]
        public void RegisterWorkflow_ShouldThrow_WhenSubWorkflowIsPublic()
        {
            var builder = new WorkflowBuilder(new MockSchemaGenerator());
            Action act = () => builder.RegisterWorkflow<PublicSubWorkflowWorkflow>();
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*must be private*");
        }

        [Fact]
        public void RegisterWorkflow_ShouldThrow_WhenSubWorkflowIsMissingAttribute()
        {
            var builder = new WorkflowBuilder(new MockSchemaGenerator());
            Action act = () => builder.RegisterWorkflow<MissingAttributeSubWorkflowWorkflow>();
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*missing [SubWorkflow] attribute*");
        }

        [Fact]
        public void DefineWait_ShouldThrow_WhenNameIsMissing()
        {
            var workflow = new MissingNameWorkflow();
            var enumerator = workflow.Run().GetAsyncEnumerator();

            Func<Task> act = async () =>
            {
                await enumerator.MoveNextAsync();
            };

            act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Wait name is mandatory*");
        }

        [Workflow("DuplicateNameWorkflow", 1)]
        public sealed class DuplicateNameWorkflow : WorkflowContainer
        {
            public async IAsyncEnumerable<Wait> Run()
            {
                yield return WaitSignal<string>("Sig1", "WaitA");
                yield return WaitSignal<string>("Sig2", "WaitA"); // Duplicate!
            }
        }

        [Workflow("PublicSubWorkflowWorkflow", 1)]
        public sealed class PublicSubWorkflowWorkflow : WorkflowContainer
        {
            public async IAsyncEnumerable<Wait> Run()
            {
                yield return WaitSubWorkflow(Child(), "ChildWait");
            }

            [SubWorkflow]
            public async IAsyncEnumerable<Wait> Child() // Public sub-workflow is invalid
            {
                yield return WaitSignal<string>("Sig", "WaitSig");
            }
        }

        [Workflow("MissingAttributeSubWorkflowWorkflow", 1)]
        public sealed class MissingAttributeSubWorkflowWorkflow : WorkflowContainer
        {
            public async IAsyncEnumerable<Wait> Run()
            {
                yield return WaitSubWorkflow(Child(), "ChildWait");
            }

            private async IAsyncEnumerable<Wait> Child() // Missing [SubWorkflow] is invalid
            {
                yield return WaitSignal<string>("Sig", "WaitSig");
            }
        }

        [Workflow("MissingNameWorkflow", 1)]
        public sealed class MissingNameWorkflow : WorkflowContainer
        {
            public async IAsyncEnumerable<Wait> Run()
            {
                yield return WaitSignal<string>("Sig", null);
            }
        }
    }
}

