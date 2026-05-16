using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Definition;
using Workflows.Primitives;
using Workflows.Runner.Helpers;

namespace Workflows.Runner
{
    internal sealed class Mapper
    {
        private readonly IExpressionSerializer _expressionSerializer;
        private readonly IObjectSerializer _objectSerializer;
        private readonly IDelegateSerializer _delegateSerializer;
        private readonly IClosureContextResolver _closureContextResolver;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object[], object>> _constructorCache = new();

        public Mapper(
            IExpressionSerializer expressionSerializer,
            IObjectSerializer objectSerializer,
            IDelegateSerializer delegateSerializer,
            IClosureContextResolver closureContextResolver)
        {
            _expressionSerializer = expressionSerializer ?? throw new ArgumentNullException(nameof(expressionSerializer));
            _objectSerializer = objectSerializer ?? throw new ArgumentNullException(nameof(objectSerializer));
            _delegateSerializer = delegateSerializer ?? throw new ArgumentNullException(nameof(delegateSerializer));
            _closureContextResolver = closureContextResolver ?? throw new ArgumentNullException(nameof(closureContextResolver));
        }

        #region To DTO

        public SubWorkflowWaitDto MapToDto(SubWorkflowWait waitsGroup)
        {
            if (waitsGroup == null) throw new ArgumentNullException(nameof(waitsGroup));

            var dto = new SubWorkflowWaitDto
            {
                CancelTokens = waitsGroup.CancelTokens
            };

            CopyBase(waitsGroup, dto);
            dto.CancelClosureKey = _closureContextResolver.CacheClosureIfAny(waitsGroup.CancelAction?.Target, waitsGroup);

            if (waitsGroup.FirstWait != null)
            {
                dto.ChildWaits = new List<WaitInfrastructureDto> { MapToDto(waitsGroup.FirstWait) };
            }
            else if (waitsGroup.ChildWaits?.Count > 0)
            {
                dto.ChildWaits = waitsGroup.ChildWaits.Select(MapToDto).ToList();
            }

            return dto;
        }

        public TimeWaitDto MapToDto(TimeWait waitsGroup)
        {
            if (waitsGroup == null) throw new ArgumentNullException(nameof(waitsGroup));

            var dto = new TimeWaitDto
            {
                TimeToWait = waitsGroup.TimeToWait,
                UniqueMatchId = waitsGroup.UniqueMatchId,
                CancelAction = _delegateSerializer.Serialize(waitsGroup.CancelAction),
                CancelTokens = waitsGroup.CancelTokens
            };

            CopyBase(waitsGroup, dto);
            dto.CancelClosureKey = _closureContextResolver.CacheClosureIfAny(waitsGroup.CancelAction?.Target, waitsGroup);

            return dto;
        }

        public GroupWaitDto MapToDto(GroupWait waitsGroup)
        {
            if (waitsGroup == null) throw new ArgumentNullException(nameof(waitsGroup));

            var dto = new GroupWaitDto
            {
                MatchFuncName = waitsGroup.GroupMatchFilter?.Method?.Name,
                MatchFuncClosureKey = _closureContextResolver.CacheClosureIfAny(waitsGroup.GroupMatchFilter?.Target, waitsGroup),
                CancelTokens = waitsGroup.CancelTokens
            };

            CopyBase(waitsGroup, dto);
            dto.CancelClosureKey = _closureContextResolver.CacheClosureIfAny(waitsGroup.CancelAction?.Target, waitsGroup);

            if (waitsGroup.ChildWaits?.Count > 0)
            {
                dto.ChildWaits = waitsGroup.ChildWaits.Select(MapToDto).ToList();
            }

            return dto;
        }

        public CommandWaitDto MapToDto<TCommand, TResult>(CommandWait<TCommand, TResult> commandWait)
        {
            if (commandWait == null) throw new ArgumentNullException(nameof(commandWait));

            var dto = new CommandWaitDto
            {
                CommandData = commandWait.CommandData == null
                    ? null
                    : _objectSerializer.Serialize(commandWait.CommandData, SerializationScope.Standard),
                MaxRetryAttempts = commandWait.MaxRetryAttempts,
                RetryBackoff = commandWait.RetryBackoff,
                CompensationMethodName = commandWait.CompensationAction?.Method?.Name,
                CancelAction = _delegateSerializer.Serialize(commandWait.CancelAction),
                ResultAction = _delegateSerializer.Serialize(commandWait.OnResultAction),
                HandlerKey = commandWait.HandlerKey,
                ExecutionMode = commandWait.ExecutionMode,
                ResultClosureKey = _closureContextResolver.CacheClosureIfAny(commandWait.OnResultAction?.Target, commandWait),
                CompensationClosureKey = _closureContextResolver.CacheClosureIfAny(commandWait.CompensationAction?.Target, commandWait)
            };

            CopyBase(commandWait, dto);
            dto.CancelClosureKey = _closureContextResolver.CacheClosureIfAny(commandWait.CancelAction?.Target, commandWait);

            return dto;
        }

