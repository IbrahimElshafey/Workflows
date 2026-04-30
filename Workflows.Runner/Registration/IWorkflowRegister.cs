using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Handler;
using Workflows.Handler.BaseUse;
namespace Workflows.Runner.Registration
{
    public interface IWorkflowRegister
    {
        //Task<CommandRegistrationResult> RegisterCommand<CommandData>(RegisterCommandInput registerCommandInput);
        Task<SignalRegistrationResult> RegisterSignal<SignalData>(SignalRegistrationInput signalRegistrationDto);
        Task<WorkflowRegistrationResult> RegisterWorkflow(WorkflowRegistrationInput registerWorkflowInput);
    }
}