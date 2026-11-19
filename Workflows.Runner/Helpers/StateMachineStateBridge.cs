using FastExpressionCompiler;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Workflows.Abstraction.DTOs;

namespace Workflows.Runner.Helpers
{
    internal static class StateMachineStateBridge
    {
        private static readonly ConcurrentDictionary<Type, Action<object, StateMachineObject>> _hydratorCache
            = new ConcurrentDictionary<Type, Action<object, StateMachineObject>>();

        private static readonly ConcurrentDictionary<Type, Func<object, StateMachineObject>> _dehydratorCache
            = new ConcurrentDictionary<Type, Func<object, StateMachineObject>>();

        private static object UnwrapEnumerator(object enumerator)
        {
            if (enumerator == null) return null;
            var type = enumerator.GetType();

            var prop = type.GetProperty("InnerEnumerator", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null)
            {
                var inner = prop.GetValue(enumerator);
                if (inner != null)
                {
                    return UnwrapEnumerator(inner);
                }
            }

            if (type.FullName != null && type.FullName.Contains("CastOrConvertStream"))
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in fields)
                {
                    var fieldType = field.FieldType;
                    var isEnumerator = (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IAsyncEnumerator<>)) ||
                                       fieldType.GetInterfaces().Any(i => 
                                           i.IsGenericType && 
                                           i.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IAsyncEnumerator<>));
                    if (isEnumerator)
                    {
                        var inner = field.GetValue(enumerator);
                        if (inner != null)
                        {
                            return UnwrapEnumerator(inner);
                        }
                    }
                }
            }
            return enumerator;
        }

        public static void Hydrate(object enumerator, StateMachineObject stateObj)
        {
            if (enumerator == null) return;
            if (stateObj == null) return;

            enumerator = UnwrapEnumerator(enumerator);
            var type = enumerator.GetType();
            var hydrator = _hydratorCache.GetOrAdd(type, BuildHydratorDelegate);
            hydrator(enumerator, stateObj);
        }

        public static StateMachineObject Dehydrate(object enumerator)
        {
            if (enumerator == null) return null;

            enumerator = UnwrapEnumerator(enumerator);
            var type = enumerator.GetType();
            var dehydrator = _dehydratorCache.GetOrAdd(type, BuildDehydratorDelegate);
            return dehydrator(enumerator);
        }


        public static string GetCleanFieldName(string fieldName)
        {
            if (fieldName.StartsWith('<') && fieldName.Contains(">5__"))
            {
                int endIdx = fieldName.IndexOf('>');
                if (endIdx > 1)
                {
                    return fieldName.Substring(1, endIdx - 1);
                }
            }
            return fieldName;
        }

        public static bool IsLocalField(FieldInfo field)
        {
            bool isLiftedOrSynthesized = field.Name.Contains(CompilerConstants.LiftedLocalMarker)
                                      || field.Name.Contains(CompilerConstants.SynthesizedLocalMarker)
                                      || field.Name.Contains(CompilerConstants.LegacyLocalWrapMarker);

            bool isSpecialInternal = field.Name == CompilerConstants.StateFieldName
                                  || field.Name.EndsWith(CompilerConstants.CallerSuffix);

            return isLiftedOrSynthesized && !isSpecialInternal;
        }

        private static Action<object, StateMachineObject> BuildHydratorDelegate(Type enumeratorType)
        {
            var enumeratorParam = Expression.Parameter(typeof(object), "enumerator");
            var stateParam = Expression.Parameter(typeof(StateMachineObject), "stateObj");

            var typedEnumerator = Expression.Convert(enumeratorParam, enumeratorType);
            var assignments = new List<Expression>();
            var localVariables = new List<ParameterExpression>();

            var fields = enumeratorType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var stateIndexProp = Expression.Property(stateParam, nameof(StateMachineObject.StateIndex));
            var instanceProp = Expression.Property(stateParam, nameof(StateMachineObject.Instance));

            var dictTryGetValueMethod = typeof(Dictionary<string, object>).GetMethod("TryGetValue", new[] { typeof(string), typeof(object).MakeByRefType() });

            // 1. Assign State Index
            var stateField = fields.FirstOrDefault(f => f.Name == CompilerConstants.StateFieldName);
            if (stateField != null)
            {
                assignments.Add(Expression.Assign(Expression.Field(typedEnumerator, stateField), stateIndexProp));
            }

            // 2. Assign Instance ('this' pointer)
            var thisField = fields.FirstOrDefault(f => f.Name.EndsWith(CompilerConstants.CallerSuffix, StringComparison.Ordinal));
            if (thisField != null)
            {
                var typedInstance = Expression.Convert(instanceProp, thisField.FieldType);
                assignments.Add(Expression.IfThen(
                    Expression.NotEqual(instanceProp, Expression.Constant(null, typeof(object))),
                    Expression.Assign(Expression.Field(typedEnumerator, thisField), typedInstance)
                ));
            }

            var block = Expression.Block(localVariables, assignments);
            var lambda = Expression.Lambda<Action<object, StateMachineObject>>(block, enumeratorParam, stateParam);

            return lambda.CompileFast();
        }

        private static Func<object, StateMachineObject> BuildDehydratorDelegate(Type enumeratorType)
        {
            var enumeratorParam = Expression.Parameter(typeof(object), "enumerator");
            var typedEnumerator = Expression.Convert(enumeratorParam, enumeratorType);
            var fields = enumeratorType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var stateVar = Expression.Variable(typeof(StateMachineObject), "stateObj");
            var assignments = new List<Expression>();

            assignments.Add(Expression.Assign(stateVar, Expression.New(typeof(StateMachineObject))));

            // 1. Extract State Index
            var stateField = fields.FirstOrDefault(f => f.Name == CompilerConstants.StateFieldName);
            if (stateField != null)
            {
                assignments.Add(Expression.Assign(
                    Expression.Property(stateVar, nameof(StateMachineObject.StateIndex)),
                    Expression.Field(typedEnumerator, stateField)
                ));
            }

            // 2. Extract Instance ('this' pointer)
            var thisField = fields.FirstOrDefault(f => f.Name.EndsWith(CompilerConstants.CallerSuffix, StringComparison.Ordinal));
            if (thisField != null)
            {
                assignments.Add(Expression.Assign(
                    Expression.Property(stateVar, nameof(StateMachineObject.Instance)),
                    Expression.Convert(Expression.Field(typedEnumerator, thisField), typeof(object))
                ));
            }

            assignments.Add(stateVar); // Return value

            var block = Expression.Block(new[] { stateVar }, assignments);
            var lambda = Expression.Lambda<Func<object, StateMachineObject>>(block, enumeratorParam);

            return lambda.CompileFast();
        }
    }
}
