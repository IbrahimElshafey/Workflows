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
        // Action<enumerator, instance, closuresDict, localsDict, state>
        private static readonly ConcurrentDictionary<Type, Action<object, object, Dictionary<string, object>, Dictionary<string, object>, int>> _hydratorCache
                = new ConcurrentDictionary<Type, Action<object, object, Dictionary<string, object>, Dictionary<string, object>, int>>();

        // Cache: Func<enumerator, MachineState>
        private static readonly ConcurrentDictionary<Type, Func<object, MachineState>> _dehydratorCache
            = new ConcurrentDictionary<Type, Func<object, MachineState>>();

        internal async Task<AdvancerResult> RunAsync(
            IAsyncEnumerable<Wait> workflow,
            WorkflowContainer instance,
            Dictionary<string, object> closures,
            Dictionary<string, object> locals,
            int stateAfterWait,
            CancellationToken cancellationToken = default)
        {
            var enumerator = workflow.GetAsyncEnumerator(cancellationToken);
            var enumeratorType = enumerator.GetType();

            // 1. Hydrate 
            var hydrator = _hydratorCache.GetOrAdd(enumeratorType, BuildHydratorDelegate);
            hydrator(enumerator, instance, closures, locals, stateAfterWait);

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
                    State = newState.State,
                    Closures = newState.Closures,
                    Locals = newState.Locals
                };
            }

            return null; // Completed natively
        }

        private static Action<object, object, Dictionary<string, object>, Dictionary<string, object>, int> BuildHydratorDelegate(Type enumeratorType)
        {
            var enumeratorParam = Expression.Parameter(typeof(object), "enumerator");
            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var closuresParam = Expression.Parameter(typeof(Dictionary<string, object>), "closures");
            var localsParam = Expression.Parameter(typeof(Dictionary<string, object>), "locals");
            var stateParam = Expression.Parameter(typeof(int), "state");

            var typedEnumerator = Expression.Convert(enumeratorParam, enumeratorType);
            var assignments = new List<Expression>();
            var localVariables = new List<ParameterExpression>();

            var fields = enumeratorType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var dictTryGetValueMethod = typeof(Dictionary<string, object>).GetMethod("TryGetValue", new[] { typeof(string), typeof(object).MakeByRefType() });

            // 1. Assign State
            var stateField = fields.FirstOrDefault(f => f.Name == CompilerConstants.StateFieldName);
            if (stateField != null)
            {
                assignments.Add(Expression.Assign(Expression.Field(typedEnumerator, stateField), stateParam));
            }

            // 2. Assign Instance ('this' pointer)
            var thisField = fields.FirstOrDefault(f => f.Name.EndsWith(CompilerConstants.CallerSuffix, StringComparison.Ordinal));
            if (thisField != null)
            {
                var typedInstance = Expression.Convert(instanceParam, thisField.FieldType);
                assignments.Add(Expression.Assign(Expression.Field(typedEnumerator, thisField), typedInstance));
            }

            // 3. Hydrate Closures
            var closureFields = fields.Where(IsClosureField).ToList();
            if (closureFields.Any())
            {
                var closuresNotNull = Expression.NotEqual(closuresParam, Expression.Constant(null, typeof(Dictionary<string, object>)));
                var closureAssignments = new List<Expression>();

                foreach (var cf in closureFields)
                {
                    var outVar = Expression.Variable(typeof(object), cf.Name + "_out");
                    localVariables.Add(outVar);

                    var tryGetCall = Expression.Call(closuresParam, dictTryGetValueMethod, Expression.Constant(cf.Name), outVar);
                    var assignField = Expression.Assign(Expression.Field(typedEnumerator, cf), Expression.Convert(outVar, cf.FieldType));

                    closureAssignments.Add(Expression.IfThen(tryGetCall, assignField));
                }
                assignments.Add(Expression.IfThen(closuresNotNull, Expression.Block(closureAssignments)));
            }

            // 4. Hydrate Locals
            var localFields = fields.Where(f => IsLocalField(f) && !closureFields.Contains(f)).ToList();
            if (localFields.Any())
            {
                var localsNotNull = Expression.NotEqual(localsParam, Expression.Constant(null, typeof(Dictionary<string, object>)));
                var localAssignments = new List<Expression>();

                foreach (var lf in localFields)
                {
                    var outVar = Expression.Variable(typeof(object), lf.Name + "_out");
                    localVariables.Add(outVar);

                    var tryGetCall = Expression.Call(localsParam, dictTryGetValueMethod, Expression.Constant(lf.Name), outVar);
                    var assignField = Expression.Assign(Expression.Field(typedEnumerator, lf), Expression.Convert(outVar, lf.FieldType));

                    localAssignments.Add(Expression.IfThen(tryGetCall, assignField));
                }
                assignments.Add(Expression.IfThen(localsNotNull, Expression.Block(localAssignments)));
            }

            var block = Expression.Block(localVariables, assignments);
            var lambda = Expression.Lambda<Action<object, object, Dictionary<string, object>, Dictionary<string, object>, int>>(
                block, enumeratorParam, instanceParam, closuresParam, localsParam, stateParam);

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
                    Expression.Property(stateVar, nameof(MachineState.State)),
                    Expression.Field(typedEnumerator, stateField)
                ));
            }

            var dictAddMethod = typeof(Dictionary<string, object>).GetMethod("Add", new[] { typeof(string), typeof(object) });

            // 2. Extract Closures
            var closureFields = fields.Where(IsClosureField).ToList();
            foreach (var cf in closureFields)
            {
                var fieldAccess = Expression.Field(typedEnumerator, cf);
                var castedField = Expression.Convert(fieldAccess, typeof(object));
                var isNotNull = Expression.NotEqual(castedField, Expression.Constant(null, typeof(object)));

                var addCall = Expression.Call(Expression.Property(stateVar, nameof(MachineState.Closures)), dictAddMethod, Expression.Constant(cf.Name), castedField);
                assignments.Add(Expression.IfThen(isNotNull, addCall));
            }

            // 3. Extract Locals
            var localFields = fields.Where(f => IsLocalField(f) && !closureFields.Contains(f)).ToList();
            foreach (var lf in localFields)
            {
                var fieldAccess = Expression.Field(typedEnumerator, lf);
                var castedField = Expression.Convert(fieldAccess, typeof(object));
                var isNotNull = Expression.NotEqual(castedField, Expression.Constant(null, typeof(object)));

                var addCall = Expression.Call(Expression.Property(stateVar, nameof(MachineState.Locals)), dictAddMethod, Expression.Constant(lf.Name), castedField);
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
            // A local is something that has the lifted marker, the synthesized marker, 
            // or the legacy wrap marker.
            bool isLiftedOrSynthesized = field.Name.Contains(CompilerConstants.LiftedLocalMarker)
                                      || field.Name.Contains(CompilerConstants.SynthesizedLocalMarker)
                                      || field.Name.Contains(CompilerConstants.LegacyLocalWrapMarker);

            // Ensure we don't accidentally grab the state tracker or the class instance
            bool isSpecialInternal = field.Name == CompilerConstants.StateFieldName
                                  || field.Name.EndsWith(CompilerConstants.CallerSuffix);

            return isLiftedOrSynthesized && !isSpecialInternal;
        }
    }
}