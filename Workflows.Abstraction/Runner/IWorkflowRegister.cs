using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;

namespace Workflows.Abstraction.Runner
{
    public interface IWorkflowRegister
    {
        Task<RegistrationResult> RegisterCommand<TCommand, TResult>(CommandRegistrationInput<TCommand, TResult> commandRegistrationInput);
        Task<RegistrationResult> RegisterSignal<SignalData>(SignalRegistrationInput signalRegistrationDto);
        Task<RegistrationResult> RegisterWorkflow(WorkflowRegistrationInput registerWorkflowInput);
    }
}