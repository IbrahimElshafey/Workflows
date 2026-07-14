using Workflows.Definition;
using Workflows.Abstraction.Enums;
using Workflows.Primitives;

namespace Workflows.Runner.Tests.TestWorkflows
{
    /// <summary>
    /// Version 1 of a simple side-by-side test workflow.
    /// </summary>
    [Workflow("SxSWorkflow", 1)]
    public sealed class SxSWorkflowV1 : WorkflowContainer
    {
        public List<string> ExecutionLog { get; set; } = new();

        public async IAsyncEnumerable<Wait> Run(SxSWorkflowStateV1 state = null!)
        {
            ExecutionLog.Add("V1: Start");
            yield return WaitSignal<SxSWorkflowSignal>("SxSWorkflowSignal", "Wait for signal");
            ExecutionLog.Add("V1: End");
        }
    }

    /// <summary>
    /// Version 2 of a simple side-by-side test workflow.
    /// </summary>
    [Workflow("SxSWorkflow", 2)]
    public sealed class SxSWorkflowV2 : WorkflowContainer
    {
        public List<string> ExecutionLog { get; set; } = new();

        public async IAsyncEnumerable<Wait> Run(SxSWorkflowStateV2 state = null!)
        {
            ExecutionLog.Add("V2: Start");
            yield return WaitSignal<SxSWorkflowSignal>("SxSWorkflowSignal", "Wait for signal");
            ExecutionLog.Add("V2: End");
        }
    }

    public sealed class SxSWorkflowSignal
    {
        public string Value { get; set; } = string.Empty;
    }

    public class SxSWorkflowStateV1
    {
    }

    public class SxSWorkflowStateV2
    {
    }
}