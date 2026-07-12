using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Abstraction.Helpers;
using Workflows.Abstraction.Persistence;
using Workflows.Definition;
using Workflows.Primitives;
using Workflows.Definition.Helpers;
using Workflows.Runner.DataObjects;
using Workflows.Runner.ExpressionTransformers;
using Workflows.Runner.Pipeline.CompletionChecker;
using IExpressionSerializer = Workflows.Abstraction.Helpers.IExpressionSerializer;

namespace Workflows.Runner
{
    internal sealed class Mapper
    {
        private readonly IExpressionSerializer _expressionSerializer;
        private readonly IObjectSerializer _objectSerializer;
        private readonly MatchExpressionTransformer _matchExpressionTransformer;
        private readonly ICallbackRegistry _callbackRegistry;
        private readonly ITemplateRepository? _templateRepository;

        public IExpressionSerializer ExpressionSerializer => _expressionSerializer;

        public Mapper(
            IExpressionSerializer expressionSerializer,
            IObjectSerializer objectSerializer,
            MatchExpressionTransformer matchExpressionTransformer,
            ICallbackRegistry callbackRegistry,
            ITemplateRepository? templateRepository = null)
        {
            _expressionSerializer = expressionSerializer ??
                throw new ArgumentNullException(nameof(expressionSerializer));
            _objectSerializer = objectSerializer ?? throw new ArgumentNullException(nameof(objectSerializer));
            _matchExpressionTransformer = matchExpressionTransformer ?? throw new ArgumentNullException(nameof(matchExpressionTransformer));
            _callbackRegistry = callbackRegistry ?? throw new ArgumentNullException(nameof(callbackRegistry));
            _templateRepository = templateRepository;
        }

        private static string? GetFullMethodName(Delegate? callback)
        {
            if (callback == null) return null;
            var unwrapped = CallbackRegistry.UnwrapDelegate(callback);
            var owner = unwrapped.Method.DeclaringType?.FullName;
            return string.IsNullOrWhiteSpace(owner)
                ? unwrapped.Method.Name
                : $"{owner}.{unwrapped.Method.Name}";
        }

        #region To DTO
        public SubWorkflowWaitDto MapToDto(SubWorkflowWait waitsGroup) => MapToDto(waitsGroup, true);

