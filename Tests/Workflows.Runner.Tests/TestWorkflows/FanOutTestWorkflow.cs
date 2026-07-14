using System.Collections.Generic;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;
using Workflows.Primitives;

namespace Workflows.Runner.Tests.TestWorkflows
{
    [Workflow("FanOutTestWorkflow", 1)]
    public sealed class FanOutTestWorkflow : WorkflowContainer
    {
        public List<string> CompletedSignals { get; set; } = new();
        public bool Completed { get; set; }

        public async IAsyncEnumerable<WaitInfrastructureDto> Run(FanOutTestWorkflowState state = null!)
        {
            yield return WaitMany(new Wait[]
            {
                WaitSignal<FanOutSignal>("FanOutSignal", "Child1"),
                WaitSignal<FanOutSignal>("FanOutSignal", "Child2"),
                WaitSignal<FanOutSignal>("FanOutSignal", "Child3")
            }, "WaitForAll");

            Completed = true;
        }
    }

    [Workflow("FanOutAnyTestWorkflow", 1)]
    public sealed class FanOutAnyTestWorkflow : WorkflowContainer
    {
        public bool Completed { get; set; }

        public async IAsyncEnumerable<WaitInfrastructureDto> Run(FanOutAnyTestWorkflowState state = null!)
        {
            yield return WaitAny(new Wait[]
            {
                WaitSignal<FanOutSignal>("FanOutSignal", "Child1"),
                WaitSignal<FanOutSignal>("FanOutSignal", "Child2"),
                WaitSignal<FanOutSignal>("FanOutSignal", "Child3")
            }, "WaitForAny");

            Completed = true;
        }
    }

    public sealed class FanOutSignal
    {
        public string Value { get; set; } = string.Empty;
    }

    public class FanOutTestWorkflowState
    {
    }

    public class FanOutAnyTestWorkflowState
    {
    }
}
