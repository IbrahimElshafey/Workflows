using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Persistence;
using Workflows.Definition;
using Workflows.Runner.DataObjects;
using Workflows.Runner.ExpressionTransformers;
using IExpressionSerializer = Workflows.Abstraction.Helpers.IExpressionSerializer;

namespace Workflows.Runner
{
    internal sealed class Mapper
    {
        private readonly IExpressionSerializer _expressionSerializer;
        private readonly IObjectSerializer _objectSerializer;
        private readonly IDelegateSerializer _delegateSerializer;
        private readonly MatchExpressionTransformer _matchExpressionTransformer;
        private readonly ITemplateRepository? _templateRepository;

        public IExpressionSerializer ExpressionSerializer => _expressionSerializer;

        public Mapper(
            IExpressionSerializer expressionSerializer,
            IObjectSerializer objectSerializer,
            IDelegateSerializer delegateSerializer,
            MatchExpressionTransformer matchExpressionTransformer,
            ITemplateRepository? templateRepository = null)
        {
            _expressionSerializer = expressionSerializer ??
                throw new ArgumentNullException(nameof(expressionSerializer));
            _objectSerializer = objectSerializer ?? throw new ArgumentNullException(nameof(objectSerializer));
            _delegateSerializer = delegateSerializer ?? throw new ArgumentNullException(nameof(delegateSerializer));
            _matchExpressionTransformer = matchExpressionTransformer ?? throw new ArgumentNullException(nameof(matchExpressionTransformer));
            _templateRepository = templateRepository;
        }

        #region To DTO
        public SubWorkflowWaitDto MapToDto(SubWorkflowWait waitsGroup)
        {
            if(waitsGroup == null)
                throw new ArgumentNullException(nameof(waitsGroup));

            var dto = new SubWorkflowWaitDto { CancelTokens = waitsGroup.CancelTokens };

            CopyBase(waitsGroup, dto);

            // Set CallerName to the actual sub-workflow method name
            dto.CallerName = GetSubWorkflowMethodName(waitsGroup.Runner) ?? waitsGroup.CallerName;

            // Stable key for storing/retrieving this sub-workflow's state in StateMachinesObjects
            dto.StateMachineObjectId = dto.Id;

            if(waitsGroup.FirstWait != null)
            {
                dto.ChildWaits = new List<WaitInfrastructureDto> { MapToDto(waitsGroup.FirstWait) };
            } else if(waitsGroup.ChildWaits?.Count > 0)
            {
                dto.ChildWaits = waitsGroup.ChildWaits.Select(MapToDto).ToList();
            }

            return dto;
        }

        private static string GetSubWorkflowMethodName(object runner)
        {
            if (runner == null) return null;
            var typeName = runner.GetType().Name;
            if (typeName.StartsWith("<") && typeName.Contains(">"))
            {
                int end = typeName.IndexOf('>');
                if (end > 1)
                {
                    return typeName.Substring(1, end - 1);
                }
            }
            return null;
        }