        public SubWorkflowWaitDto MapToDto(SubWorkflowWait waitsGroup, bool mapChildren)
        {
            if(waitsGroup == null)
                throw new ArgumentNullException(nameof(waitsGroup));

            var dto = new SubWorkflowWaitDto { CancelTokens = waitsGroup.CancelTokens };

            CopyBase(waitsGroup, dto);

            // Set CallerName to the actual sub-workflow method name
            dto.CallerName = GetSubWorkflowMethodName(waitsGroup.Runner) ?? waitsGroup.CallerName;

            // Stable key for storing/retrieving this sub-workflow's state in Locals
            dto.StateMachineObjectId = Guid.NewGuid();

            if (mapChildren)
            {
                if(waitsGroup.FirstWait != null)
                {
                    dto.ChildWaits = new List<WaitInfrastructureDto> { MapToDto(waitsGroup.FirstWait) };
                } else if(waitsGroup.ChildWaits?.Count > 0)
                {
                    dto.ChildWaits = waitsGroup.ChildWaits.Select(MapToDto).ToList();
                }
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

            string? cancelActionKey = null;
            if (waitsGroup.CancelAction != null && !string.IsNullOrEmpty(waitsGroup.CancelActionKey))
            {
                cancelActionKey = $"Time:{waitsGroup.UniqueMatchId}:{waitsGroup.CancelActionKey}:Cancel";
                _callbackRegistry.Register(cancelActionKey, waitsGroup.CancelAction);
                if (_templateRepository != null)
                {
                    _templateRepository.SaveTemplate(new TemplateCacheRecordDto
                    {
                        TemplateHashKey = cancelActionKey,
                        AfterMatchAction = GetFullMethodName(waitsGroup.CancelAction)
                    });
                }
            }

            var dto = new TimeWaitDto
            {
                // Convert relative duration to an absolute UTC fire time at mapping time
                ExecutionTime = DateTime.UtcNow.Add(waitsGroup.TimeToWait),
                UniqueMatchId = waitsGroup.UniqueMatchId,
                CancelAction = cancelActionKey,
                CancelTokens = waitsGroup.CancelTokens
            };

            CopyBase(waitsGroup, dto);
            return dto;
        }


        public GroupWaitDto MapToDto(GroupWait waitsGroup) => MapToDto(waitsGroup, true);

        public GroupWaitDto MapToDto(GroupWait waitsGroup, bool mapChildren)
        {
            if(waitsGroup == null)
                throw new ArgumentNullException(nameof(waitsGroup));

            string? matchFuncName = null;
            if (waitsGroup.GroupMatchFilterOriginal != null)
            {
                if (!string.IsNullOrEmpty(waitsGroup.HandlerKey))
                {
                    matchFuncName = $"{waitsGroup.WaitName}:{waitsGroup.HandlerKey}";
                }
                else
                {
                    var hash = WorkflowHashCalculator.CalculateHash(null, waitsGroup.CallerName, "GroupMatch_" + waitsGroup.WaitName);
                    matchFuncName = $"{waitsGroup.WaitName}:{hash}";
                }

                _callbackRegistry.Register(matchFuncName, waitsGroup.GroupMatchFilterOriginal);

                if (_templateRepository != null)
                {
                    _templateRepository.SaveTemplate(new TemplateCacheRecordDto
                    {
                        TemplateHashKey = matchFuncName,
                        AfterMatchAction = GetFullMethodName(waitsGroup.GroupMatchFilterOriginal)
                    });
                }
            }

            var dto = new GroupWaitDto
            {
                MatchFuncName = matchFuncName,
                CancelTokens = waitsGroup.CancelTokens
            };

            CopyBase(waitsGroup, dto);
            if(mapChildren && waitsGroup.ChildWaits?.Count > 0)
            {
                dto.ChildWaits = waitsGroup.ChildWaits.Select(MapToDto).ToList();
            }

            return dto;
        }

        public ExternalGroupWaitDto MapToDto(ExternalGroupWait waitsGroup)
        {
            if (waitsGroup == null)
                throw new ArgumentNullException(nameof(waitsGroup));

            var requiredCompletedCount = waitsGroup.WaitType == WaitType.WaitAny ? 1 : waitsGroup.ChildWaits.Count;

            var dto = new ExternalGroupWaitDto
            {
                ChildCount = waitsGroup.ChildWaits.Count,
                RequiredCompletedCount = requiredCompletedCount,
                CancelTokens = waitsGroup.CancelTokens,
                // Keep child DTOs in memory for the current execution, but they will NOT be serialized into the state blob.
                ExternalChildWaits = waitsGroup.ChildWaits.Select(MapToDto).ToList()
            };

            CopyBase(waitsGroup, dto);
            return dto;
        }

        public CommandWaitDto MapToDto<TCommand, TResult>(CommandWait<TCommand, TResult> commandWait)
        {
            if(commandWait == null)
                throw new ArgumentNullException(nameof(commandWait));

            // Register live delegates into the registry so the runner can resolve them
            // by the stable HandlerKey (= command name) without any reflection.
            if (commandWait.OnResultAction != null)
            {
                var key = commandWait.HandlerKey + ":OnResult";
                _callbackRegistry.Register(key, commandWait.OnResultAction);
                if (_templateRepository != null)
                {
                    _templateRepository.SaveTemplate(new TemplateCacheRecordDto
                    {
                        TemplateHashKey = key,
                        AfterMatchAction = GetFullMethodName(commandWait.OnResultAction)
                    });
                }
            }
            if (commandWait.OnFailureAction != null)
            {
                var key = commandWait.HandlerKey + ":OnFailure";
                _callbackRegistry.Register(key, commandWait.OnFailureAction);
                if (_templateRepository != null)
                {
                    _templateRepository.SaveTemplate(new TemplateCacheRecordDto
                    {
                        TemplateHashKey = key,
                        AfterMatchAction = GetFullMethodName(commandWait.OnFailureAction)
                    });
                }
            }
            if (commandWait.CompensationAction != null)
            {
                var key = commandWait.HandlerKey + ":Compensation";
                _callbackRegistry.Register(key, commandWait.CompensationAction);
                if (_templateRepository != null)
                {
                    _templateRepository.SaveTemplate(new TemplateCacheRecordDto
                    {
                        TemplateHashKey = key,
                        AfterMatchAction = GetFullMethodName(commandWait.CompensationAction)
                    });
                }
            }

            string? cancelActionKey = null;
            if (commandWait.CancelAction != null && !string.IsNullOrEmpty(commandWait.CancelActionKey))
            {
                cancelActionKey = $"{commandWait.HandlerKey}:{commandWait.CancelActionKey}:Cancel";
                _callbackRegistry.Register(cancelActionKey, commandWait.CancelAction);
                if (_templateRepository != null)
                {
                    _templateRepository.SaveTemplate(new TemplateCacheRecordDto
                    {
                        TemplateHashKey = cancelActionKey,
                        AfterMatchAction = GetFullMethodName(commandWait.CancelAction)
                    });
                }
            }

            string? matchTemplateHashKey = null;
            if (commandWait.MatchExpression != null)
            {
                if (!string.IsNullOrEmpty(commandWait.MatchTemplateHashKey))
                {
                    matchTemplateHashKey = $"Cmd_{commandWait.HandlerKey}:{commandWait.MatchTemplateHashKey}";
                }
                else
                {
                    var hash = WorkflowHashCalculator.CalculateHash(
                        commandWait.MatchExpressionAsText,
                        commandWait.CallerName,
                        "Match_" + commandWait.HandlerKey);
                    matchTemplateHashKey = $"Cmd_{commandWait.HandlerKey}:{hash}";
                }
            }

            MatchTransformationResult? transformResult = null;
            TemplateCacheRecordDto? dbCached = null;

            if (commandWait.MatchExpression != null && !string.IsNullOrEmpty(matchTemplateHashKey))
            {
                dbCached = _templateRepository?.GetTemplate(matchTemplateHashKey);

                if (dbCached == null)
                {
                    transformResult = _matchExpressionTransformer.TransformCommandMatch(
                        commandWait.MatchExpression,
                        commandWait.WorkflowContainer);

                    if (_templateRepository != null)
                    {
                        var templateDto = new TemplateCacheRecordDto
                        {
                            TemplateHashKey = matchTemplateHashKey,
                            SignalExactMatchPathsJson = System.Text.Json.JsonSerializer.Serialize(transformResult.SignalExactMatchPaths),
                            IsExactMatchFullMatch = transformResult.IsExactMatchFullMatch,
                            IsGenericMatchFullMatch = transformResult.IsGenericMatchFullMatch,
                            GenericMatchExpressionJson = transformResult.GenericMatchExpression != null ? _expressionSerializer.Serialize(transformResult.GenericMatchExpression) as string : null,
                            InstanceExactMatchExpressionJson = (transformResult.SignalExactMatchPaths == null || transformResult.SignalExactMatchPaths.Count == 0)
                                 ? null
                                 : (transformResult.InstanceExactMatchExpression != null ? _expressionSerializer.Serialize(transformResult.InstanceExactMatchExpression) as string : null),
                            NormalizedMatchExpressionJson = transformResult.MatchExpression != null ? _expressionSerializer.Serialize(transformResult.MatchExpression) as string : null,
                            AfterMatchAction = GetFullMethodName(commandWait.OnResultAction),
                            CancelAction = null
                        };
                        _templateRepository.SaveTemplate(templateDto);
                    }
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
                instanceExactMatchExpr = (transformResult?.SignalExactMatchPaths == null || transformResult.SignalExactMatchPaths.Count == 0)
                    ? null
                    : transformResult?.InstanceExactMatchExpression;
            }

            string? matchExactMatchPart = null;
            if (instanceExactMatchExpr != null)
            {
                var compiler = new ExpressionCompiler();
                var exactMatchFunc = compiler.CompiledInstanceExactMatchExpression(instanceExactMatchExpr);
                var exactMatchParts = exactMatchFunc(commandWait.WorkflowContainer, commandWait.ExplicitState);
                if (exactMatchParts != null && exactMatchParts.Length > 0)
                {
                    matchExactMatchPart = System.Text.Json.JsonSerializer.Serialize(exactMatchParts);
                }
            }

            // Populate in-memory CommandCache
            if (!string.IsNullOrEmpty(matchTemplateHashKey))
            {
                LambdaExpression? normalizedExprToCache = null;
                LambdaExpression? instanceExprToCache = null;

                if (commandWait.MatchExpression != null)
                {
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
                        instanceExprToCache = (transformResult?.SignalExactMatchPaths == null || transformResult.SignalExactMatchPaths.Count == 0)
                            ? null
                            : transformResult?.InstanceExactMatchExpression;
                    }
                }

                var serializedResultAction = GetFullMethodName(commandWait.OnResultAction);

                Func<object, object, object, bool>? compiledDelegate = null;
                Func<object, object, string[]>? compiledInstanceExpr = null;

                if (normalizedExprToCache != null)
                {
                    var compiler = new ExpressionCompiler();
                    compiledDelegate = compiler.CompiledMatchExpression(normalizedExprToCache);
                    if (instanceExprToCache != null)
                    {
                        compiledInstanceExpr = compiler.CompiledInstanceExactMatchExpression(instanceExprToCache);
                    }
                }

                var record = Pipeline.CompletionChecker.CommandCompletionChecker.CommandCache.GetOrAdd(matchTemplateHashKey, _ => new Cache.CommandTemplateCacheRecord
                {
                    CompiledMatchDelegate = compiledDelegate,
                    CompiledInstanceExactMatchExpression = compiledInstanceExpr,
                    ResultAction = serializedResultAction
                });

                if (record.ResultAction == null && serializedResultAction != null)
                {
                    record.ResultAction = serializedResultAction;
                }
                if (record.CompiledMatchDelegate == null && compiledDelegate != null)
                {
                    record.CompiledMatchDelegate = compiledDelegate;
                    record.CompiledInstanceExactMatchExpression = compiledInstanceExpr;
                }
            }

            CommandWaitDto? dto = new CommandWaitDto
            {
                CommandData = _objectSerializer.Serialize(commandWait.CommandData, SerializationScope.Standard),
                MaxRetryAttempts = commandWait.MaxRetryAttempts,
                RetryBackoff = commandWait.RetryBackoff,
                CompensationMethodName = commandWait.CompensationAction?.Method?.Name,
                CancelAction = cancelActionKey,
                ResultAction = commandWait.HandlerKey + ":OnResult",
                HandlerKey = commandWait.HandlerKey,
                ExecutionMode = commandWait.ExecutionMode,
                CompensationTokens = commandWait.CompensationTokens ?? Array.Empty<string>(),
                MatchTemplateHashKey = matchTemplateHashKey,
                MatchExactMatchPart = matchExactMatchPart
            };

            CopyBase(commandWait, dto);
            return dto;
        }

        public SignalWaitDto MapToDto<TSignalData>(SignalWait<TSignalData> signalWait)
        {
            if(signalWait == null)
                throw new ArgumentNullException(nameof(signalWait));

            // Resolve the stable template hash key.
            // Priority: HandlerKey set by MatchIf (most precise) → fallback to CallerName hashing.
            string? templateHashKey;
            if (!string.IsNullOrEmpty(signalWait.HandlerKey))
            {
                // Hash key was pre-computed in MatchIf via WorkflowHashCalculator.
                templateHashKey = $"{signalWait.SignalIdentifier}:{signalWait.HandlerKey}";
            }
            else if (signalWait.MatchExpression != null)
            {
                // Fallback: compute here using CallerName + expression text (MatchAny path won't hit this).
                var hash = WorkflowHashCalculator.CalculateHash(
                    signalWait.MatchExpressionAsText,
                    signalWait.CallerName,
                    "Match_" + signalWait.SignalIdentifier);
                templateHashKey = $"{signalWait.SignalIdentifier}:{hash}";
            }
            else
            {
                var wfName = signalWait.WorkflowContainer?.GetType().Name ?? string.Empty;
                templateHashKey = $"{signalWait.SignalIdentifier}:{wfName}";
            }
            // Register the live AfterMatchAction delegate in the CallbackRegistry so the
            // SignalWaitMatcher can retrieve it by the stable hash key without reflection.
            if (signalWait.AfterMatchAction != null && !string.IsNullOrEmpty(templateHashKey))
                _callbackRegistry.Register(templateHashKey, signalWait.AfterMatchAction);

            MatchTransformationResult? transformResult = null;
            TemplateCacheRecordDto? dbCached = null;

            if (signalWait.MatchExpression != null)
            {
                // Try to load template from SQLite DB cache first
                dbCached = _templateRepository?.GetTemplate(templateHashKey);

                if (dbCached == null)
                {
                    // Transform expression structure since it is a cache miss
                    transformResult = _matchExpressionTransformer.Transform(signalWait.MatchExpression, signalWait.WorkflowContainer);

                    // Save template to SQLite DB cache (expressions + callbacks)
                    if (_templateRepository != null)
                    {
                        var templateDto = new TemplateCacheRecordDto
                        {
                            TemplateHashKey = templateHashKey,
                            SignalExactMatchPathsJson = System.Text.Json.JsonSerializer.Serialize(transformResult.SignalExactMatchPaths),
                            IsExactMatchFullMatch = transformResult.IsExactMatchFullMatch,
                            IsGenericMatchFullMatch = transformResult.IsGenericMatchFullMatch,
                            GenericMatchExpressionJson = transformResult.GenericMatchExpression != null ? _expressionSerializer.Serialize(transformResult.GenericMatchExpression) as string : null,
                            InstanceExactMatchExpressionJson = (transformResult.SignalExactMatchPaths == null || transformResult.SignalExactMatchPaths.Count == 0)
                                 ? null
                                 : (transformResult.InstanceExactMatchExpression != null ? _expressionSerializer.Serialize(transformResult.InstanceExactMatchExpression) as string : null),
                            NormalizedMatchExpressionJson = transformResult.MatchExpression != null ? _expressionSerializer.Serialize(transformResult.MatchExpression) as string : null,
                            AfterMatchAction = GetFullMethodName(signalWait.AfterMatchAction),
                            CancelAction = null
                        };
                        _templateRepository.SaveTemplate(templateDto);
                    }
                }
            }
            else
            {
                // MatchAny or empty expression - save template if we have AfterMatchAction and template key
                if (signalWait.AfterMatchAction != null && _templateRepository != null)
                {
                    var templateDto = new TemplateCacheRecordDto
                    {
                        TemplateHashKey = templateHashKey,
                        AfterMatchAction = GetFullMethodName(signalWait.AfterMatchAction),
                        CancelAction = null
                    };
                    _templateRepository.SaveTemplate(templateDto);
                }
            }

            string? cancelActionKey = null;
            if (signalWait.CancelAction != null && !string.IsNullOrEmpty(signalWait.CancelActionKey))
            {
                cancelActionKey = $"{signalWait.SignalIdentifier}:{signalWait.CancelActionKey}:Cancel";
                _callbackRegistry.Register(cancelActionKey, signalWait.CancelAction);
                if (_templateRepository != null)
                {
                    _templateRepository.SaveTemplate(new TemplateCacheRecordDto
                    {
                        TemplateHashKey = cancelActionKey,
                        AfterMatchAction = GetFullMethodName(signalWait.CancelAction)
                    });
                }
            }

            // Build DTO — instance-specific data including callbacks.
            // Template-level data (expressions, match paths) is in the template cache.
            // NOTE: The CallbackRegistry provides the fast same-process path via TemplateHashKey.
            var dto = new SignalWaitDto
            {
                SignalIdentifier = signalWait.SignalIdentifier,
                TemplateHashKey = templateHashKey,
                AfterMatchAction = templateHashKey,
                CancelAction = cancelActionKey,
                CancelTokens = signalWait.CancelTokens,
            };

            // Compute instance-specific ExactMatchPart (evaluated against current workflow state)
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
                instanceExactMatchExpr = (transformResult?.SignalExactMatchPaths == null || transformResult.SignalExactMatchPaths.Count == 0)
                    ? null
                    : transformResult?.InstanceExactMatchExpression;
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

            // Populate in-memory SignalCache (expressions + callbacks) for this templateHashKey.
            // This runs for ALL signals — even those without a MatchExpression — so that
            // AfterMatchAction callbacks are always available to SignalWaitMatcher.
            if (!string.IsNullOrEmpty(templateHashKey))
            {
                LambdaExpression? normalizedExprToCache = null;
                LambdaExpression? instanceExprToCache = null;

                if (signalWait.MatchExpression != null)
                {
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
                        instanceExprToCache = (transformResult?.SignalExactMatchPaths == null || transformResult.SignalExactMatchPaths.Count == 0)
                            ? null
                            : transformResult?.InstanceExactMatchExpression;
                    }
                }

                // Resolve the serialized afterMatchAction for the cache record
                var serializedAfterMatch = GetFullMethodName(signalWait.AfterMatchAction);

                Func<object, object, object, bool>? compiledDelegate = null;
                Func<object, object, string[]>? compiledInstanceExpr = null;

                if (normalizedExprToCache != null)
                {
                    var compiler = new ExpressionCompiler();
                    compiledDelegate = compiler.CompiledMatchExpression(normalizedExprToCache);
                    if (instanceExprToCache != null)
                    {
                        compiledInstanceExpr = compiler.CompiledInstanceExactMatchExpression(instanceExprToCache);
                    }
                }

                var record = SignalCompletionChecker.SignalCache.GetOrAdd(templateHashKey, _ => new Cache.SignalTemplateCacheRecord
                {
                    CompiledMatchDelegate = compiledDelegate,
                    CompiledInstanceExactMatchExpression = compiledInstanceExpr,
                    AfterMatchAction = serializedAfterMatch
                });

                // Always keep AfterMatchAction up to date in case the record was created without it
                if (record.AfterMatchAction == null && serializedAfterMatch != null)
                {
                    record.AfterMatchAction = serializedAfterMatch;
                }
                // Ensure compiled delegates are set if the record was created without them
                if (record.CompiledMatchDelegate == null && compiledDelegate != null)
                {
                    record.CompiledMatchDelegate = compiledDelegate;
                    record.CompiledInstanceExactMatchExpression = compiledInstanceExpr;
                }
            }



            CopyBase(signalWait, dto);
            return dto;
        }

