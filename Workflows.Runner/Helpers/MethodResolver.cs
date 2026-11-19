using System;
using System.Reflection;
using FastExpressionCompiler;
using Workflows.Definition;

namespace Workflows.Runner.Helpers
{
    internal static class MethodResolver
    {
        public static MethodInfo? ResolveMethod(string methodFullPath)
        {
            if (string.IsNullOrWhiteSpace(methodFullPath))
                return null;

            int lastDot = methodFullPath.LastIndexOf('.');
            if (lastDot == -1)
                return null;

            string typeName = methodFullPath.Substring(0, lastDot);
            string methodName = methodFullPath.Substring(lastDot + 1);

            Type? type = Type.GetType(typeName);
            if (type == null)
            {
                // Fallback: search loaded assemblies for the type (e.g. if assembly is not referenced directly)
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(typeName);
                    if (type != null)
                        break;
                }
            }

            if (type == null)
                return null;

            return type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        }

        public static Action<object, object, object> BuildMethodInvoker(MethodInfo method)
        {
            var declaringType = method.DeclaringType;
            if (declaringType == null) return null!;

            var parameters = method.GetParameters();

            if (method.IsStatic)
            {
                var signalParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "signal");
                var stateParam  = System.Linq.Expressions.Expression.Parameter(typeof(object), "state");
                var instanceParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "instance");

                System.Linq.Expressions.Expression call;
                if (parameters.Length == 0)
                {
                    call = System.Linq.Expressions.Expression.Call(method);
                }
                else if (parameters.Length == 1)
                {
                    call = System.Linq.Expressions.Expression.Call(method, System.Linq.Expressions.Expression.Convert(signalParam, parameters[0].ParameterType));
                }
                else
                {
                    var convertStateMethod = typeof(Workflows.Runner.Pipeline.StateConverter).GetMethod(
                        nameof(Workflows.Runner.Pipeline.StateConverter.ConvertState),
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    call = System.Linq.Expressions.Expression.Call(method,
                        System.Linq.Expressions.Expression.Convert(signalParam, parameters[0].ParameterType),
                        System.Linq.Expressions.Expression.Convert(
                            System.Linq.Expressions.Expression.Call(convertStateMethod!, stateParam, System.Linq.Expressions.Expression.Constant(parameters[1].ParameterType)),
                            parameters[1].ParameterType));
                }

                return System.Linq.Expressions.Expression.Lambda<Action<object, object, object>>(call, instanceParam, signalParam, stateParam)
                                 .CompileFast();
            }

            if (typeof(WorkflowContainer).IsAssignableFrom(declaringType))
            {
                var instanceParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "instance");
                var signalParam   = System.Linq.Expressions.Expression.Parameter(typeof(object), "signal");
                var stateParam    = System.Linq.Expressions.Expression.Parameter(typeof(object), "state");

                System.Linq.Expressions.Expression targetExpr = System.Linq.Expressions.Expression.Convert(instanceParam, declaringType);
                System.Linq.Expressions.Expression call;
                if (parameters.Length == 0)
                {
                    call = System.Linq.Expressions.Expression.Call(targetExpr, method);
                }
                else if (parameters.Length == 1)
                {
                    call = System.Linq.Expressions.Expression.Call(targetExpr, method, System.Linq.Expressions.Expression.Convert(signalParam, parameters[0].ParameterType));
                }
                else
                {
                    var convertStateMethod = typeof(Workflows.Runner.Pipeline.StateConverter).GetMethod(
                        nameof(Workflows.Runner.Pipeline.StateConverter.ConvertState),
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    call = System.Linq.Expressions.Expression.Call(targetExpr, method,
                        System.Linq.Expressions.Expression.Convert(signalParam, parameters[0].ParameterType),
                        System.Linq.Expressions.Expression.Convert(
                            System.Linq.Expressions.Expression.Call(convertStateMethod!, stateParam, System.Linq.Expressions.Expression.Constant(parameters[1].ParameterType)),
                            parameters[1].ParameterType));
                }

                return System.Linq.Expressions.Expression.Lambda<Action<object, object, object>>(call, instanceParam, signalParam, stateParam)
                                 .CompileFast();
            }

            // Fallback: DeclaringType is compiler-generated (e.g. State Machine or DisplayClass)
            return (instance, signal, state) =>
            {
                var targetObj = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(declaringType);
                
                // Find and set the WorkflowContainer field
                var containerField = declaringType.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)
                    || declaringType.Name.Contains("<")
                    ? System.Linq.Enumerable.FirstOrDefault(declaringType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                        f => typeof(WorkflowContainer).IsAssignableFrom(f.FieldType))
                    : null;

                if (containerField != null)
                {
                    containerField.SetValue(targetObj, instance);
                }

                var args = new object[parameters.Length];
                if (parameters.Length > 0)
                {
                    args[0] = Workflows.Runner.Pipeline.StateConverter.ConvertState(signal, parameters[0].ParameterType);
                }
                if (parameters.Length > 1)
                {
                    args[1] = Workflows.Runner.Pipeline.StateConverter.ConvertState(state, parameters[1].ParameterType);
                }

                method.Invoke(targetObj, args);
            };
        }
    }
}
