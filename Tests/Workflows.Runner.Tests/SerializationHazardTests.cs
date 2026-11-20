using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Workflows.Runner.Tests.TestData;
using Workflows.Runner.Tests.TestWorkflows;
using Workflows.TestShell;
using Xunit;

namespace Workflows.Runner.Tests
{
    /// <summary>
    /// Negative tests for state shapes that cannot survive serialization. They document how the
    /// failure shows up so that a regression is never silent: either the value is silently reset
    /// (below) or the run throws. WF010/WF011 catch both shapes before the workflow ever runs.
    /// </summary>
    public class SerializationHazardTests
    {
        public static TheoryData<string> Modes => new() { "direct", "rehydrating" };

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task JsonIgnoreProperty_IsDroppedByTheSerializedState(string mode)
        {
            using var shell = new WorkflowTestShell(
                mode == "rehydrating" ? RehydrationMode.Rehydrating : RehydrationMode.Direct);

            shell.RegisterWorkflow<JsonIgnorePropertyWorkflow>()
                 .RegisterSignal<OrderReceivedSignal>("OrderReceived");

            await shell.StartWorkflowAsync("JsonIgnorePropertyWorkflow");
            await shell.SimulateSignalAsync("OrderReceived", new OrderReceivedSignal { OrderId = "ORD-1" });

            var workflow = (JsonIgnorePropertyWorkflow)shell.CurrentState!.StateObject.Instance;

            // The pre-yield code saw the mutation because ExecutionLog is serialized ...
            workflow.ExecutionLog.Should().ContainInOrder("Before: mutated-before-yield", "After: initial");

            // ... but the [JsonIgnore] property silently fell back to its initializer.
            workflow.CachedToken.Should().Be("initial");
        }

        [Fact]
        public async Task InterfaceTypedProperty_FailsLoudlyInsteadOfSilentlyResetting()
        {
            using var shell = new WorkflowTestShell(RehydrationMode.Rehydrating);

            shell.RegisterWorkflow<InterfacePropertyWorkflow>()
                 .RegisterSignal<OrderReceivedSignal>("OrderReceived");

            // The state is persisted on every yield, so the start already trips the serializer.
            Func<Task> run = async () =>
            {
                await shell.StartWorkflowAsync("InterfacePropertyWorkflow");
                await shell.SimulateSignalAsync("OrderReceived", new OrderReceivedSignal { OrderId = "ORD-1" });
            };

            var thrown = await run.Should().ThrowAsync<Exception>(
                "a property whose type is an interface cannot be reconstructed from JSON");

            thrown.Which.ToString().Should().Contain(nameof(IOrderLedger));
        }
    }
}