        public SignalWaitDto MapToDto<TSignalData>(SignalWait<TSignalData> signalWait)
        {
            if (signalWait == null) throw new ArgumentNullException(nameof(signalWait));

            var serializedMatch = signalWait.MatchExpression == null
                ? null
                : _expressionSerializer.Serialize(signalWait.MatchExpression);

            var afterMatchAction = _delegateSerializer.Serialize(signalWait.AfterMatchAction);
            var cancelAction = _delegateSerializer.Serialize(signalWait.CancelAction);
            var matchClosureKey = _closureContextResolver.CacheClosureIfAny(_closureContextResolver.TryGetClosureFromExpression(signalWait.MatchExpression), signalWait);
            var afterMatchClosureKey = _closureContextResolver.CacheClosureIfAny(signalWait.AfterMatchAction?.Target, signalWait);

            var dto = new SignalWaitDto
            {
                SignalIdentifier = signalWait.SignalIdentifier,
                MatchExpression = serializedMatch,
                MatchExpressionAsText = signalWait.MatchExpressionAsText,
                MatchClosureKey = matchClosureKey,
                AfterMatchAction = afterMatchAction,
                AfterMatchClosureKey = afterMatchClosureKey,
                CancelAction = cancelAction,
                CancelTokens = signalWait.CancelTokens,
                TemplateHashKey = $"{signalWait.SignalIdentifier}:{signalWait.MatchExpressionAsText}"
            };

            CopyBase(signalWait, dto);
            dto.CancelClosureKey = _closureContextResolver.CacheClosureIfAny(signalWait.CancelAction?.Target, signalWait);

            return dto;
        }

        public WaitInfrastructureDto MapToDto(Wait wait)
        {
            if (wait == null) throw new ArgumentNullException(nameof(wait));

            var dto = wait switch
            {
                SubWorkflowWait subWorkflowWait => MapToDto(subWorkflowWait),
                TimeWait timeWait => MapToDto(timeWait),
                GroupWait groupWait => MapToDto(groupWait),
                ICommandWait commandWait => MapToDto((dynamic)commandWait),
                ISignalWait signalWait => MapToDto((dynamic)signalWait),
                _ => throw new NotSupportedException($"Unsupported wait type [{wait.GetType().FullName}].")
            };

            dto.CancelClosureKey ??= _closureContextResolver.CacheClosureIfAny(wait.CancelAction?.Target, wait);
            return dto;
        }

        private static void CopyBase(Wait wait, WaitInfrastructureDto dto)
        {
            dto.Id = wait.Id;
            dto.WaitName = wait.WaitName;
            dto.WaitType = wait.WaitType;
            dto.CallerName = wait.CallerName;
            dto.InCodeLine = wait.InCodeLine;
            dto.Created = wait.Created;
            dto.StateAfterWait = wait.StateAfterWait;
        }

        #endregion

        #region From DTO

        public Wait MapToWait(WaitInfrastructureDto dto, Abstraction.Runner.IWorkflowRegistry registry, WorkflowStateObject stateMachineObject = null)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            Wait wait = dto switch
            {
                SignalWaitDto signalDto => MapToWait(signalDto, registry),
                CommandWaitDto commandDto => MapToWait(commandDto, registry),
                TimeWaitDto timeDto => MapToWait(timeDto),
                GroupWaitDto groupDto => MapToWait(groupDto, registry, stateMachineObject),
                SubWorkflowWaitDto subWorkflowDto => MapToWait(subWorkflowDto, registry, stateMachineObject),
                _ => throw new NotSupportedException($"Unsupported DTO type [{dto.GetType().FullName}].")
            };

            RestoreBase(dto, wait);

            // Restore ExplicitState from WorkflowStateObject.WaitStatesObjects
            if (stateMachineObject?.WaitStatesObjects != null && 
                stateMachineObject.WaitStatesObjects.TryGetValue(wait.Id, out var explicitState))
            {
                wait.ExplicitState = explicitState;
            }

            return wait;
        }

