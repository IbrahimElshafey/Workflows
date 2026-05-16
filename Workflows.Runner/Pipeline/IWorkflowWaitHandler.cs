using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Definition;

namespace Workflows.Runner.Pipeline
{
    /// <summary>
    /// Base class for wait handlers that process yielded waits after state machine advancement.
    /// Prepares out-bound footprints (indexes, schedules, command dispatches).
    /// Returns true if the execution loop should continue immediately (active waits),
    /// false if the workflow should suspend (passive waits).
    /// </summary>
    internal abstract class WorkflowWaitHandler
    {
        /// <summary>
        /// Handles the yielded wait and returns whether the execution loop should continue.
        /// </summary>
        public abstract Task<bool> HandleAsync(Wait yieldedWait, WorkflowExecutionContext context);

        /// <summary>
        /// Helper to save wait explicit state to machine state object.
        /// </summary>
        protected void SaveWaitStatesToMachineState(Wait wait, WorkflowStateObject stateObject)
        {
            if (wait.ExplicitState == null) return;

            stateObject.WaitStatesObjects ??= new Dictionary<Guid, object>();

            if (!stateObject.WaitStatesObjects.ContainsKey(wait.Id))
            {
                stateObject.WaitStatesObjects[wait.Id] = wait.ExplicitState;
            }
        }
    }
}
