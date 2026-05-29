# How SxS (Side-by-Side) Execution Works in .NET 10

## Executive Summary

This document provides an architectural overview of implementing isolated plugin loading and Side-by-Side (SxS) multi-version runtime execution using modern .NET primitives. It highlights the mechanics of `AssemblyLoadContext` (ALC), integration with Dependency Injection (DI) containers, risk areas concerning memory leaks, and strategic memory management techniques.

---

## 1. Core Mechanics of Side-by-Side Loading

In .NET 10, the runtime completely moves away from the legacy .NET Framework `AppDomain` boundaries. Dynamic assembly isolation and side-by-side loading of conflicting versions are achieved through two primary vectors:

### Default ALC Side-by-Side (Identity Mutation)

The Default `AssemblyLoadContext` determines type uniqueness based on the **Full Assembly Name (Identity)**, not the physical file name on disk. To load two versions of an assembly with identical namespaces and types side-by-side into the default context without runtime collision, the assemblies must be compiled with differing identities:

* **Differentiated Assembly Names:** Modifying the assembly metadata at compile time (e.g., `<AssemblyName>SomeName_V1</AssemblyName>` and `<AssemblyName>SomeName_V2</AssemblyName>`).
* **Differentiated Assembly Versions:** Compiling with unique version attributes (e.g., `1.0.0.0` vs `2.0.0.0`) combined with strong-name signing.

### Isolated AssemblyLoadContext (ALC)

For dynamic, pluggable infrastructures, creating a custom class derived from `AssemblyLoadContext(isCollectible: true)` is the industry standard. It isolates the dependency graph of individual modules in-process, eliminating the IPC serialization overhead of multi-process or microservice architectures.

---

## 2. Architecture: Merging ALC with Dependency Injection

To prevent host application degradation and allow dynamic unloading, dependency injection must follow a **Dual-Container (Parent-Child)** architectural pattern.

* **The Host (Parent) Container:** Registers shared, long-lived infrastructure services (e.g., `ILoggerFactory`, `IConfiguration`) and handles shared interfaces/contracts.
* **The Plugin (Child) Container:** Spun up dynamically per ALC instance. It inherits/clones base services from the host but registers its concrete implementations inside an isolated scope.
* **Type Forwarding Guardrail:** The custom ALC must return `null` inside its `Load(AssemblyName)` override when encountering shared contract assemblies. This forces runtime resolution back to the Default ALC, preventing type-cast exceptions (e.g., `InvalidCastException` when converting an isolated type to a shared interface).

---

## 3. High-Priority Architectural Concerns

When scaling side-by-side execution to support multiple co-existing versions of the same plugin module, the following friction points must be designed around:

### The "Cooperative Unloading" Trap

Unlike legacy AppDomains, modern ALC unloading is strictly **cooperative**. Calling `alc.Unload()` merely notifies the CLR to stop external loading and registers the context for a future Garbage Collection sweep. If a single strong reference to any object instantiated inside the ALC remains active, the entire context leaks permanently in memory ("Zombie Assemblies").

### Namespace & Type Ambiguity

If multi-version assemblies are loaded into the default context, identical namespaces will throw compile-time ambiguity errors. Developers must utilize **extern aliases** within the `.csproj` configuration to explicitly map type routing at compile time.

### Shared Static State Leakage

ALCs isolate type execution, not memory states of shared assemblies. If a plugin relies on a third-party library that alters a `static` property inside a shared base runtime assembly, changes made by `Version 1` will instantly bleed into and overwrite configurations used by `Version 2`.

---

## 4. Solving the "Host-Retention" Memory Leak

The most frequent vector for permanent memory leakage in plugin orchestration is the **GC Reference Chain** loop. If a host application maps plugin executions and caches instances in a long-lived, static compilation collection (like a `List<IPlugin>` or `Dictionary<string, IPlugin>`), the host functions as an indestructible GC Root, pinning the metadata and halting ALC reclamation.

### The Solution: `WeakReference<T>`

To track plugin instances without pinning them down, the host collection must leverage the native `System.WeakReference<T>` primitive.

#### How It Operates Under the Hood:

1. **Syntactic Wrapper, Runtime Primitive:** While interacting like a regular C# class, `WeakReference<T>` wraps a low-level CLR **GCHandle** designated as `HandleType.Weak` inside the runtime handle table.
2. **GC Sweep Bypass:** During the tracing/marking phase, the .NET Garbage Collector explicitly ignores weak handle pointers when compiling the tree of "reachable" objects.
3. **Automatic Zeroing:** If no strong reference connects to the target object, the GC reclaims the plugin instance memory and automatically zeros out the handle pointer to `0x0` (`null`).
4. **Comparison to Swift ARC:** This pattern maps conceptually to a `weak` property declaration under Swift's Automatic Reference Counting (ARC). However, because .NET utilizes non-deterministic background tracing rather than real-time reference counting, memory reclamation occurs asynchronously during the next scheduled GC cycle rather than immediately on code reference drop.

#### Defensive Implementation Pattern

```csharp
// Host tracks objects via weak wrappers to prevent retain cycles
private static List<WeakReference<IPlugin>> _trackedPlugins = new();

public static void RunOrchestration()
{
    foreach (var weakRef in _trackedPlugins)
    {
        // Momentarily upgrade to a strong reference to check validity
        if (weakRef.TryGetTarget(out IPlugin? activePlugin))
        {
            // Object is alive; execution is completely safe
            activePlugin.Execute();
        }
        else
        {
            // Object was swept by GC; underlying ALC has cleanly unloaded
            Log("Context cleared.");
        }
    }
}

```