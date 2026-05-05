using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Enums;
using Workflows.Common.Abstraction;
using Workflows.Definition;

namespace Workflows.Runner.Helpers
{
    internal sealed class WaitMapper
    {
        private static readonly MethodInfo _mapCommandMethod = typeof(WaitMapper)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(x => x.Name == nameof(MapToDto)
                      && x.IsGenericMethodDefinition
                      && x.GetGenericArguments().Length == 2
                      && x.GetParameters().Length == 1
                      && x.GetParameters()[0].ParameterType.IsGenericType
                      && x.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(CommandWait<,>));

        private static readonly MethodInfo _mapSignalMethod = typeof(WaitMapper)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(x => x.Name == nameof(MapToDto)
                      && x.IsGenericMethodDefinition
                      && x.GetGenericArguments().Length == 1
                      && x.GetParameters().Length == 1
                      && x.GetParameters()[0].ParameterType.IsGenericType
                      && x.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(SignalWait<>));

        private readonly Common.Abstraction.IExpressionSerializer _expressionSerializer;
        private readonly IObjectSerializer _objectSerializer;

        public WaitMapper(Common.Abstraction.IExpressionSerializer expressionSerializer, IObjectSerializer objectSerializer)
        {
            _expressionSerializer = expressionSerializer ?? throw new ArgumentNullException(nameof(expressionSerializer));
            _objectSerializer = objectSerializer ?? throw new ArgumentNullException(nameof(objectSerializer));
        }

        public SubWorkflowWaitDto MapToDto(SubWorkflowWait waitsGroup)
        {
            if (waitsGroup == null) throw new ArgumentNullException(nameof(waitsGroup));

            var dto = new SubWorkflowWaitDto
            {
                CancelTokens = CopyCancelTokens(waitsGroup.CancelTokens)
            };

            CopyBase(waitsGroup, dto);
            dto.CancelClosureKey = CacheClosureIfAny(waitsGroup.CancelAction?.Target, waitsGroup);

            if (waitsGroup.FirstWait != null)
            {
                dto.ChildWaits = new List<WaitInfrastructureDto> { MapAnyToDto(waitsGroup.FirstWait) };
            }
            else if (waitsGroup.ChildWaits?.Count > 0)
            {
                dto.ChildWaits = waitsGroup.ChildWaits.Select(MapAnyToDto).ToList();
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
                CancelAction = SerializeDelegate(waitsGroup.CancelAction),
                CancelTokens = CopyCancelTokens(waitsGroup.CancelTokens)
            };

            CopyBase(waitsGroup, dto);
            dto.CancelClosureKey = CacheClosureIfAny(waitsGroup.CancelAction?.Target, waitsGroup);

            return dto;
        }

        public GroupWaitDto MapToDto(GroupWait waitsGroup)
        {
            if (waitsGroup == null) throw new ArgumentNullException(nameof(waitsGroup));

            var dto = new GroupWaitDto
            {
                MatchFuncName = waitsGroup.GroupMatchFilter?.Method?.Name,
                MatchFuncClosureKey = CacheClosureIfAny(waitsGroup.GroupMatchFilter?.Target, waitsGroup),
                CancelTokens = CopyCancelTokens(waitsGroup.CancelTokens)
            };

            CopyBase(waitsGroup, dto);
            dto.CancelClosureKey = CacheClosureIfAny(waitsGroup.CancelAction?.Target, waitsGroup);

            if (waitsGroup.ChildWaits?.Count > 0)
            {
                dto.ChildWaits = waitsGroup.ChildWaits.Select(MapAnyToDto).ToList();
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
                CancelAction = SerializeDelegate(commandWait.CancelAction),
                ResultAction = SerializeDelegate(commandWait.OnResultAction),
                HandlerKey = commandWait.HandlerKey,
                ExecutionMode = commandWait.ExecutionMode == Common.Abstraction.CommandExecutionMode.Indirect
                    ? Common.Abstraction.CommandExecutionMode.Indirect
                    : Common.Abstraction.CommandExecutionMode.Direct,
                ResultClosureKey = CacheClosureIfAny(commandWait.OnResultAction?.Target, commandWait),
                CompensationClosureKey = CacheClosureIfAny(commandWait.CompensationAction?.Target, commandWait)
            };

            CopyBase(commandWait, dto);
            dto.CancelClosureKey = CacheClosureIfAny(commandWait.CancelAction?.Target, commandWait);

            return dto;
        }

