using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Orchestration;
using Workflows.Abstraction.Runner;
using Workflows.Orchestrator.Data.EF;
using Workflows.Primitives;
using Newtonsoft.Json;

namespace Workflows.Orchestrator
{
    public class Orchestrator : IOrchestrator
    {
        private readonly IWorkflowStore _workflowStore;
        private readonly IWorkflowRunner _workflowRunner;
        private readonly IExternalScheduler _scheduler;
        private readonly ICommandHandlerFactory _commandHandlerFactory;

        public Orchestrator(
            IWorkflowStore workflowStore,
            IWorkflowRunner workflowRunner,
            IExternalScheduler scheduler,
            ICommandHandlerFactory commandHandlerFactory)
        {
            _workflowStore = workflowStore;
            _workflowRunner = workflowRunner;
            _scheduler = scheduler;
            _commandHandlerFactory = commandHandlerFactory;
        }

        public async Task ProcessSignalAsync<TPayload>(string signalIdentifier, TPayload payload)
        {
            var signalDataJson = JsonConvert.SerializeObject(payload);
            var matches = await _workflowStore.FindMatchingWaitsAsync(signalIdentifier, signalDataJson);

            foreach (var match in matches)
            {
                var stateDto = await _workflowStore.LoadWorkflowStateAsync(match.WorkflowId);
                if (stateDto == null) continue;

                var signalDto = new SignalDto
                {
                    Data = payload,
                    SignalIdentifier = signalIdentifier
                };

                var runRequest = new WorkflowExecutionRequest
                {
                    WorkflowState = stateDto,
                    TriggeringWaitId = match.WaitId,
                    Signal = signalDto
                };

                var runResult = await _workflowRunner.RunWorkflowAsync(runRequest);
                await ProcessRunResultAsync(match.WorkflowId, runRequest.WorkflowState);
            }
        }

        private async Task ProcessRunResultAsync(Guid workflowId, WorkflowStateDto state)
        {
            if (state == null) return;

            await _workflowStore.SaveWorkflowStateAsync(workflowId, state);

            foreach (var wait in state.Waits)
            {
                if (wait is TimeWaitDto timeWait && timeWait.Status == WaitStatus.Waiting)
                {
                    // No direct mapping, but conceptually it triggers scheduler logic
                }
            }
        }
    }
}
