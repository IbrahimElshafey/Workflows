using System;
using System.Collections.Concurrent;
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

        public Mapper(
            IExpressionSerializer expressionSerializer,
            IObjectSerializer objectSerializer,
            IDelegateSerializer delegateSerializer)
        {
            _expressionSerializer = expressionSerializer ??
                throw new ArgumentNullException(nameof(expressionSerializer));
            _objectSerializer = objectSerializer ?? throw new ArgumentNullException(nameof(objectSerializer));
            _delegateSerializer = delegateSerializer ?? throw new ArgumentNullException(nameof(delegateSerializer));
        }

        #region To DTO
        public SubWorkflowWaitDto MapToDto(SubWorkflowWait waitsGroup)
        {
            if(waitsGroup == null)
                throw new ArgumentNullException(nameof(waitsGroup));

            var dto = new SubWorkflowWaitDto { CancelTokens = waitsGroup.CancelTokens };

            CopyBase(waitsGroup, dto);

            if(waitsGroup.FirstWait != null)
            {
                dto.ChildWaits = new List<WaitInfrastructureDto> { MapToDto(waitsGroup.FirstWait) };
            } else if(waitsGroup.ChildWaits?.Count > 0)
            {
                dto.ChildWaits = waitsGroup.ChildWaits.Select(MapToDto).ToList();
            }

            return dto;
        }

        public TimeWaitDto MapToDto(TimeWait waitsGroup)
        {
            if(waitsGroup == null)
                throw new ArgumentNullException(nameof(waitsGroup));

            var dto = new TimeWaitDto
            {
                TimeToWait = waitsGroup.TimeToWait,
                UniqueMatchId = waitsGroup.UniqueMatchId,
                CancelAction = _delegateSerializer.Serialize(waitsGroup.CancelAction),
                CancelTokens = waitsGroup.CancelTokens
            };

            CopyBase(waitsGroup, dto);
            return dto;
        }

        public GroupWaitDto MapToDto(GroupWait waitsGroup)
        {
            if(waitsGroup == null)
                throw new ArgumentNullException(nameof(waitsGroup));

            var dto = new GroupWaitDto
            {
                MatchFuncName = waitsGroup.GroupMatchFilter?.Method?.Name,
                CancelTokens = waitsGroup.CancelTokens
            };

            CopyBase(waitsGroup, dto);
            if(waitsGroup.ChildWaits?.Count > 0)
            {
                dto.ChildWaits = waitsGroup.ChildWaits.Select(MapToDto).ToList();
            }

            return dto;
        }

        public CommandWaitDto MapToDto<TCommand, TResult>(CommandWait<TCommand, TResult> commandWait)
        {
            if(commandWait == null)
                throw new ArgumentNullException(nameof(commandWait));

            CommandWaitDto? dto = new CommandWaitDto
            {
                CommandData = _objectSerializer.Serialize(commandWait.CommandData, SerializationScope.Standard),
                MaxRetryAttempts = commandWait.MaxRetryAttempts,
                RetryBackoff = commandWait.RetryBackoff,
                CompensationMethodName = commandWait.CompensationAction?.Method?.Name,
                CancelAction = _delegateSerializer.Serialize(commandWait.CancelAction),
                ResultAction = _delegateSerializer.Serialize(commandWait.OnResultAction),
                HandlerKey = commandWait.HandlerKey,
                ExecutionMode = commandWait.ExecutionMode,
            };

            CopyBase(commandWait, dto);
            return dto;
        }

        public SignalWaitDto MapToDto<TSignalData>(SignalWait<TSignalData> signalWait)
        {
            if(signalWait == null)
                throw new ArgumentNullException(nameof(signalWait));
            var serializedMatch = _expressionSerializer.Serialize(signalWait.MatchExpression);
            var afterMatchAction = _delegateSerializer.Serialize(signalWait.AfterMatchAction);
            var cancelAction = _delegateSerializer.Serialize(signalWait.CancelAction);
            var dto = new SignalWaitDto
            {
                SignalIdentifier = signalWait.SignalIdentifier,
                MatchExpression = serializedMatch,
                MatchExpressionAsText = signalWait.MatchExpressionAsText,
                AfterMatchAction = afterMatchAction,
                CancelAction = cancelAction,
                CancelTokens = signalWait.CancelTokens,
                //todo: consider a more robust way to generate TemplateHashKey that takes into account the structure of the expression and not just its text representation,
                //to avoid collisions in cases where different expressions have the same text (e.g., due to variable names).
                TemplateHashKey = $"{signalWait.SignalIdentifier}:{signalWait.MatchExpressionAsText}"
            };

            CopyBase(signalWait, dto);
            return dto;
        }

        public WaitInfrastructureDto MapToDto(Wait wait)
        {
            if(wait == null)
                throw new ArgumentNullException(nameof(wait));

            var dto = wait switch
            {
                SubWorkflowWait subWorkflowWait => MapToDto(subWorkflowWait),
                TimeWait timeWait => MapToDto(timeWait),
                GroupWait groupWait => MapToDto(groupWait),
                ICommandWait commandWait => MapToDto((dynamic)commandWait),
                ISignalWait signalWait => MapToDto((dynamic)signalWait),
                _ => throw new NotSupportedException($"Unsupported wait type [{wait.GetType().FullName}].")
            };
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
            dto.StateKey = wait.StateKey;
        }
        #endregion

        #region From DTO - Removed
        // MapToWait methods removed - we must not convert WaitDto types back to Wait objects.
        // Instead, work with DTOs directly and use CallerName to invoke workflow methods.
        #endregion
    }
}