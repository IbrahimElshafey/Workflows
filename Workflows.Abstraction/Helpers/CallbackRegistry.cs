using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Workflows.Abstraction.Helpers;

public class CallbackRegistry
{
    // The registry now directly holds our unified Array-based invoker
    private readonly ConcurrentDictionary<string, Func<object?, object[], ValueTask>> _compiledRegistry = new();

    public void Restore(string key, string declaringTypeName, string methodName, string[]? parameterTypeNames = null)
    {
        Type? targetType = ResolveType(declaringTypeName)
            ?? throw new TypeLoadException($"Could not load type: '{declaringTypeName}'.");

        // Convert string names to Type objects for overload resolution
        Type[]? parameterTypes = null;
        if (parameterTypeNames != null)
        {
            parameterTypes = parameterTypeNames.Select(typeName =>
                ResolveType(typeName) ?? throw new TypeLoadException($"Could not resolve parameter type: {typeName}")
            ).ToArray();
        }

        MethodInfo? method;
        try
        {
            if (parameterTypes != null)
            {
                method = targetType.GetMethod(methodName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static,
                    null,
                    parameterTypes,
                    null);
            }
            else
            {
                method = targetType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            }
        }
        catch (AmbiguousMatchException)
        {
            throw new AmbiguousMatchException($"Method '{methodName}' on type '{declaringTypeName}' has overloads. You must provide 'parameterTypeNames'.");
        }

        if (method == null)
            throw new MissingMethodException($"Method '{methodName}' not found on type '{declaringTypeName}'.");

        RegisterCore(key, method, null);
    }

    public void Register(string key, MethodInfo method) => RegisterCore(key, method, null);

    public void Register(string key, Delegate del) => RegisterCore(key, del.Method, del.Target);

    private void RegisterCore(string key, MethodInfo method, object? knownTarget)
    {
        ParameterInfo[] methodParameters = method.GetParameters();

        // 1. Explicitly define two inputs for the lambda: the instance, and the arguments array
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var argsParam = Expression.Parameter(typeof(object[]), "args");

        Expression? targetExpression = null;

        // 2. Resolve the Target
        if (method.IsStatic)
        {
            // Static method: Ignore the passed 'instanceParam' entirely
            targetExpression = null;
        }
        else if (knownTarget != null)
        {
            // Captured delegate: Bake the known target in. Ignore the 'instanceParam'.
            targetExpression = Expression.Constant(knownTarget);
        }
        else
        {
            // Open instance method: We MUST cast and use the 'instanceParam' passed by the Runner
            targetExpression = Expression.Convert(instanceParam, method.DeclaringType!);
        }

        // 3. Resolve Arguments (Always mapping 1:1 from the argsParam array)
        Expression[] arrayArgumentExpressions = new Expression[methodParameters.Length];
        for (int i = 0; i < methodParameters.Length; i++)
        {
            Expression arrayAccess = Expression.ArrayIndex(argsParam, Expression.Constant(i));
            arrayArgumentExpressions[i] = Expression.Convert(arrayAccess, methodParameters[i].ParameterType);
        }

        // 4. Build the call and wrap it in a ValueTask
        Expression callExpression = Expression.Call(targetExpression, method, arrayArgumentExpressions);
        Expression bodyExpression = WrapInValueTask(callExpression, method.ReturnType);

        // 5. Compile directly to: Func<object?, object[], ValueTask>
        var lambda = Expression.Lambda<Func<object?, object[], ValueTask>>(bodyExpression, instanceParam, argsParam);
        var compiledDelegate = lambda.Compile();

        _compiledRegistry.AddOrUpdate(key, compiledDelegate, (_, _) => compiledDelegate);
    }

    private Expression WrapInValueTask(Expression callExpression, Type returnType)
    {
        if (returnType == typeof(ValueTask)) return callExpression;

        if (typeof(Task).IsAssignableFrom(returnType))
        {
            var valueTaskConstructor = typeof(ValueTask).GetConstructor(new[] { typeof(Task) })!;
            Expression taskExpression = Expression.Convert(callExpression, typeof(Task));
            return Expression.New(valueTaskConstructor, taskExpression);
        }

        var completedValueTaskConstant = Expression.Constant(default(ValueTask));
        return Expression.Block(callExpression, completedValueTaskConstant);
    }

    private Type? ResolveType(string typeName)
    {
        Type? type = Type.GetType(typeName);
        if (type != null) return type;

        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(typeName))
            .FirstOrDefault(t => t != null);
    }

    // ========================================================================
    // EXECUTION ENGINE
    // ========================================================================

    // The single, unified execution point.
    public ValueTask ExecuteAsync(string key, object? instance, params object[] args)
    {
        if (_compiledRegistry.TryGetValue(key, out var callback))
        {
            return callback(instance, args);
        }
        throw new KeyNotFoundException($"No callback registered under key '{key}'.");
    }
}