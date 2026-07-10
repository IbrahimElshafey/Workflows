using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline.Serializers
{
    /// <summary>
    /// Base class for wait processors that serialize yielded waits after state machine advancement.
    /// </summary>
    internal abstract class WaitSerializer
    {
        /// <summary>
        /// Serializes the yielded wait and returns whether the execution loop should be cached or suspended.
        /// </summary>
        public abstract Task<bool> Serialize(Wait yieldedWait, WorkflowExecutionContext context);

        /// <summary>
        /// Helper to save wait explicit state to machine state object.
        /// </summary>
        protected void SaveWaitStatesToMachineState(Wait wait, WorkflowStateObject stateObject)
        {
            Console.WriteLine($"[SERIALIZER DEBUG] SaveWaitStatesToMachineState: StateKey = {wait.StateKey}, Id = {wait.Id}, ExplicitState = {wait.ExplicitState}");
            if (wait.ExplicitState == null) return;

            stateObject.Locals ??= new Dictionary<string, object>();

            if (wait.StateKey != Guid.Empty && !stateObject.Locals.ContainsKey(wait.StateKey.ToString()))
            {
                stateObject.Locals[wait.StateKey.ToString()] = wait.ExplicitState;
            }

            if (!stateObject.Locals.ContainsKey(wait.Id.ToString()))
            {
                stateObject.Locals[wait.Id.ToString()] = wait.ExplicitState;
            }
        }
    }
}
