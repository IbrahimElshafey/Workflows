using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Numerics;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Shared.Serialization;

namespace Workflows.Runner
{
    internal class WorkflowRunner : IWorkflowRunner
    {
        private readonly IWorkflowRegistry _workflowRegistry;
        private readonly StateMachineAdvancer _stateMachineAdvancer;
        private readonly IWorkflowRunnerClient _runResultSender;
        private readonly ICommandHandlerFactory _commandHandlerFactory;
        private readonly IServiceProvider _serviceProvider;

        public WorkflowRunner(
            Mapper mapper,
            IWorkflowRegistry workflowRegistry,
            StateMachineAdvancer stateMachineAdvancer,
            IWorkflowRunnerClient runResultSender,
            ICommandHandlerFactory commandHandlerFactory,
            IServiceProvider serviceProvider)
        {
            _workflowRegistry = workflowRegistry;
            _stateMachineAdvancer = stateMachineAdvancer;
            _runResultSender = runResultSender;
            _commandHandlerFactory = commandHandlerFactory;
            _serviceProvider = serviceProvider;
        }
        public Task<AsyncResult> RunWorkflowAsync(WorkflowExecutionRequest runContext)
        {
            //# Orchestrator
            //* Wait Activation Time = Siganl caused it sent time
            //* NewWaitsIds: To be indexed in SQL
            //* ConsumedWaitsIds: To be removed from SQL
            //* 
            //
            //* Send run started signal and enque request in channel
            //* Check signal match, Signal match needs
            //* We need to deserialize(workflow instance class,active closure, signal)
            //* Deserialize Match expression if needed and compile it to run with(instance, closure, signal)
            //* Check if we need to proceed(group completed, sub workflow ended,...)
            //* Deserialize all remaining in variables dictionary
            //* Execute after match action if exist
            //* Check request to cancel tokens list and cancel related waits
            //* Proceed to execution and get next new wait
            //* If command wait or command group (execute in smart way)
            //* If long command do same as signal wait
            //* If signal wait Convert Wait to DTO
            //* Update state DTO and return NewWaitsIds,ConsumedWaitsIds
            throw new NotImplementedException();
        }
    }
}