        public SignalWaitDto MapToDto<TSignalData>(SignalWait<TSignalData> signalWait)
        {
            if (signalWait == null) throw new ArgumentNullException(nameof(signalWait));

            var serializedMatch = signalWait.MatchExpression == null
                ? null
                : _expressionSerializer.Serialize(signalWait.MatchExpression);

            var afterMatchAction = SerializeDelegate(signalWait.AfterMatchAction);
            var cancelAction = SerializeDelegate(signalWait.CancelAction);
            var matchClosureKey = CacheClosureIfAny(TryGetClosureFromExpression(signalWait.MatchExpression), signalWait);
            var afterMatchClosureKey = CacheClosureIfAny(signalWait.AfterMatchAction?.Target, signalWait);

            var dto = new SignalWaitDto
            {
                SignalIdentifier = signalWait.SignalIdentifier,
                MatchExpression = serializedMatch,
                MatchClosureKey = matchClosureKey,
                AfterMatchAction = afterMatchAction,
                AfterMatchClosureKey = afterMatchClosureKey,
                CancelAction = cancelAction,
                CancelTokens = CopyCancelTokens(signalWait.CancelTokens),
                MatchingTemplate = new MatchingTemplateDto
                {
                    MatchExpression = serializedMatch,
                    AfterMatchAction = afterMatchAction,
                    CancelAction = cancelAction,
                    SignalIdentifier = signalWait.SignalIdentifier
                }
            };

            CopyBase(signalWait, dto);
            dto.CancelClosureKey = CacheClosureIfAny(signalWait.CancelAction?.Target, signalWait);

            return dto;
        }

        private WaitInfrastructureDto MapAnyToDto(Wait wait)
        {
            if (wait == null) throw new ArgumentNullException(nameof(wait));

            if (wait is SubWorkflowWait subWorkflowWait)
                return MapToDto(subWorkflowWait);

            if (wait is TimeWait timeWait)
                return MapToDto(timeWait);

            if (wait is GroupWait groupWait)
                return MapToDto(groupWait);

            var type = wait.GetType();
            if (type.IsGenericType)
            {
                var genericDef = type.GetGenericTypeDefinition();

                if (genericDef == typeof(CommandWait<,>))
                {
                    var method = _mapCommandMethod.MakeGenericMethod(type.GetGenericArguments());
                    return (WaitInfrastructureDto)method.Invoke(this, new object[] { wait });
                }

                if (genericDef == typeof(SignalWait<>))
                {
                    var method = _mapSignalMethod.MakeGenericMethod(type.GetGenericArguments());
                    return (WaitInfrastructureDto)method.Invoke(this, new object[] { wait });
                }
            }

            throw new NotSupportedException($"Unsupported wait type [{type.FullName}].");
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

        private static HashSet<string> CopyCancelTokens(HashSet<string> cancelTokens)
        {
            return cancelTokens == null ? null : new HashSet<string>(cancelTokens);
        }

        private static string SerializeDelegate(Delegate callback)
        {
            if (callback == null)
                return null;

            var owner = callback.Method.DeclaringType?.FullName;
            return string.IsNullOrWhiteSpace(owner)
                ? callback.Method.Name
                : $"{owner}.{callback.Method.Name}";
        }

        private static string CacheClosureIfAny(object actionTarget, Wait wait)
        {
            var workflowContainer = wait.WorkflowContainer;
            if (actionTarget == null || workflowContainer?.Variables == null)
                return null;

            var closureType = actionTarget.GetType();
            if (!closureType.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal))
                return null;

            var key = closureType.FullName ?? closureType.Name;
            workflowContainer.Variables[key] = actionTarget;
            return key;
        }

        private static object TryGetClosureFromExpression(Expression expr)
        {
            if (expr == null) return null;

            var stack = new Stack<Expression>();
            stack.Push(expr);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null) continue;

                if (current is ConstantExpression constant && constant.Value != null)
                {
                    var type = constant.Value.GetType();
                    bool isClosure = type.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal) ||
                                     type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

                    if (isClosure)
                    {
                        return constant.Value;
                    }
                    continue;
                }

                if (current is MemberExpression member)
                {
                    stack.Push(member.Expression);
                }
                else if (current is BinaryExpression binary)
                {
                    stack.Push(binary.Left);
                    stack.Push(binary.Right);
                }
                else if (current is LambdaExpression lambda)
                {
                    stack.Push(lambda.Body);
                }
                else if (current is UnaryExpression unary)
                {
                    stack.Push(unary.Operand);
                }
                else if (current is MethodCallExpression call)
                {
                    stack.Push(call.Object);
                    foreach (var arg in call.Arguments) stack.Push(arg);
                }
                else if (current is ConditionalExpression conditional)
                {
                    stack.Push(conditional.Test);
                    stack.Push(conditional.IfTrue);
                    stack.Push(conditional.IfFalse);
                }
                else if (current is InvocationExpression invocation)
                {
                    stack.Push(invocation.Expression);
                    foreach (var arg in invocation.Arguments) stack.Push(arg);
                }
                else if (current is NewExpression newExpr)
                {
                    foreach (var arg in newExpr.Arguments) stack.Push(arg);
                }
                else if (current is NewArrayExpression newArray)
                {
                    foreach (var element in newArray.Expressions) stack.Push(element);
                }
                else if (current is MemberInitExpression memberInit)
                {
                    stack.Push(memberInit.NewExpression);
                    foreach (var binding in memberInit.Bindings)
                    {
                        if (binding is MemberAssignment assignment)
                        {
                            stack.Push(assignment.Expression);
                        }
                    }
                }
            }

            return null;
        }
    }
}