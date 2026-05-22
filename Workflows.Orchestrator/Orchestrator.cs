using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Orchestrator;
using Workflows.Abstraction.Persistence;
using Workflows.Abstraction.Runner;

namespace Workflows.Orchestrator
{
    public class Orchestrator : IOrchestrator
    {
        private readonly IWorkflowStore _workflowStore;
        private readonly IDefinitionRepository _definitionRepository;
        private readonly IWorkflowRunner _runner;

        public Orchestrator(
            IWorkflowStore workflowStore,
            IDefinitionRepository definitionRepository,
            IWorkflowRunner runner)
        {
            _workflowStore = workflowStore ?? throw new ArgumentNullException(nameof(workflowStore));
            _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public async Task ProcessCommandResultAsync(Guid commandWaitId, object result)
        {
            var instanceId = await _workflowStore.GetInstanceByCommandWaitIdAsync(commandWaitId);
            if (instanceId == Guid.Empty)
            {
                throw new InvalidOperationException($"No workflow instance found waiting for command ID '{commandWaitId}'.");
            }

            var state = await _workflowStore.GetInstanceStateAsync(instanceId);
            if (state == null)
            {
                throw new InvalidOperationException($"Workflow instance '{instanceId}' not found.");
            }

            var request = new WorkflowExecutionRequest
            {
                TriggeringWaitId = commandWaitId,
                WorkflowState = state,
                CommandResult = result
            };

            await _runner.RunWorkflowAsync(request);
        }

        public async Task ProcessSignalAsync(string signalPath, object payload)
        {
            if (string.IsNullOrEmpty(signalPath)) throw new ArgumentNullException(nameof(signalPath));

            var instanceIds = await _workflowStore.FindInstancesWaitingForSignalAsync(signalPath);
            foreach (var instanceId in instanceIds)
            {
                var state = await _workflowStore.GetInstanceStateAsync(instanceId);
                if (state == null) continue;

                var triggeringWait = FindWaitingRecordForSignal(state.Waits, signalPath);
                if (triggeringWait == null) continue;

                var signalDto = payload as SignalDto ?? new SignalDto
                {
                    SignalIdentifier = signalPath,
                    Data = payload
                };

                var request = new WorkflowExecutionRequest
                {
                    TriggeringWaitId = triggeringWait.Id,
                    WorkflowState = state,
                    Signal = signalDto
                };

                await _runner.RunWorkflowAsync(request);
            }
        }

        public async Task<Guid> StartWorkflowAsync(string workflowName, string version, object input)
        {
            if (string.IsNullOrEmpty(workflowName)) throw new ArgumentNullException(nameof(workflowName));

            // Validate that definition exists
            var definition = await _definitionRepository.GetDefinitionAsync(workflowName, version);
            if (definition == null)
            {
                throw new InvalidOperationException($"Workflow '{workflowName}' with version '{version}' is not registered.");
            }

            var result = await _runner.StartWorkflow(workflowName, input);
            return result.Id;
        }

        private WaitInfrastructureDto FindWaitingRecordForSignal(IEnumerable<WaitInfrastructureDto> waits, string signalPath)
        {
            if (waits == null) return null;

            foreach (var w in waits)
            {
                if (w is SignalWaitDto signalWait && signalWait.SignalIdentifier == signalPath && signalWait.Status == WaitStatus.Waiting)
                {
                    return signalWait;
                }
                if (w is TimeWaitDto timeWait && timeWait.UniqueMatchId == signalPath && timeWait.Status == WaitStatus.Waiting)
                {
                    return timeWait;
                }

                if (w.ChildWaits != null && w.ChildWaits.Count > 0)
                {
                    var child = FindWaitingRecordForSignal(w.ChildWaits, signalPath);
                    if (child != null) return child;
                }
            }

            return null;
        }
    }
}
