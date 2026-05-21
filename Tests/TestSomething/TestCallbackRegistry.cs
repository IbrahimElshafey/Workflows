using System;
using System.Threading.Tasks;
using Workflows.Abstraction.Helpers;
public class LogicService
{
    // 1. Static method
    public static ValueTask StaticHello(string name)
    {
        Console.WriteLine($"[Static] Hello, {name}!");
        return ValueTask.CompletedTask;
    }

    // 2. Instance method
    public ValueTask InstanceGreet(string name, int count)
    {
        Console.WriteLine($"[Instance] Hello {name}, this is call #{count}.");
        return ValueTask.CompletedTask;
    }
}
class TestCallbackRegistry
{
    public static async Task Run()
    {
        var registry = new CallbackRegistry();
        var service = new LogicService();

        // --- TestCallbackRegistry 1: Static Method ---
        registry.Register("static_greet", typeof(LogicService).GetMethod(nameof(LogicService.StaticHello))!);
        await registry.ExecuteAsync("static_greet", service, "Gemini");

        // --- TestCallbackRegistry 2: Instance Delegate ---
        // We pass the service instance, registry handles the binding via .Target
        registry.Register("instance_greet", (Func<string, int, ValueTask>)service.InstanceGreet);
        await registry.ExecuteAsync("instance_greet", service, "User", 1);

        // --- TestCallbackRegistry 3: Restore (Reflection) ---
        // Restoring the same method via string names
        registry.Restore("restored_greet", typeof(LogicService).AssemblyQualifiedName!, nameof(LogicService.InstanceGreet));
        await registry.ExecuteAsync("restored_greet", service, "ReflectionUser", 2);

        // --- TestCallbackRegistry 4: Concurrency (The ConcurrentDictionary check) ---
        Console.WriteLine("\n--- Running Concurrent TestCallbackRegistry ---");
        await Parallel.ForAsync(0, 50, async (i, _) =>
        {
            await registry.ExecuteAsync("static_greet", null, $"Thread-{i}");
        });

        Console.WriteLine("\nAll tests finished successfully.");
    }
}