        private Wait MapToWait(SignalWaitDto dto, Abstraction.Runner.IWorkflowRegistry registry)
        {
            var signalType = registry.SignalTypes.TryGetValue(dto.SignalIdentifier, out var type) ? type : typeof(object);
            var waitType = typeof(SignalWait<>).MakeGenericType(signalType);

            var factory = _constructorCache.GetOrAdd(waitType, t =>
            {
                var ctor = t.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string), typeof(string), typeof(int), typeof(string), typeof(string) }, null);
                return args => ctor.Invoke(args);
            });

            var wait = (ISignalWait)factory(new object[] { dto.SignalIdentifier, dto.WaitName, dto.InCodeLine, dto.CallerName, "" });

            if (!string.IsNullOrEmpty((string)dto.MatchExpression))
            {
                wait.MatchExpression = _expressionSerializer.Deserialize((string)dto.MatchExpression);
            }

            if (!string.IsNullOrEmpty(dto.AfterMatchAction))
            {
                // Note: Full implementation would require deserializing the delegate.
                // For now, we ensure the infrastructure exists to support AfterMatchAction if it's already restored
                // or if we add delegate restoration logic here.
            }

            var baseWait = (Wait)wait;
            var cancelTokensField = baseWait.GetType().GetProperty("CancelTokens", BindingFlags.Public | BindingFlags.Instance);
            cancelTokensField?.SetValue(baseWait, dto.CancelTokens);
            return baseWait;
        }

        private Wait MapToWait(CommandWaitDto dto, Abstraction.Runner.IWorkflowRegistry registry)
        {
            var commandInfo = registry.CommandTypes.TryGetValue(dto.HandlerKey, out var info) ? info : (CommandPayloadType: typeof(object), CommandResultType: typeof(object));
            var waitType = typeof(CommandWait<,>).MakeGenericType(commandInfo.CommandPayloadType, commandInfo.CommandResultType);

            var commandData = dto.CommandData == null ? null : (dto.CommandData is string s ? _objectSerializer.Deserialize(s, commandInfo.CommandPayloadType) : dto.CommandData);

            var wait = (Wait)Activator.CreateInstance(waitType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { dto.WaitName, commandData, dto.InCodeLine, dto.CallerName, "" }, null);

            var type = wait.GetType();
            type.GetProperty("HandlerKey", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(wait, dto.HandlerKey);
            type.GetProperty("ExecutionMode", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(wait, (int)dto.ExecutionMode == (int)CommandExecutionMode.DeferredCommand ? CommandExecutionMode.DeferredCommand : CommandExecutionMode.ImmediateCommand);
            type.GetProperty("MaxRetryAttempts", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(wait, dto.MaxRetryAttempts);
            type.GetProperty("RetryBackoff", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(wait, dto.RetryBackoff);

            return wait;
        }

        private Wait MapToWait(TimeWaitDto dto)
        {
            var wait = new TimeWait(dto.WaitName, dto.TimeToWait, dto.UniqueMatchId, dto.InCodeLine, dto.CallerName, "");
            wait.CancelTokens = dto.CancelTokens;
            return wait;
        }

        private Wait MapToWait(GroupWaitDto dto, Abstraction.Runner.IWorkflowRegistry registry, WorkflowStateObject stateMachineObject = null)
        {
            var childWaits = dto.ChildWaits?.Select(c => MapToWait(c, registry, stateMachineObject)).ToList() ?? new List<Wait>();
            var wait = new GroupWait(dto.WaitName, childWaits, dto.InCodeLine, dto.CallerName, "");
            wait.WaitType = (WaitType)(int)dto.WaitType;
            wait.CancelTokens = dto.CancelTokens;
            return wait;
        }

        private Wait MapToWait(SubWorkflowWaitDto dto, Abstraction.Runner.IWorkflowRegistry registry, WorkflowStateObject stateMachineObject = null)
        {
            var wait = new SubWorkflowWait(dto.WaitName, dto.InCodeLine, dto.CallerName, "");
            if (dto.ChildWaits?.Count > 0)
            {
                wait.FirstWait = MapToWait(dto.ChildWaits[0], registry, stateMachineObject);
            }
            wait.CancelTokens = dto.CancelTokens;
            return wait;
        }

        private static void RestoreBase(WaitInfrastructureDto dto, Wait wait)
        {
            wait.Id = dto.Id;
            wait.StateAfterWait = dto.StateAfterWait;
            wait.Created = dto.Created;
        }

        #endregion
    }
}