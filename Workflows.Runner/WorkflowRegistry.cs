using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Schema.Generation;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Registration;
using Workflows.Abstraction.Runner;
using Workflows.Definition;
using Workflows.Definition.Registration;
using Workflows.Primitives;

namespace Workflows.Runner
{
    /// <summary>
    /// Is resposibe to create registration object that will be sent to Orchestartor
    /// and to add workflows types to DI container
    /// </summary>
    internal class WorkflowBuilder : IWorkflowBuilder, IWorkflowRegistry
    {
        private readonly BulkRegistrationPackage registrationPackage = new BulkRegistrationPackage();
        // Key=> Workflow Name, Value => Tuple of (WorkflowContainer Type, StateMachine Type)
        private readonly Dictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine)> _workflows = new();
        // Key => Signal Identifier, Value => Signal Payload Type
        private readonly Dictionary<string, Type> _signals = new();
        // Key => Command Identifier, Value => Tuple of (Command Payload Type, Command Result Type)
        private readonly Dictionary<string, (Type CommandPayloadType, Type CommandResultType)> _commands = new();
        private readonly JSchemaGenerator _schemaGenerator;

        public WorkflowBuilder(JSchemaGenerator schemaGenerator)
        {
            _schemaGenerator = schemaGenerator;
        }

        public Dictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine)> Workflows => _workflows;

        public Dictionary<string, Type> SignalTypes => _signals;

        public Dictionary<string, (Type CommandPayloadType, Type CommandResultType)> CommandTypes => _commands;

        public Task<RegistrationSyncResult> CommitAsync()
        {
            return Task.FromResult(new RegistrationSyncResult { Success = true });
        }

        public IWorkflowBuilder RegisterCommand<TCommand, TResult>(
            string commandIdentifier,
            TimeSpan timeout = default,
            CommandExecutionMode mode = CommandExecutionMode.Direct)
        {
            _commands[commandIdentifier] = (typeof(TCommand), typeof(TResult));
            registrationPackage.Commands.Add(new CommandDefinition
            {
                CommandName = commandIdentifier,
                RequestTypeName = typeof(TCommand).AssemblyQualifiedName,
                RequestSchema = _schemaGenerator.Generate(typeof(TCommand)).ToString(),
                ResultTypeName = typeof(TResult).AssemblyQualifiedName,
                ResultSchema = _schemaGenerator.Generate(typeof(TResult)).ToString(),
                DefaultTimeout = timeout,
                ExecutionMode = mode
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
            JSchema schema = _schemaGenerator.Generate(typeof(TSignal));
            registrationPackage.Signals.Add(new SignalDefinition
            {
                SignalIdentifier = signalIdentifier,
                PayloadTypeName = typeof(TSignal).AssemblyQualifiedName,
                PayloadSchema = schema.ToString()
            });
            return this;
        }

        public IWorkflowBuilder RegisterWorkflow<WorkflowClass>(string name, string version) where WorkflowClass : WorkflowContainer
        {
            Type workflowType = typeof(WorkflowClass);

            // 1. Check that the workflow class is sealed
            if (!workflowType.IsSealed)
            {
                throw new InvalidOperationException($"Registration failed for '{name}'. The workflow class '{workflowType.Name}' must be sealed.");
            }

            // 2. Locate the ExecuteWorkflowAsync method to get its compiler-generated state machine
            var methodInfo = workflowType.GetMethod(
                nameof(WorkflowContainer.ExecuteWorkflowAsync),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (methodInfo == null)
            {
                throw new InvalidOperationException($"Could not find 'ExecuteWorkflowAsync' method on '{workflowType.Name}'.");
            }

            // 3. Extract the generated state machine type
            var stateMachineAttribute = methodInfo.GetCustomAttribute<AsyncStateMachineAttribute>();
            Type stateMachineType = stateMachineAttribute?.StateMachineType;

            if (stateMachineType == null)
            {
                throw new InvalidOperationException($"Method 'ExecuteWorkflowAsync' on '{workflowType.Name}' must be an 'async' method.");
            }

            // 4. Correctly assign the container type AND the extracted state machine type
            _workflows[name] = (workflowType, stateMachineType);

            registrationPackage.Workflows.Add(new WorkflowDefinition
            {
                WorkflowName = name,
                Version = version,
                WorkflowTypeName = workflowType.AssemblyQualifiedName,
                RegisteredAt = DateTime.UtcNow,
                WorkflowTypeSchema = _schemaGenerator.Generate(workflowType).ToString()
            });

            return this;
        }

        public IWorkflowBuilder SettingsSection(string settingsSection)
        {
            return this;
        }
    }
}
