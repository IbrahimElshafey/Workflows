using System;
using FluentAssertions;
using Workflows.Definition;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class WithStateValidationTests
    {
        public class StubSignal
        {
            public int Id { get; set; }
        }

        public class NamedState
        {
            public int Id { get; set; }
        }

        [Workflow("WithStateValidationTest", 1)]
        public sealed class WithStateValidationWorkflow : WorkflowContainer
        {
            public void WithAnonymousStateOnSignal()
            {
                WaitSignal<StubSignal>("AnonymousSignal", "Anonymous state wait")
                    .WithState(new { Id = 1 });
            }

            public void WithAnonymousStateOnGroup()
            {
                var child = WaitSignal<StubSignal>("GroupChild", "Group child wait");
                WaitGroup(new Wait[] { child }, "Anonymous state group")
                    .WithState(new { Id = 1 });
            }

            public void WithNamedState()
            {
                WaitSignal<StubSignal>("NamedSignal", "Named state wait")
                    .WithState(new NamedState { Id = 1 });
            }

            public void WithTupleState()
            {
                WaitSignal<StubSignal>("TupleSignal", "Tuple state wait")
                    .WithState((Count: 1, Flag: true));
            }
        }

        [Fact]
        public void WithState_AnonymousTypeOnSignal_ShouldThrow()
        {
            var workflow = new WithStateValidationWorkflow();

            Action act = workflow.WithAnonymousStateOnSignal;

            act.Should().Throw<ArgumentException>()
                .WithParameterName("state")
                .WithMessage("*Anonymous type*");
        }

        [Fact]
        public void WithState_AnonymousTypeOnGroup_ShouldThrow()
        {
            var workflow = new WithStateValidationWorkflow();

            Action act = workflow.WithAnonymousStateOnGroup;

            act.Should().Throw<ArgumentException>()
                .WithParameterName("state")
                .WithMessage("*Anonymous type*");
        }

        [Fact]
        public void WithState_NamedPoco_ShouldNotThrow()
        {
            var workflow = new WithStateValidationWorkflow();

            Action act = workflow.WithNamedState;

            act.Should().NotThrow();
        }

        [Fact]
        public void WithState_TupleState_ShouldNotThrow()
        {
            var workflow = new WithStateValidationWorkflow();

            Action act = workflow.WithTupleState;

            act.Should().NotThrow();
        }
    }
}
