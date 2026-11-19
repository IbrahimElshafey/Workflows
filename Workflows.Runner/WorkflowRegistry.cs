using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Schema.Generation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs.Registration;
using Workflows.Abstraction.DTOs.Waits;
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
        // Key => (Workflow Name, Version), Value => (WorkflowContainer Type, StateMachine Type, StateType Type, StartMethod Name)
        private readonly Dictionary<(string Name, int Version), (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod)> _workflows = new();
        // Key => Signal Identifier, Value => Signal Payload Type
        private readonly Dictionary<string, Type> _signals = new();
        // Key => Command Identifier, Value => Tuple of (Command Payload Type, Command Result Type)
        private readonly Dictionary<string, (Type CommandPayloadType, Type CommandResultType)> _commands = new();
        private readonly JSchemaGenerator _schemaGenerator;

        public WorkflowBuilder(JSchemaGenerator schemaGenerator)
        {
            _schemaGenerator = schemaGenerator;
        }

        /// <summary>
        /// Backward-compatible property returning the latest version of each workflow by name.
        /// </summary>
        public Dictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod)> Workflows
        {
            get
            {
                var latest = new Dictionary<string, (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod)>();
                foreach (var kvp in _workflows)
                {
                    var name = kvp.Key.Name;
                    var version = kvp.Key.Version;
                    if (!latest.TryGetValue(name, out var existing) || version > GetVersionFromTuple(existing))
                    {
                        latest[name] = kvp.Value;
                    }
                }
                return latest;
            }
        }

        public Dictionary<string, Type> SignalTypes => _signals;

        public Dictionary<string, (Type CommandPayloadType, Type CommandResultType)> CommandTypes => _commands;

        /// <inheritdoc/>
        public bool TryGetWorkflow(string name, int version, out (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod) tuple)
        {
            return _workflows.TryGetValue((name, version), out tuple);
        }

        /// <inheritdoc/>
        public bool TryGetLatestWorkflow(string name, out (Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod) tuple)
        {
            tuple = default;
            var bestVersion = -1;
            foreach (var kvp in _workflows)
            {
                if (kvp.Key.Name == name && kvp.Key.Version > bestVersion)
                {
                    bestVersion = kvp.Key.Version;
                    tuple = kvp.Value;
                }
            }
            return bestVersion >= 0;
        }

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

            bool isDeferred = typeof(IDeferredCommand<TCommand, TResult>).IsAssignableFrom(typeof(TCommand));
            bool isImmediate = typeof(IImmediateCommand<TCommand, TResult>).IsAssignableFrom(typeof(TCommand));
            var resolvedMode = isDeferred ? CommandExecutionMode.Deferred : (isImmediate ? CommandExecutionMode.Immediate : mode);

            registrationPackage.Commands.Add(new CommandDefinition
            {
                CommandName = commandIdentifier,
                PayloadTypeName = typeof(TCommand).AssemblyQualifiedName,
                PayloadSchema = _schemaGenerator.Generate(typeof(TCommand)).ToString(),
                ResultTypeName = typeof(TResult).AssemblyQualifiedName,
                ResultSchema = _schemaGenerator.Generate(typeof(TResult)).ToString(),
                DefaultTimeout = timeout,
                ExecutionMode = resolvedMode,
                HasMatchingFunction = isDeferred
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
            return RegisterWorkflowInternal<WorkflowClass>(attribute.Name, attribute.Version, null);
        }

        public IWorkflowBuilder RegisterWorkflow<WorkflowClass>(string startMethod) where WorkflowClass : WorkflowContainer
        {
            var attribute = typeof(WorkflowClass).GetCustomAttribute<WorkflowAttribute>();
            if (attribute == null)
            {
                throw new InvalidOperationException($"The workflow class '{typeof(WorkflowClass).Name}' is not decorated with [WorkflowAttribute].");
            }
            return RegisterWorkflowInternal<WorkflowClass>(attribute.Name, attribute.Version, startMethod);
        }

        public IWorkflowBuilder RegisterWorkflow<WorkflowClass>(string name, int version) where WorkflowClass : WorkflowContainer
        {
            return RegisterWorkflowInternal<WorkflowClass>(name, version, null);
        }

        public IWorkflowBuilder RegisterWorkflow<WorkflowClass>(string name, int version, string startMethod) where WorkflowClass : WorkflowContainer
        {
            return RegisterWorkflowInternal<WorkflowClass>(name, version, startMethod);
        }

        private IWorkflowBuilder RegisterWorkflowInternal<WorkflowClass>(string name, int version, string? customStartMethod) where WorkflowClass : WorkflowContainer
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

            var startMethodName = !string.IsNullOrEmpty(customStartMethod) 
                ? customStartMethod 
                : (string.IsNullOrEmpty(attribute.StartMethod) ? "Run" : attribute.StartMethod);

            var methodInfo = workflowType.GetMethod(
                startMethodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (methodInfo == null)
            {
                throw new InvalidOperationException($"Could not find '{startMethodName}' method on '{workflowType.Name}'.");
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
                throw new InvalidOperationException($"Method '{startMethodName}' on '{workflowType.Name}' must be an 'async' method.");
            }

            // 4. Resolve State Type
            Type stateType = typeof(object);
            if (attribute.StateType != null)
            {
                stateType = attribute.StateType;
            }
            else if (methodInfo.GetParameters().Length == 1)
            {
                stateType = methodInfo.GetParameters()[0].ParameterType;
            }

            // 5. Store by (name, version) composite key — SxS version routing
            var tuple = (workflowType, stateMachineType, stateType, startMethodName);
            _workflows[(name, version)] = tuple;
            global::Workflows.Definition.Registration.WorkflowDefinitionRegistry.AddOrUpdate(name, version, tuple);

            // Validate sub-workflows (visibility and attribute)
            ValidateWorkflowSubWorkflows(workflowType, startMethodName);

            // Validate wait names
            ValidateWorkflowWaits(workflowType, startMethodName, stateType);

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

        private void ValidateWorkflowSubWorkflows(Type workflowType, string startMethodName)
        {
            var methods = workflowType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                if (method.Name == startMethodName) continue;

                var hasSubWorkflowAttr = method.GetCustomAttribute<SubWorkflowAttribute>() != null;
                var returnsWaitAsyncEnum = typeof(IAsyncEnumerable<Wait>).IsAssignableFrom(method.ReturnType);

                var returnsWaitDtoAsyncEnum = typeof(IAsyncEnumerable<WaitInfrastructureDto>).IsAssignableFrom(method.ReturnType);

                if (hasSubWorkflowAttr || returnsWaitAsyncEnum || returnsWaitDtoAsyncEnum)
                {
                    if (!hasSubWorkflowAttr)
                    {
                        throw new InvalidOperationException($"Method '{method.Name}' in workflow '{workflowType.Name}' returns IAsyncEnumerable<WaitInfrastructureDto> but is missing [SubWorkflow] attribute.");
                    }
                    if (!method.IsPrivate)
                    {
                        throw new InvalidOperationException($"Sub-workflow method '{method.Name}' in workflow '{workflowType.Name}' must be private to prevent usage outside of its parent workflow container.");
                    }
                }
            }
        }

        private void ValidateWorkflowWaits(Type workflowType, string startMethodName, Type stateType)
        {
            try
            {
                var container = (WorkflowContainer)Activator.CreateInstance(workflowType);
                object state = Activator.CreateInstance(stateType);
                var runMethod = workflowType.GetMethod(startMethodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (runMethod != null)
                    {
                        // Ensure the Wait -> DTO converter is configured for validation-time invocation
                        WaitDtoConversion.Converter = wait =>
                        {
                            // During validation we don't have a full Mapper, so create a minimal DTO copy
                            // for wait name uniqueness checks. Real execution uses the runner's Mapper.
                            return new PlaceholderWaitDto
                            {
                                WaitName = wait.WaitName,
                                WaitType = wait.WaitType,
                                CallerName = wait.CallerName,
                                InCodeLine = wait.InCodeLine,
                                Created = wait.Created
                            };
                        };

                        IAsyncEnumerable<WaitInfrastructureDto> stream;
                        if (runMethod.GetParameters().Length == 1)
                        {
                            stream = CastOrConvertStream(runMethod.Invoke(container, new[] { state }));
                        }
                        else
                        {
                            stream = CastOrConvertStream(runMethod.Invoke(container, null));
                        }
                        var enumerator = stream.GetAsyncEnumerator();
                    var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    var moveNextTask = Task.Run(async () =>
                    {
                        var waits = new List<WaitInfrastructureDto>();
                        while (await enumerator.MoveNextAsync())
                        {
                            waits.Add(enumerator.Current);
                        }
                        return waits;
                    });

                    if (moveNextTask.Wait(5000))
                    {
                        var waits = moveNextTask.Result;
                        foreach (var waitDto in waits)
                        {
                            if (waitDto != null)
                            {
                                ValidateWaitRecursive(waitDto, seenNames, workflowType.Name);
                            }
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

        private void ValidateWaitRecursive(WaitInfrastructureDto waitDto, HashSet<string> seenNames, string workflowName)
        {
            if (waitDto == null) return;

            var name = waitDto.WaitName;
            if (waitDto is CompensationWaitDto compWait)
            {
                name = compWait.Token;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException($"Wait name is mandatory. A wait of type '{waitDto.GetType().Name}' in workflow '{workflowName}' is defined without a name.");
            }

            if (!seenNames.Add(name))
            {
                throw new InvalidOperationException($"Wait name '{name}' is duplicate in workflow '{workflowName}'. Wait names must be unique within a workflow.");
            }

            if (waitDto.ChildWaits != null)
            {
                foreach (var child in waitDto.ChildWaits)
                {
                    ValidateWaitRecursive(child, seenNames, workflowName);
                }
            }
        }

        public IWorkflowBuilder SettingsSection(string settingsSection)
        {
            return this;
        }

        private static int GetVersionFromTuple((Type WorkflowContainer, Type WorkflowStateMachine, Type StateType, string StartMethod) tuple)
        {
            var attr = tuple.WorkflowContainer.GetCustomAttribute<WorkflowAttribute>();
            return attr?.Version ?? 1;
        }

        private static async IAsyncEnumerable<WaitInfrastructureDto> CastOrConvertStream(object rawStream)
        {
            if (rawStream is IAsyncEnumerable<WaitInfrastructureDto> dtoStream)
            {
                await foreach (var item in dtoStream)
                {
                    yield return item;
                }
            }
            else if (rawStream is IAsyncEnumerable<Wait> waitStream)
            {
                await foreach (var item in waitStream)
                {
                    yield return item;
                }
            }
            else
            {
                throw new InvalidOperationException($"Invalid workflow stream type: {rawStream?.GetType().FullName}");
            }
        }
    }
}