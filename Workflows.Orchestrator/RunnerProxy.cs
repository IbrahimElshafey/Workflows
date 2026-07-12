using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;
using Workflows.Communication.Abstraction;

namespace Workflows.Orchestrator
{
    public class RunnerProxy : IWorkflowRunner
    {
        private readonly IMessageDispatcher _dispatcher;

        public RunnerProxy(IMessageDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public async Task<AsyncResult> RunWorkflowAsync(WorkflowExecutionRequest request)
        {
            return await _dispatcher.DispatchAndReceiveAsync<WorkflowExecutionRequest, AsyncResult>(request);
        }

        public async Task<AsyncResult> StartWorkflow(string workflowName, object input = null)
        {
            return await StartWorkflow(workflowName, version: 0, input);
        }

        public async Task<AsyncResult> StartWorkflow(string workflowName, int version, object input = null)
        {
            var request = new StartWorkflowRequest { WorkflowName = workflowName, Version = version, Input = input };
            return await _dispatcher.DispatchAndReceiveAsync<StartWorkflowRequest, AsyncResult>(request);
        }
    }
}
