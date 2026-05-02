using System;
using Workflows.Abstraction.Communication;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;

namespace Workflows.Orchestrator
{
    public class RunnerProxy : IWorkflowRunner
    {
        private readonly IMessageDispatcher _dispatcher;

        public RunnerProxy(IMessageDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public async Task<WorkflowRunId> RunWorkflowAsync(WorkflowExecutionRequest request)
        {
            // Fire and Forget. 
            // The Dispatcher handles evaluating the type, finding the RabbitMQ transport, 
            // and routing it to "orders-queue". The Orchestrator thread is immediately freed.
            return await _dispatcher.DispatchAndReceiveAsync<WorkflowExecutionRequest, WorkflowRunId>(request);
        }
    }
}
