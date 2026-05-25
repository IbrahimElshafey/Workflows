using System;
using System.Linq.Expressions;

class Program
{
    static void Main(string[] args)
    {
        Type serializerType = null;
        foreach (var ass in AppDomain.CurrentDomain.GetAssemblies())
        {
            serializerType = ass.GetType("Workflows.Shared.Serialization.ExpressionSerializer");
            if (serializerType != null)
                break;
        }

        if (serializerType == null)
        {
            // Force load assembly
            var sharedAssembly = System.Reflection.Assembly.Load("Workflows.Shared");
            serializerType = sharedAssembly.GetType("Workflows.Shared.Serialization.ExpressionSerializer");
        }

        if (serializerType == null)
        {
            Console.WriteLine("Could not find ExpressionSerializer type!");
            return;
        }

        var serializer = (Workflows.Abstraction.Helpers.IExpressionSerializer)Activator.CreateInstance(serializerType);
        Expression<Func<int, int>> expr = x => x + 1;
        Console.WriteLine($"Original Expression: {expr}");
        
        var serialized = serializer.Serialize(expr);
        Console.WriteLine($"Serialized: {serialized}");
        Console.WriteLine($"Serialized Type: {serialized?.GetType().FullName}");
        
        var deserialized = serializer.Deserialize(serialized);
        Console.WriteLine($"Deserialized: {deserialized}");
    }
}