        public TimeWaitDto MapToDto(TimeWait waitsGroup)
        {
            if(waitsGroup == null)
                throw new ArgumentNullException(nameof(waitsGroup));

            var dto = new TimeWaitDto
            {
                // Convert relative duration to an absolute UTC fire time at mapping time
                ExecutionTime = DateTime.UtcNow.Add(waitsGroup.TimeToWait),
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
                MatchFuncName = waitsGroup.GroupMatchFilterOriginal != null 
                    ? _delegateSerializer.Serialize(waitsGroup.GroupMatchFilterOriginal)
                    : null,
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

            string? templateHashKey = null;
            MatchTransformationResult? transformResult = null;
            TemplateCacheRecordDto? dbCached = null;

            if (signalWait.MatchExpression != null)
            {
                // Generate a robust template hash key using normalized expression tree to avoid collisions
                var normalizer = new MatchExpressionNormalizer();
                var normalizedExpr = normalizer.Normalize(signalWait.MatchExpression, signalWait.WorkflowContainer);
                var expressionStr = normalizedExpr.ToString();

                string hashStr;
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(expressionStr));
                    hashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
                templateHashKey = $"{signalWait.SignalIdentifier}:{hashStr}";

                // Try to load template from SQLite DB cache first
                dbCached = _templateRepository?.GetTemplate(templateHashKey);

                if (dbCached == null)
                {
                    // Transform expression structure since it is a cache miss
                    transformResult = _matchExpressionTransformer.Transform(signalWait.MatchExpression, signalWait.WorkflowContainer);

                    // Save template to SQLite DB cache
                    if (_templateRepository != null)
                    {
                        var templateDto = new TemplateCacheRecordDto
                        {
                            TemplateHashKey = templateHashKey,
                            SignalExactMatchPathsJson = System.Text.Json.JsonSerializer.Serialize(transformResult.SignalExactMatchPaths),
                            IsExactMatchFullMatch = transformResult.IsExactMatchFullMatch,
                            IsGenericMatchFullMatch = transformResult.IsGenericMatchFullMatch,
                            GenericMatchExpressionJson = transformResult.GenericMatchExpression != null ? _expressionSerializer.Serialize(transformResult.GenericMatchExpression) as string : null,
                            InstanceExactMatchExpressionJson = transformResult.InstanceExactMatchExpression != null ? _expressionSerializer.Serialize(transformResult.InstanceExactMatchExpression) as string : null,
                            NormalizedMatchExpressionJson = transformResult.MatchExpression != null ? _expressionSerializer.Serialize(transformResult.MatchExpression) as string : null
                        };
                        _templateRepository.SaveTemplate(templateDto);
                    }
                }
            }
            else
            {
                templateHashKey = $"{signalWait.SignalIdentifier}:";
            }

            var dto = new SignalWaitDto
            {
                SignalIdentifier = signalWait.SignalIdentifier,
                MatchExpression = serializedMatch,
                MatchExpressionAsText = signalWait.MatchExpressionAsText,
                AfterMatchAction = afterMatchAction,
                CancelAction = cancelAction,
                CancelTokens = signalWait.CancelTokens,
                TemplateHashKey = templateHashKey
            };

            if (dbCached != null)
            {
                dto.SignalExactMatchPaths = System.Text.Json.JsonSerializer.Deserialize<List<string>>(dbCached.SignalExactMatchPathsJson) ?? new List<string>();
                dto.IsExactMatchFullMatch = dbCached.IsExactMatchFullMatch;
                dto.IsGenericMatchFullMatch = dbCached.IsGenericMatchFullMatch;
                dto.GenericMatchExpression = dbCached.GenericMatchExpressionJson;
            }
            else
            {
                dto.SignalExactMatchPaths = transformResult?.SignalExactMatchPaths ?? new List<string>();
                dto.IsExactMatchFullMatch = transformResult?.IsExactMatchFullMatch ?? false;
                dto.IsGenericMatchFullMatch = transformResult?.IsGenericMatchFullMatch ?? false;

                if (transformResult?.GenericMatchExpression != null)
                {
                    dto.GenericMatchExpression = _expressionSerializer.Serialize(transformResult.GenericMatchExpression) as string;
                }
            }

            LambdaExpression? instanceExactMatchExpr = null;
            if (dbCached != null)
            {
                if (dbCached.InstanceExactMatchExpressionJson != null)
                {
                    instanceExactMatchExpr = _expressionSerializer.Deserialize(dbCached.InstanceExactMatchExpressionJson);
                }
            }
            else
            {
                instanceExactMatchExpr = transformResult?.InstanceExactMatchExpression;
            }

            if (instanceExactMatchExpr != null)
            {
                var compiler = new ExpressionCompiler();
                var exactMatchFunc = compiler.CompiledInstanceExactMatchExpression(instanceExactMatchExpr);
                var exactMatchParts = exactMatchFunc(signalWait.WorkflowContainer, signalWait.ExplicitState);
                if (exactMatchParts != null && exactMatchParts.Length > 0)
                {
                    dto.ExactMatchPart = System.Text.Json.JsonSerializer.Serialize(exactMatchParts);
                }
            }

            // Also populate in-memory SignalCache if missing
            if (signalWait.MatchExpression != null && !string.IsNullOrEmpty(templateHashKey) && !Pipeline.Matchers.SignalWaitMatcher.SignalCache.ContainsKey(templateHashKey))
            {
                LambdaExpression? normalizedExprToCache = null;
                LambdaExpression? instanceExprToCache = null;

                if (dbCached != null)
                {
                    if (dbCached.NormalizedMatchExpressionJson != null)
                    {
                        normalizedExprToCache = _expressionSerializer.Deserialize(dbCached.NormalizedMatchExpressionJson);
                    }
                    if (dbCached.InstanceExactMatchExpressionJson != null)
                    {
                        instanceExprToCache = _expressionSerializer.Deserialize(dbCached.InstanceExactMatchExpressionJson);
                    }
                }
                else
                {
                    normalizedExprToCache = transformResult?.MatchExpression;
                    instanceExprToCache = transformResult?.InstanceExactMatchExpression;
                }

                if (normalizedExprToCache != null)
                {
                    var compiler = new ExpressionCompiler();
                    var compiled = compiler.CompiledMatchExpression(normalizedExprToCache);
                    Func<object, object, string[]>? compiledInstanceExpr = null;
                    if (instanceExprToCache != null)
                    {
                        compiledInstanceExpr = compiler.CompiledInstanceExactMatchExpression(instanceExprToCache);
                    }

                    var record = new Cache.SignalTemplateCacheRecord
                    {
                        CompiledMatchDelegate = compiled,
                        CompiledInstanceExactMatchExpression = compiledInstanceExpr
                    };
                    Pipeline.Matchers.SignalWaitMatcher.SignalCache.TryAdd(templateHashKey, record);
                }
            }

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
                Wait w when w.WaitType == Workflows.Primitives.WaitType.Command => MapToDto((dynamic)w),
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