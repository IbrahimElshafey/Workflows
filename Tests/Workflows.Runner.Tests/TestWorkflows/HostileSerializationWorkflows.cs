using Workflows.Definition;
using Workflows.Runner.Tests.TestData;

namespace Workflows.Runner.Tests.TestWorkflows
{
    /// <summary>
    /// Hostile fixture: <see cref="CachedToken"/> is mutated before the yield but excluded from
    /// serialization, so the mutation is silently dropped when the state is rehydrated.
    /// The WF010 analyzer rule flags this shape at authoring time.
    /// </summary>
    [Workflow("JsonIgnorePropertyWorkflow", 1)]
    public sealed class JsonIgnorePropertyWorkflow : WorkflowContainer
    {
        public List<string> ExecutionLog { get; set; } = new();

        [Newtonsoft.Json.JsonIgnore]
        public string CachedToken { get; set; } = "initial";

        public async IAsyncEnumerable<Wait> Run(JsonIgnorePropertyWorkflowState state = null!)
        {
            CachedToken = "mutated-before-yield";
            ExecutionLog.Add($"Before: {CachedToken}");

            yield return WaitSignal<OrderReceivedSignal>("OrderReceived", "Wait for order");

            ExecutionLog.Add($"After: {CachedToken}");
        }
    }

    /// <summary>
    /// Hostile fixture: an interface-typed property has no polymorphic setup, so it cannot be
    /// reconstructed from the serialized state. The WF011 analyzer rule flags this shape at
    /// authoring time; at runtime the deserialization fails loudly.
    /// </summary>
    [Workflow("InterfacePropertyWorkflow", 1)]
    public sealed class InterfacePropertyWorkflow : WorkflowContainer
    {
        public List<string> ExecutionLog { get; set; } = new();

        public IOrderLedger Ledger { get; set; } = new InMemoryOrderLedger { Size = 1 };

        public async IAsyncEnumerable<Wait> Run(InterfacePropertyWorkflowState state = null!)
        {
            ExecutionLog.Add($"Before: {Ledger.Size}");

            yield return WaitSignal<OrderReceivedSignal>("OrderReceived", "Wait for order");

            ExecutionLog.Add($"After: {Ledger.Size}");
        }
    }

    public interface IOrderLedger
    {
        int Size { get; set; }
    }

    public sealed class InMemoryOrderLedger : IOrderLedger
    {
        public int Size { get; set; }
    }

    public class JsonIgnorePropertyWorkflowState
    {
    }

    public class InterfacePropertyWorkflowState
    {
    }
}