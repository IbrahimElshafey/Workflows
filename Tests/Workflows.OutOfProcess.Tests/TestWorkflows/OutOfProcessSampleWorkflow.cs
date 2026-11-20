using System.Collections.Generic;
using Workflows.Definition;

namespace Workflows.OutOfProcess.Tests.TestWorkflows
{
    [Workflow("OutOfProcessSampleWorkflow", 1)]
    public sealed class OutOfProcessSampleWorkflow : WorkflowContainer
    {
        public string LastPing { get; set; } = string.Empty;

        public async IAsyncEnumerable<Wait> Run(OutOfProcessSampleWorkflowState state = null!)
        {
            yield return WaitSignal<string>("Ping", "Ping wait");
            LastPing = "done";
        }
    }

    public class OutOfProcessSampleWorkflowState
    {
    }
}