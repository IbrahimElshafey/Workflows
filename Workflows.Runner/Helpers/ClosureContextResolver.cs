using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Workflows.Definition;

namespace Workflows.Runner.Helpers
{
    internal sealed class ClosureContextResolver : IClosureContextResolver
    {
        public string CacheClosureIfAny(object actionTarget, Wait wait)
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

        //https://gemini.google.com/share/c821bbb7e6f4
        // Cache to prevent expensive Reflection calls
        private static readonly ConcurrentDictionary<Type, bool> _closureTypeCache = new();

        public object TryGetClosureFromExpression(Expression expr)
        {
            if (expr == null) return null;

            // Rent a conservatively large array to act as our stack (zero allocations)
            var stackArray = ArrayPool<Expression>.Shared.Rent(256);
            int head = 0;

            stackArray[head++] = expr;

            try
            {
                while (head > 0)
                {
                    // Pop the current item
                    var current = stackArray[--head];

                    // CRITICAL: Null out the array slot to prevent memory leaks in the pool
                    stackArray[head] = null;

                    if (current == null) continue;

                    switch (current)
                    {
                        case ConstantExpression constant when constant.Value != null:
                            if (_closureTypeCache.GetOrAdd(constant.Value.GetType(), IsClosureType))
                            {
                                return constant.Value;
                            }
                            break;

                        case MemberExpression member:
                            Push(ref stackArray, ref head, member.Expression);
                            break;

                        case BinaryExpression binary:
                            Push(ref stackArray, ref head, binary.Left);
                            Push(ref stackArray, ref head, binary.Right);
                            break;

                        case LambdaExpression lambda:
                            Push(ref stackArray, ref head, lambda.Body);
                            break;

                        case UnaryExpression unary:
                            Push(ref stackArray, ref head, unary.Operand);
                            break;

                        case MethodCallExpression call:
                            Push(ref stackArray, ref head, call.Object);
                            for (int i = 0; i < call.Arguments.Count; i++)
                                Push(ref stackArray, ref head, call.Arguments[i]);
                            break;

                        case ConditionalExpression conditional:
                            Push(ref stackArray, ref head, conditional.Test);
                            Push(ref stackArray, ref head, conditional.IfTrue);
                            Push(ref stackArray, ref head, conditional.IfFalse);
                            break;

                        case InvocationExpression invocation:
                            Push(ref stackArray, ref head, invocation.Expression);
                            for (int i = 0; i < invocation.Arguments.Count; i++)
                                Push(ref stackArray, ref head, invocation.Arguments[i]);
                            break;

                        case NewExpression newExpr:
                            for (int i = 0; i < newExpr.Arguments.Count; i++)
                                Push(ref stackArray, ref head, newExpr.Arguments[i]);
                            break;

                        case NewArrayExpression newArray:
                            for (int i = 0; i < newArray.Expressions.Count; i++)
                                Push(ref stackArray, ref head, newArray.Expressions[i]);
                            break;

                        case MemberInitExpression memberInit:
                            Push(ref stackArray, ref head, memberInit.NewExpression);
                            for (int i = 0; i < memberInit.Bindings.Count; i++)
                            {
                                if (memberInit.Bindings[i] is MemberAssignment assignment)
                                {
                                    Push(ref stackArray, ref head, assignment.Expression);
                                }
                            }
                            break;
                    }
                }
                return null;
            }
            finally
            {
                // Return the array to the pool. 
                // clearArray: false is safe because we manually nulled elements as we popped them.
                ArrayPool<Expression>.Shared.Return(stackArray, clearArray: false);
            }

            // Local static function for the reflection check
            static bool IsClosureType(Type t)
            {
                return t.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal) ||
                       t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
            }
        }

        // High-performance push helper that handles array resizing if the tree is exceptionally deep
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Push(ref Expression[] array, ref int head, Expression expr)
        {
            if (expr == null) return; // Don't bother pushing nulls

            if (head >= array.Length)
            {
                // Rent a larger array, copy existing items, and return the old one
                var newArray = ArrayPool<Expression>.Shared.Rent(array.Length * 2);
                Array.Copy(array, newArray, array.Length);

                // We must clear the old array here because we didn't get to null out the un-popped items
                ArrayPool<Expression>.Shared.Return(array, clearArray: true);
                array = newArray;
            }
            array[head++] = expr;
        }
    }
}
