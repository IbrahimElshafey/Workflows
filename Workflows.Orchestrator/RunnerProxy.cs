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

        public async Task<AsyncResult> StartWorkflow(string workflowName)
        {
            return await _dispatcher.DispatchAndReceiveAsync<string, AsyncResult>(workflowName);
        }
    }
}
