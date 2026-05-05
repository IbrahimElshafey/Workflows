using System;
using System.Collections.Generic;
using System.Linq;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;
using Workflows.Runner.Helpers;
using Workflows.Shared.DataObject;
using Workflows.Shared.Serialization;

namespace Workflows.Runner
{
    internal sealed class Mapper
    {
        private readonly IExpressionSerializer _expressionSerializer;
        private readonly IObjectSerializer _objectSerializer;
        private readonly IDelegateSerializer _delegateSerializer;
        private readonly IClosureContextResolver _closureContextResolver;

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
                ExecutionMode = commandWait.ExecutionMode == CommandExecutionMode.Indirect
                    ? CommandExecutionMode.Indirect
                    : CommandExecutionMode.Direct,
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
                MatchClosureKey = matchClosureKey,
                AfterMatchAction = afterMatchAction,
                AfterMatchClosureKey = afterMatchClosureKey,
                CancelAction = cancelAction,
                CancelTokens = signalWait.CancelTokens
            };

            CopyBase(signalWait, dto);
            dto.CancelClosureKey = _closureContextResolver.CacheClosureIfAny(signalWait.CancelAction?.Target, signalWait);

            return dto;
        }

        private WaitInfrastructureDto MapToDto(Wait wait)
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
            dto.WaitType = (Abstraction.Enums.WaitType)(int)wait.WaitType;
            dto.CallerName = wait.CallerName;
            dto.InCodeLine = wait.InCodeLine;
            dto.Created = wait.Created;
            dto.StateAfterWait = wait.StateAfterWait;
        }
    }
}