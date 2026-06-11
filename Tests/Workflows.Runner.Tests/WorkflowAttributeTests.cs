using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json.Schema.Generation;
using Workflows.Definition;
using Workflows.Definition.Registration;
using Workflows.Primitives;
using Workflows.Runner;
using Xunit;

namespace Workflows.Runner.Tests
{
    public class WorkflowAttributeTests
    {
        [Fact]
        public void RegisterWorkflow_ShouldRegisterSuccessfully_WhenWorkflowHasAttribute()
        {
            // Arrange
            var builder = new WorkflowBuilder(new MockSchemaGenerator());

            // Act
            builder.RegisterWorkflow<AnnotatedTestWorkflow>();

            // Assert
            builder.Workflows.Should().ContainKey("AnnotatedWorkflow");
            var registration = builder.Workflows["AnnotatedWorkflow"];
            registration.WorkflowContainer.Should().Be(typeof(AnnotatedTestWorkflow));
        }

        [Fact]
        public void RegisterWorkflow_ShouldThrow_WhenWorkflowDoesNotHaveAttribute()
        {
            // Arrange
            var builder = new WorkflowBuilder(new MockSchemaGenerator());

            // Act
            Action act = () => builder.RegisterWorkflow<UnannotatedTestWorkflow>();

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*UnannotatedTestWorkflow*not decorated with [WorkflowAttribute]*");
        }

        [Fact]
        public void RegisterFromAssemblyContaining_ShouldRegisterAnnotatedWorkflows_AndThrowOnMissingAttribute_WhenNoVersionPassed()
        {
            // Arrange
            var builder = new WorkflowBuilder(new MockSchemaGenerator());

            // Act & Assert
            // Since there are unannotated workflows in the test assembly, calling parameterless scan should throw
            Action act = () => builder.RegisterFromAssemblyContaining<AnnotatedTestWorkflow>();
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*is missing [WorkflowAttribute]*");
        }

        [Fact]
        public void RegisterFromAssemblyContaining_ShouldThrowOnMissingAttribute_WhenVersionPassed()
        {
            // Arrange
            var builder = new WorkflowBuilder(new MockSchemaGenerator());

            // Act
            Action act = () => builder.RegisterFromAssemblyContaining<AnnotatedTestWorkflow>(3);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*is missing [WorkflowAttribute]*");
        }
    }

    [Workflow("AnnotatedWorkflow", 2)]
    public sealed class AnnotatedTestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            yield break;
        }
    }

    public sealed class UnannotatedTestWorkflow : WorkflowContainer
    {
        public async IAsyncEnumerable<Wait> Run()
        {
            yield break;
        }
    }
}

