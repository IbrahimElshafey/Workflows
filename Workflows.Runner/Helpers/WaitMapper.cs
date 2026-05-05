using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Workflows.Abstraction.DTOs;
using Workflows.Definition;

namespace Workflows.Runner.Helpers
{
    internal static class WaitMapper
    {
        public static SubWorkflowWaitDto MapToDto(this SubWorkflowWait waitsGroup)
        {
            throw new NotImplementedException();
        }
        public static TimeWaitDto MapToDto(this TimeWait waitsGroup)
        {
            throw new NotImplementedException();
        }
        public static GroupWaitDto MapToDto(this GroupWait waitsGroup)
        {
            throw new NotImplementedException();
        }
        public static CommandWaitDto MapToDto<TCommand, TResult>(this CommandWait<TCommand, TResult> commandWait)
        {
            throw new NotImplementedException();
        }
        public static SignalWaitDto MapToDto<SignalData>(this SignalData signalData)
        {
            throw new NotImplementedException();
        }
        private static void CacheClosureIfAny(object? actionTarget, Wait wait)
        {
            var WorkflowContainer = wait.WorkflowContainer;
            if (actionTarget == null || WorkflowContainer?.Variables == null)
                return;

            var closureType = actionTarget.GetType();
            if (!closureType.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal))
                return;

            var key = closureType.FullName ?? closureType.Name;

            // The indexer is O(1) and safely acts as an AddOrUpdate.
            // It prevents duplicate key exceptions while ensuring you have the latest closure state.
            WorkflowContainer.Variables[key] = actionTarget;
        }

        private static object? TryGetClosureFromExpression(Expression? expr)
        {
            if (expr == null) return null;

            // Stack accepts nullable Expressions so we can blindly push properties 
            // and let the null-check at the top of the loop handle it safely.
            var stack = new Stack<Expression?>();
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

                // Unroll the tree
                if (current is MemberExpression member)
                {
                    // MINOR: Removed '!' - safely handles static members which have null expressions
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
                // --- GAP FIXES: Added missing expression shapes ---
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
                        // Closures can hide inside object initializers: new Foo { Id = closure.Id }
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