        public PlaceholderWaitDto MapToDto(PlaceholderWait wait)
        {
            var dto = new PlaceholderWaitDto();
            CopyBase(wait, dto);
            return dto;
        }

        public PlaceholderSubWorkflowWaitDto MapToDto(PlaceholderSubWorkflowWait wait)
        {
            var dto = new PlaceholderSubWorkflowWaitDto
            {
                MethodFullPath = wait.MethodFullPath
            };
            CopyBase(wait, dto);
            return dto;
        }

        public CompensationWaitDto MapToDto(CompensationWait wait)
        {
            var dto = new CompensationWaitDto
            {
                Token = wait.Token
            };
            CopyBase(wait, dto);
            return dto;
        }

        public WaitInfrastructureDto MapToDto(Wait wait)
        {
            if(wait == null)
                throw new ArgumentNullException(nameof(wait));

            var dto = wait switch
            {
                PlaceholderWait placeholderWait => MapToDto(placeholderWait),
                PlaceholderSubWorkflowWait placeholderSubWorkflowWait => MapToDto(placeholderSubWorkflowWait),
                SubWorkflowWait subWorkflowWait => MapToDto(subWorkflowWait),
                TimeWait timeWait => MapToDto(timeWait),
                GroupWait groupWait => MapToDto(groupWait),
                ExternalGroupWait externalGroupWait => MapToDto(externalGroupWait),
                CompensationWait compensationWait => MapToDto(compensationWait),
                Wait w when w.WaitType == WaitType.Command => MapToDto((dynamic)w),
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