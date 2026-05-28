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

        public static void Hydrate(object enumerator, StateMachineObject stateObj)
        {
            if (enumerator == null) return;
            if (stateObj == null) return;

            var type = enumerator.GetType();
            var hydrator = _hydratorCache.GetOrAdd(type, BuildHydratorDelegate);
            hydrator(enumerator, stateObj);
        }

        public static StateMachineObject Dehydrate(object enumerator)
        {
            if (enumerator == null) return null;

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

            // 3. Hydrate state machine variables
            var stateFieldsToHydrate = fields.Where(IsLocalField).ToList();
            if (stateFieldsToHydrate.Any())
            {
                var convertStateMethod = typeof(Workflows.Runner.Pipeline.StateConverter).GetMethod(
                    nameof(Workflows.Runner.Pipeline.StateConverter.ConvertState),
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                var variableAssignments = new List<Expression>();

                foreach (var f in stateFieldsToHydrate)
                {
                    var cleanName = GetCleanFieldName(f.Name);
                    var outVar = Expression.Variable(typeof(object), cleanName + "_out");
                    localVariables.Add(outVar);

                    var tryGetCall = Expression.Call(stateParam, dictTryGetValueMethod, Expression.Constant(cleanName), outVar);
                    var convertedVal = Expression.Convert(
                        Expression.Call(convertStateMethod!, outVar, Expression.Constant(f.FieldType)),
                        f.FieldType);
                    var assignField = Expression.Assign(Expression.Field(typedEnumerator, f), convertedVal);

                    variableAssignments.Add(Expression.IfThen(tryGetCall, assignField));
                }
                assignments.Add(Expression.Block(variableAssignments));
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

            // 3. Extract variables
            var dictAddMethod = typeof(Dictionary<string, object>).GetMethod("Add", new[] { typeof(string), typeof(object) });

            var stateFieldsToDehydrate = fields.Where(IsLocalField).ToList();
            foreach (var f in stateFieldsToDehydrate)
            {
                var cleanName = GetCleanFieldName(f.Name);
                var fieldAccess = Expression.Field(typedEnumerator, f);
                var castedField = Expression.Convert(fieldAccess, typeof(object));
                var isNotNull = Expression.NotEqual(castedField, Expression.Constant(null, typeof(object)));

                var addCall = Expression.Call(stateVar, dictAddMethod, Expression.Constant(cleanName), castedField);
                assignments.Add(Expression.IfThen(isNotNull, addCall));
            }

            assignments.Add(stateVar); // Return value

            var block = Expression.Block(new[] { stateVar }, assignments);
            var lambda = Expression.Lambda<Func<object, StateMachineObject>>(block, enumeratorParam);

            return lambda.CompileFast();
        }
    }
}
