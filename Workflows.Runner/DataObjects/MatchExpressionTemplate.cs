using System;
using System.Threading.Tasks;
namespace Workflows.Runner.DataObjects
{
    internal class MatchExpressionTemplate
    {
        // (TSignalData, TInstance, TState) => bool
        public Func<object, object, object, bool> CompiledMatchDelegate { get; set; }

        // (workflowInstance, signalData, state) => void
        public Func<object, object, object> AfterMatchAction { get; set; }

        // (workflowInstance, state) => ValueTask
        public Func<object, object, ValueTask> CancelAction { get; set; }

        // (workflowInstance, state) => string[]
        public Func<object, object, string[]> InstanceExactMatchFunc { get; set; }
    }
}