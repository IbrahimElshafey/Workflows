using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Definition;
using Workflows.Runner.DataObjects;
using Workflows.Runner.Helpers;

namespace Workflows.Runner
{
    internal class StateMachineAdvancer
    {
        internal async Task<AdvancerResult> RunAsync(
            IAsyncEnumerable<Wait> workflow,
            WorkflowStateObject previousState,
            CancellationToken cancellationToken = default)
        {
            if (previousState == null)
            {
                previousState = new WorkflowStateObject();
            }

            var enumerator = workflow.GetAsyncEnumerator(cancellationToken);

            // 1. Extract root state machine object
            previousState.StateMachinesObjects ??= new Dictionary<string, object>();
            if (!previousState.StateMachinesObjects.TryGetValue("root", out var rootRaw))
            {
                rootRaw = new StateMachineObject();
                previousState.StateMachinesObjects["root"] = rootRaw;
            }
            var rootState = rootRaw as StateMachineObject ?? new StateMachineObject();
            rootState.StateIndex = previousState.StateIndex;
            rootState.Instance = previousState.Instance;

            // 2. Hydrate 
            StateMachineStateBridge.Hydrate(enumerator, rootState);

            // 3. Advance
            if (await enumerator.MoveNextAsync())
            {
                var nextWait = enumerator.Current;

                // 4. Dehydrate (Capture all active scopes)
                var newStateObj = StateMachineStateBridge.Dehydrate(enumerator);

                // 5. Build full WorkflowStateObject — copy all existing entries (sub-workflows etc.), update root
                var newState = new WorkflowStateObject
                {
                    StateIndex = newStateObj.StateIndex,
                    Instance = newStateObj.Instance,
                    StateMachinesObjects = new Dictionary<string, object>(previousState.StateMachinesObjects),
                    WaitStatesObjects = new Dictionary<Guid, object>(previousState.WaitStatesObjects)
                };
                newState.StateMachinesObjects["root"] = newStateObj;

                // 6. Return the clean package
                return new AdvancerResult
                {
                    Wait = nextWait,
                    State = newState
                };
            }

            return null; // Completed natively
        }
    }
}