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
        // Key=> Workflow Name, Value => Tuple of (WorkflowContainer Type, StateMachine Type, StateType Type)
        private readonly Dictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType)> _workflows = new();
        // Key => Signal Identifier, Value => Signal Payload Type
        private readonly Dictionary<string, Type> _signals = new();
        // Key => Command Identifier, Value => Tuple of (Command Payload Type, Command Result Type)
        private readonly Dictionary<string, (Type CommandPayloadType, Type CommandResultType)> _commands = new();
        private readonly JSchemaGenerator _schemaGenerator;

        public WorkflowBuilder(JSchemaGenerator schemaGenerator)
        {
            _schemaGenerator = schemaGenerator;
        }

        public Dictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType)> Workflows => _workflows;

        public Dictionary<string, Type> SignalTypes => _signals;

        public Dictionary<string, (Type CommandPayloadType, Type CommandResultType)> CommandTypes => _commands;

        public Task<RegistrationSyncResult> CommitAsync()
        {
            return Task.FromResult(new RegistrationSyncResult { Success = true });
        }

        public IWorkflowBuilder RegisterCommand<TCommand, TResult>(
            string commandIdentifier,
            TimeSpan timeout = default,
            CommandExecutionMode mode = CommandExecutionMode.Immediate)
        {
            _commands[commandIdentifier] = (typeof(TCommand), typeof(TResult));
            registrationPackage.Commands.Add(new CommandDefinition
            {
                CommandName = commandIdentifier,
                PayloadTypeName = typeof(TCommand).AssemblyQualifiedName,
                PayloadSchema = _schemaGenerator.Generate(typeof(TCommand)).ToString(),
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

        public IWorkflowBuilder RegisterWorkflow<WorkflowClass>() where WorkflowClass : WorkflowContainer
        {
            var attribute = typeof(WorkflowClass).GetCustomAttribute<WorkflowAttribute>();
            if (attribute == null)
            {
                throw new InvalidOperationException($"The workflow class '{typeof(WorkflowClass).Name}' is not decorated with [WorkflowAttribute].");
            }
            return RegisterWorkflow<WorkflowClass>(attribute.Name, attribute.Version);
        }

        public IWorkflowBuilder RegisterWorkflow<WorkflowClass>(string name, int version) where WorkflowClass : WorkflowContainer
        {
            Type workflowType = typeof(WorkflowClass);

            var attribute = workflowType.GetCustomAttribute<WorkflowAttribute>();
            if (attribute == null)
            {
                throw new InvalidOperationException($"The workflow class '{workflowType.Name}' is not decorated with [WorkflowAttribute].");
            }

            // 1. Check that the workflow class is sealed
            if (!workflowType.IsSealed)
            {
                throw new InvalidOperationException($"Registration failed for '{name}'. The workflow class '{workflowType.Name}' must be sealed.");
            }

            var methodInfo = workflowType.GetMethod(
                nameof(WorkflowContainer.Run),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);

            if (methodInfo == null)
            {
                throw new InvalidOperationException($"Could not find 'ExecuteWorkflowAsync' method on '{workflowType.Name}'.");
            }

            // 3. Extract the generated state machine type
            Type? stateMachineType = null;
            var stateMachineAttribute = methodInfo.GetCustomAttribute<AsyncStateMachineAttribute>();
            if (stateMachineAttribute != null)
            {
                stateMachineType = stateMachineAttribute.StateMachineType;
            }
            else
            {
                var asyncIteratorAttribute = methodInfo.GetCustomAttribute<System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute>();
                if (asyncIteratorAttribute != null)
                {
                    stateMachineType = asyncIteratorAttribute.StateMachineType;
                }
            }

            if (stateMachineType == null)
            {
                throw new InvalidOperationException($"Method 'ExecuteWorkflowAsync' on '{workflowType.Name}' must be an 'async' method.");
            }

            // 4. Resolve State Type
            Type stateType = typeof(DefaultWorkflowState);
            var baseType = workflowType.BaseType;
            while (baseType != null)
            {
                if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(WorkflowContainer<>))
                {
                    stateType = baseType.GetGenericArguments()[0];
                    break;
                }
                baseType = baseType.BaseType;
            }

            // 5. Correctly assign the container type, state machine type, and state type
            _workflows[name] = (workflowType, stateMachineType, stateType);
            global::Workflows.Definition.Registration.WorkflowDefinitionRegistry.Workflows[name] = (workflowType, stateMachineType, stateType);

            // Validate sub-workflows (visibility and attribute)
            ValidateWorkflowSubWorkflows(workflowType);

            // Validate wait names
            ValidateWorkflowWaits(workflowType);

            registrationPackage.Workflows.Add(new WorkflowDefinition
            {
                WorkflowName = name,
                Version = version,
                WorkflowTypeName = workflowType.AssemblyQualifiedName,
                StateTypeName = stateType.AssemblyQualifiedName,
                StateTypeSchema = _schemaGenerator.Generate(stateType).ToString(),
                RegisteredAt = DateTime.UtcNow,
                WorkflowTypeSchema = _schemaGenerator.Generate(workflowType).ToString()
            });

            return this;
        }

        private void ValidateWorkflowSubWorkflows(Type workflowType)
        {
            var methods = workflowType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                if (method.Name == nameof(WorkflowContainer.Run)) continue;

                var hasSubWorkflowAttr = method.GetCustomAttribute<SubWorkflowAttribute>() != null;
                var returnsWaitAsyncEnum = typeof(IAsyncEnumerable<Wait>).IsAssignableFrom(method.ReturnType);

                if (hasSubWorkflowAttr || returnsWaitAsyncEnum)
                {
                    if (!hasSubWorkflowAttr)
                    {
                        throw new InvalidOperationException($"Method '{method.Name}' in workflow '{workflowType.Name}' returns IAsyncEnumerable<Wait> but is missing [SubWorkflow] attribute.");
                    }
                    if (!method.IsPrivate)
                    {
                        throw new InvalidOperationException($"Sub-workflow method '{method.Name}' in workflow '{workflowType.Name}' must be private to prevent usage outside of its parent workflow container.");
                    }
                }
            }
        }

        private void ValidateWorkflowWaits(Type workflowType)
        {
            try
            {
                var container = (WorkflowContainer)Activator.CreateInstance(workflowType);
                var enumerator = container.Run().GetAsyncEnumerator();
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var moveNextTask = Task.Run(async () =>
                {
                    var waits = new List<Wait>();
                    while (await enumerator.MoveNextAsync())
                    {
                        waits.Add(enumerator.Current);
                    }
                    return waits;
                });

                if (moveNextTask.Wait(200))
                {
                    var waits = moveNextTask.Result;
                    foreach (var wait in waits)
                    {
                        if (wait != null)
                        {
                            ValidateWaitRecursive(wait, seenNames, workflowType.Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var inner = ex;
                while (inner is TargetInvocationException || inner is AggregateException)
                {
                    inner = inner.InnerException;
                }

                if (inner is InvalidOperationException && inner.Message.Contains("Wait name"))
                {
                    throw inner;
                }
            }
        }

        private void ValidateWaitRecursive(Wait wait, HashSet<string> seenNames, string workflowName)
        {
            if (wait == null) return;

            var name = wait.WaitName;
            if (wait is CompensationWait compWait)
            {
                name = compWait.Token;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException($"Wait name is mandatory. A wait of type '{wait.GetType().Name}' in workflow '{workflowName}' is defined without a name.");
            }

            if (!seenNames.Add(name))
            {
                throw new InvalidOperationException($"Wait name '{name}' is duplicate in workflow '{workflowName}'. Wait names must be unique within a workflow.");
            }

            if (wait.ChildWaits != null)
            {
                foreach (var child in wait.ChildWaits)
                {
                    ValidateWaitRecursive(child, seenNames, workflowName);
                }
            }
        }

        public IWorkflowBuilder SettingsSection(string settingsSection)
        {
            return this;
        }
    }
}
