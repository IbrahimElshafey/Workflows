using FastExpressionCompiler;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Workflows.Definition;
using Workflows.Runner.DataObjects;
using Workflows.Runner.Helpers;

namespace Workflows.Runner
{

    internal class StateMachineAdvancer
    {
        // Cache of compiled hydrator delegates.
        // Action<enumerator, machineState>
        private static readonly ConcurrentDictionary<Type, Action<object, MachineState>> _hydratorCache
                = new ConcurrentDictionary<Type, Action<object, MachineState>>();

        // Cache: Func<enumerator, MachineState>
        private static readonly ConcurrentDictionary<Type, Func<object, MachineState>> _dehydratorCache
            = new ConcurrentDictionary<Type, Func<object, MachineState>>();

        internal async Task<AdvancerResult> RunAsync(
            IAsyncEnumerable<Wait> workflow,
            MachineState previousState,
            CancellationToken cancellationToken = default)
        {
            var enumerator = workflow.GetAsyncEnumerator(cancellationToken);
            var enumeratorType = enumerator.GetType();

            // 1. Hydrate 
            var hydrator = _hydratorCache.GetOrAdd(enumeratorType, BuildHydratorDelegate);
            hydrator(enumerator, previousState);

            // 2. Advance
            if (await enumerator.MoveNextAsync())
            {
                var nextWait = enumerator.Current;

                // 3. Dehydrate (Capture all active scopes)
                var dehydrator = _dehydratorCache.GetOrAdd(enumeratorType, BuildDehydratorDelegate);
                var newState = dehydrator(enumerator);

                // 4. Return the clean package
                return new AdvancerResult
                {
                    Wait = nextWait,
                    State = newState
                };
            }

            return null; // Completed natively
        }

        private static Action<object, MachineState> BuildHydratorDelegate(Type enumeratorType)
        {
            var enumeratorParam = Expression.Parameter(typeof(object), "enumerator");
            var stateParam = Expression.Parameter(typeof(MachineState), "stateObj");

            var typedEnumerator = Expression.Convert(enumeratorParam, enumeratorType);
            var assignments = new List<Expression>();
            var localVariables = new List<ParameterExpression>();

            var fields = enumeratorType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            // Extract property accessors for MachineState
            var stateIndexProp = Expression.Property(stateParam, nameof(MachineState.StateIndex));
            var instanceProp = Expression.Property(stateParam, nameof(MachineState.Instance));
            var variablesProp = Expression.Property(stateParam, nameof(MachineState.Variables));

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
                assignments.Add(Expression.Assign(Expression.Field(typedEnumerator, thisField), typedInstance));
            }

            // 3. Hydrate Variables (Locals + Closures combined)
            var stateFieldsToHydrate = fields.Where(f => IsClosureField(f) || IsLocalField(f)).ToList();
            if (stateFieldsToHydrate.Any())
            {
                var variablesNotNull = Expression.NotEqual(variablesProp, Expression.Constant(null, typeof(Dictionary<string, object>)));
                var variableAssignments = new List<Expression>();

                foreach (var f in stateFieldsToHydrate)
                {
                    var outVar = Expression.Variable(typeof(object), f.Name + "_out");
                    localVariables.Add(outVar);

                    var tryGetCall = Expression.Call(variablesProp, dictTryGetValueMethod, Expression.Constant(f.Name), outVar);
                    var assignField = Expression.Assign(Expression.Field(typedEnumerator, f), Expression.Convert(outVar, f.FieldType));

                    variableAssignments.Add(Expression.IfThen(tryGetCall, assignField));
                }
                assignments.Add(Expression.IfThen(variablesNotNull, Expression.Block(variableAssignments)));
            }

            var block = Expression.Block(localVariables, assignments);
            var lambda = Expression.Lambda<Action<object, MachineState>>(block, enumeratorParam, stateParam);

            return lambda.CompileFast();
        }

        private static Func<object, MachineState> BuildDehydratorDelegate(Type enumeratorType)
        {
            var enumeratorParam = Expression.Parameter(typeof(object), "enumerator");
            var typedEnumerator = Expression.Convert(enumeratorParam, enumeratorType);
            var fields = enumeratorType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var stateVar = Expression.Variable(typeof(MachineState), "stateObj");
            var assignments = new List<Expression>();

            assignments.Add(Expression.Assign(stateVar, Expression.New(typeof(MachineState))));

            // 1. Extract State Index
            var stateField = fields.FirstOrDefault(f => f.Name == CompilerConstants.StateFieldName);
            if (stateField != null)
            {
                assignments.Add(Expression.Assign(
                    Expression.Property(stateVar, nameof(MachineState.StateIndex)),
                    Expression.Field(typedEnumerator, stateField)
                ));
            }

            // 2. Extract Instance ('this' pointer)
            var thisField = fields.FirstOrDefault(f => f.Name.EndsWith(CompilerConstants.CallerSuffix, StringComparison.Ordinal));
            if (thisField != null)
            {
                assignments.Add(Expression.Assign(
                    Expression.Property(stateVar, nameof(MachineState.Instance)),
                    Expression.Convert(Expression.Field(typedEnumerator, thisField), typeof(object))
                ));
            }

            // 3. Extract Variables (Locals + Closures combined)
            var dictAddMethod = typeof(Dictionary<string, object>).GetMethod("Add", new[] { typeof(string), typeof(object) });
            var variablesProp = Expression.Property(stateVar, nameof(MachineState.Variables));

            var stateFieldsToDehydrate = fields.Where(f => IsClosureField(f) || IsLocalField(f)).ToList();
            foreach (var f in stateFieldsToDehydrate)
            {
                var fieldAccess = Expression.Field(typedEnumerator, f);
                var castedField = Expression.Convert(fieldAccess, typeof(object));
                var isNotNull = Expression.NotEqual(castedField, Expression.Constant(null, typeof(object)));

                var addCall = Expression.Call(variablesProp, dictAddMethod, Expression.Constant(f.Name), castedField);
                assignments.Add(Expression.IfThen(isNotNull, addCall));
            }

            assignments.Add(stateVar); // Return value

            var block = Expression.Block(new[] { stateVar }, assignments);
            var lambda = Expression.Lambda<Func<object, MachineState>>(block, enumeratorParam);

            return lambda.CompileFast();
        }

        private static bool IsClosureField(FieldInfo field)
        {
            return field.Name.StartsWith(CompilerConstants.ClosureFieldPrefix, StringComparison.Ordinal)
                || field.FieldType.Name.StartsWith(CompilerConstants.ClosurePrefix, StringComparison.Ordinal);
        }

        private static bool IsLocalField(FieldInfo field)
        {
            bool isLiftedOrSynthesized = field.Name.Contains(CompilerConstants.LiftedLocalMarker)
                                      || field.Name.Contains(CompilerConstants.SynthesizedLocalMarker)
                                      || field.Name.Contains(CompilerConstants.LegacyLocalWrapMarker);

            bool isSpecialInternal = field.Name == CompilerConstants.StateFieldName
                                  || field.Name.EndsWith(CompilerConstants.CallerSuffix);

            return isLiftedOrSynthesized && !isSpecialInternal;
        }
    }
}