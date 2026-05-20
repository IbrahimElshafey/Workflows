using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Workflows.Abstraction.Helpers;

public class CallbackRegistry
{
    private readonly Dictionary<string, Func<object[], ValueTask>> _compiledRegistry = new();
    public void Restore(string key, string declaringTypeName, string methodName)
    {
        // 1. Resolve the string back into a real .NET Type via Reflection
        Type? targetType = Type.GetType(declaringTypeName);
        if (targetType == null)
        {
            throw new TypeLoadException($"Could not load type: {declaringTypeName}");
        }

        // 2. Find the exact method on that type via Reflection
        // (Adjust BindingFlags if you want to allow private or static methods)
        MethodInfo? method = targetType.GetMethod(methodName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        if (method == null)
        {
            throw new MissingMethodException($"Method '{methodName}' not found on type '{declaringTypeName}'.");
        }

        // 3. Hand the reflected MethodInfo directly over to your ultra-fast compiler
        Register(key, method);
    }
    public void Register(string key, MethodInfo method)
    {
        // Accept a single unified array: inputs
        var inputsParam = Expression.Parameter(typeof(object[]), "inputs");

        Expression targetExpression;
        int argumentStartIndex;

        // 1. Determine index alignment based on whether the method is static or instance
        if (method.IsStatic)
        {
            targetExpression = null!;
            argumentStartIndex = 0;
        }
        else
        {
            Expression rawInstance = Expression.ArrayIndex(inputsParam, Expression.Constant(0));
            targetExpression = Expression.Convert(rawInstance, method.DeclaringType!);
            argumentStartIndex = 1;
        }

        // 2. Map and cast parameters using the correct offset index
        ParameterInfo[] methodParameters = method.GetParameters();
        Expression[] argumentExpressions = new Expression[methodParameters.Length];

        for (int i = 0; i < methodParameters.Length; i++)
        {
            int arrayIndex = argumentStartIndex + i;
            Expression arrayAccess = Expression.ArrayIndex(inputsParam, Expression.Constant(arrayIndex));
            argumentExpressions[i] = Expression.Convert(arrayAccess, methodParameters[i].ParameterType);
        }

        // 3. Build the call expression
        Expression callExpression = Expression.Call(targetExpression, method, argumentExpressions);
        Expression bodyExpression;

        // 4. Optimize based on return type (Streamlined)
        if (method.ReturnType == typeof(ValueTask))
        {
            // Direct ValueTask return: No wrapping needed
            bodyExpression = callExpression;
        }
        else if (typeof(Task).IsAssignableFrom(method.ReturnType))
        {
            // Handle legacy Task / Task<T>: Convert to Task, then construct a new ValueTask from it
            var valueTaskConstructor = typeof(ValueTask).GetConstructor(new[] { typeof(Task) })!;
            Expression taskExpression = Expression.Convert(callExpression, typeof(Task));

            bodyExpression = Expression.New(valueTaskConstructor, taskExpression);
        }
        else
        {
            // Synchronous methods: Execute, then yield a zero-allocation, default ValueTask struct
            var completedValueTaskConstant = Expression.Constant(default(ValueTask));

            bodyExpression = Expression.Block(
                callExpression,
                completedValueTaskConstant
            );
        }

        // 5. Compile into the unified Func<object[], ValueTask> signature
        var lambda = Expression.Lambda<Func<object[], ValueTask>>(bodyExpression, inputsParam);
        _compiledRegistry[key] = lambda.Compile();
    }
    public void Register(string key, Delegate del)
    {
        Register(key, del.Method);
    }
    public async ValueTask ExecuteAsync(string key, params object[] inputs)
    {
        if (_compiledRegistry.TryGetValue(key, out var compiledFunc))
        {
            await compiledFunc(inputs);
        }
    }
}