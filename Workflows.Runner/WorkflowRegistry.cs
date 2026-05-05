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
        private readonly Dictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine)> _workflows = new();
        private readonly Dictionary<string, Type> _signals = new();
        private readonly Dictionary<string, (Type CommandPayloadType, Type CommandResultType)> _commands = new();

        public WorkflowBuilder()
        {
            
        }

        public Dictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine)> Workflows => _workflows;

        public Dictionary<string, Type> SignalTypes => _signals;

        public Dictionary<string, (Type CommandPayloadType, Type CommandResultType)> CommandTypes => _commands;

        public Task<RegistrationSyncResult> CommitAsync()
        {
            return Task.FromResult(new RegistrationSyncResult { Success = true });
        }

        public IWorkflowBuilder RegisterCommand<TCommand, TResult>(string commandIdentifier)
        {
            _commands[commandIdentifier] = (typeof(TCommand), typeof(TResult));
            registrationPackage.Commands.Add(new CommandDefinition
            {
                CommandName = commandIdentifier,
                RequestTypeName = typeof(TCommand).AssemblyQualifiedName,
                ResultTypeName = typeof(TResult).AssemblyQualifiedName
            });
            return this;
        }

        public IWorkflowBuilder RegisterRunner(string runnerName)
        {
            registrationPackage.RunnerName = runnerName;
            return this;
        }

        public IWorkflowBuilder RegisterSignal<TSignal>(string signalIdentifier)
        {
            _signals[signalIdentifier] = typeof(TSignal);
            registrationPackage.Signals.Add(new SignalDefinition
            {
                SignalIdentifier = signalIdentifier
            });
            return this;
        }

        public IWorkflowBuilder RegisterWorkflow<WorkflowClass>(IAsyncEnumerable<Wait> workflow, string name, string version = "1.0.0") where WorkflowClass : WorkflowContainer
        {
            _workflows[name] = (typeof(WorkflowClass), workflow.GetType());
            registrationPackage.Workflows.Add(new WorkflowDefinition
            {
                WorkflowName = name,
                Version = version,
                WorkflowTypeName = typeof(WorkflowClass).AssemblyQualifiedName,
                RegisteredAt = DateTime.UtcNow
            });
            return this;
        }

        public IWorkflowBuilder SettingsSection(string settingsSection)
        {
            return this;
        }
    }
}
