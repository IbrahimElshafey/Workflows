using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Registration;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Shared.DataObject;

namespace Workflows.Runner
{
    /// <summary>
    /// Is resposibe to create registration object that will be sent to Orchestartor
    /// and to add workflows types to DI container
    /// </summary>
    internal class WorkflowBuilder : IWorkflowBuilder, IWorkflowRegistry
    {
        private readonly BulkRegistrationPackage registrationPackage = new BulkRegistrationPackage();
        public WorkflowBuilder()
        {
            
        }

        public Dictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine)> Workflows => throw new NotImplementedException();

        public Dictionary<string, Type> SignalTypes => throw new NotImplementedException();

        public Dictionary<string, (Type CommandPayloadType, Type CommandResultType)> CommandTypes => throw new NotImplementedException();

        public Task<RegistrationSyncResult> CommitAsync()
        {
            throw new NotImplementedException();
        }

        public IWorkflowBuilder RegisterCommand<TCommand, TResult>(string commandIdentifier)
        {
            throw new NotImplementedException();
        }

        public IWorkflowBuilder RegisterRunner(string runnerName)
        {
            throw new NotImplementedException();
        }

        public IWorkflowBuilder RegisterSignal<TSignal>(string signalIdentifier)
        {
            throw new NotImplementedException();
        }

        public IWorkflowBuilder RegisterWorkflow<WorkflowClass>(IAsyncEnumerable<Wait> workflow, string name, string version = "1.0.0") where WorkflowClass : WorkflowContainer
        {
            throw new NotImplementedException();
        }

        public IWorkflowBuilder SettingsSection(string settingsSection)
        {
            throw new NotImplementedException();
        }
    }
}
