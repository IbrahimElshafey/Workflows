# Workflow Side-by-Side (SxS) Execution Architecture

## 1. Overview & Objective

In a durable orchestration system, long-running workflow instances might remain suspended in the database for days, weeks, or months. When a new workflow version is deployed, existing mid-flight instances must either be explicitly migrated to the new schema/code or allowed to finish execution under their original codebase.

The **Side-by-Side (SxS)** execution framework in this system provides an automatic fallback mechanism. If no explicit data migration is registered for a suspended instance, the orchestrator automatically routes its execution to an isolated runtime context using the exact version of the workflow assembly it was started with.

---

## 2. Current Implementation Mechanics

The SxS implementation is primarily located in [WorkflowVersionRouter.cs](file:///d:/MySrc/Workflows/Hosting/Workflows.Hosting.InProcess/WorkflowVersionRouter.cs) and intercepted during execution in [RunnerWorker.cs](file:///d:/MySrc/Workflows/Hosting/Workflows.Hosting.InProcess/RunnerWorker.cs). 

The routing and execution flow proceeds as follows:

```mermaid
graph TD
    A[RunnerWorker receives WorkflowExecutionRequest] --> B{Is requested version already in memory?}
    B -- Yes --> C[Run instance normally on host]
    B -- No --> D{Is requested version == latest version?}
    D -- Yes --> C
    D -- No --> E{Is an IWorkflowMigrationExecutor registered?}
    E -- Yes --> F[Run migration & proceed on VNew]
    E -- No --> G[Trigger Side-by-Side (SxS) Execution]
    
    G --> H[Locate / Compile archived project VOld]
    H --> I[Load DLL into new AssemblyLoadContext]
    I --> J[Build isolated ServiceProvider]
    J --> K[Register VOld workflow container via reflection]
    K --> L[Run workflow inside scoped ALC Runner]
```

### Key Phases of SxS Execution:

1. **Interception ([RunnerWorker.cs:L44-45](file:///d:/MySrc/Workflows/Hosting/Workflows.Hosting.InProcess/RunnerWorker.cs#L44-L45))**:
   Prior to runner dispatch, the `RunnerWorker` queries [WorkflowVersionRouter.RouteAsync](file:///d:/MySrc/Workflows/Hosting/Workflows.Hosting.InProcess/WorkflowVersionRouter.cs#L32). If it returns a result, the runner worker bypasses the main runner execution loop and returns the routed result directly.
2. **Version Checks ([WorkflowVersionRouter.cs:L39-57](file:///d:/MySrc/Workflows/Hosting/Workflows.Hosting.InProcess/WorkflowVersionRouter.cs#L39-L57))**:
   - If the exact workflow version is registered in memory (e.g., loaded during startup in the default context), it skips routing.
   - If the request version matches the host's current version, it skips routing.
3. **Migration Check ([WorkflowVersionRouter.cs:L59-73](file:///d:/MySrc/Workflows/Hosting/Workflows.Hosting.InProcess/WorkflowVersionRouter.cs#L59-L73))**:
   - The router checks for a registered keyed service: `IWorkflowMigrationExecutor` mapped to `"{workflowName}:{instanceVersion}:{currentVersion}"`.
   - If present, the migration runs, the state is updated to the new version format, and routing yields to the host runner.
4. **On-Demand Compilation ([WorkflowVersionRouter.cs:L137-193](file:///d:/MySrc/Workflows/Hosting/Workflows.Hosting.InProcess/WorkflowVersionRouter.cs#L137-L193))**:
   - The router looks up the assembly cache `_loadedAssemblies`.
   - If not cached, it traverses up to locate the `/Archive/{workflowName}/V{version}` directory containing the legacy project (`.csproj`).
   - If the compiled DLL is missing, it spawns a synchronous CLI process: `dotnet build "{csprojFile}" -c Debug`.
5. **ALC Isolation ([WorkflowVersionRouter.cs:L190-191](file:///d:/MySrc/Workflows/Hosting/Workflows.Hosting.InProcess/WorkflowVersionRouter.cs#L190-L191))**:
   - The compiled DLL is loaded into a dedicated, collectible assembly load context:
     ```csharp
     var alc = new AssemblyLoadContext($"SxS_{workflowName}_V{version}", isCollectible: true);
     return alc.LoadFromAssemblyPath(dllPath);
     ```
6. **Isolated DI Container & Execution ([WorkflowVersionRouter.cs:L105-134](file:///d:/MySrc/Workflows/Hosting/Workflows.Hosting.InProcess/WorkflowVersionRouter.cs#L105-L134))**:
   - A new `ServiceCollection` is populated with proxies of core host services (serializers, message dispatcher, store).
   - `services.AddWorkflowsRunner()` registers the workflow engine services in the isolated container.
   - An isolated `ServiceProvider` (`alcSp`) is built, the legacy workflow container type is registered via reflection, and the execution is dispatched:
     ```csharp
     using (var scope = alcSp.CreateScope())
     {
         var runner = scope.ServiceProvider.GetRequiredService<WorkflowRunner>();
         return await runner.RunWorkflowAsync(request);
     }
     ```

---

## 3. Evaluation & Architecture Review

While the current implementation achieves logical separation and successfully executes legacy code versions concurrently, several critical design bugs and performance bottlenecks exist.

### Pros

* **Namespace & Assembly Isolation**: Using `AssemblyLoadContext` completely resolves type name collision issues. Both V1 and V2 versions of the same workflow can reside in-memory with identical namespace structures.
* **Seamless Fallback**: The developer does not need to write anything to support older versions unless they actively want to migrate data. The system transparently falls back to SxS.
* **On-Demand Compilation**: The compiler hook simplifies developer workflows in dev environments by automatically building missing archives without requiring pre-build scripts.
* **Shared Service Forwarding**: Heavy infrastructure services (e.g., database stores and serializers) are forwarded from the host container, preventing duplication of system connections.

---

### Cons & Architectural Risks

| Risk Area | Severity | Impact | Description |
| :--- | :--- | :--- | :--- |
| **Permanent Memory Leak (Zombie ALCs)** | **CRITICAL** | Leak of assembly metadata and JIT compiled code over time. | Loaded assemblies are stored in a static dictionary: `_loadedAssemblies = new()`. Because `Assembly` references its `AssemblyLoadContext`, the collectible ALCs can **never** be garbage collected. |
| **Undisposed ServiceProvider Leak** | **HIGH** | Leak of memory, open resources, and host references. | `ExecuteSxSAsync` calls `services.BuildServiceProvider()` on every execution request. The returned `ServiceProvider` implements `IDisposable` but is never disposed. |
| **High Performance Overhead** | **HIGH** | Increased CPU usage, thread blockages, and GC pressure. | Building a new DI Container (`ServiceProvider`) on *every single execution request* is extremely slow and resource-intensive due to reflection and expression-tree compilation. |
| **Synchronous CLI Build Block** | **MEDIUM** | Starvation of thread pool threads during compilation. | Spawning `dotnet build` with synchronous `process.WaitForExit()` halts the calling thread. If multiple versions start cold, the thread pool is blocked. |
| **Compilation Race Condition** | **MEDIUM** | Intermittent build failures or corrupted DLLs. | If multiple requests for the same cold-start version are routed concurrently, they will attempt to run `dotnet build` on the same directory simultaneously, causing file locks. |
| **No Dependency Redirection** | **LOW** | Potential runtime `TypeLoadException` or `InvalidCastException`. | The ALC does not implement resolution overrides for dependencies, meaning sub-contexts may load conflicting versions of shared packages. |

---

## 4. Deep-Dive on Critical Flaws

### 1. The Collectible ALC Leak
In [WorkflowVersionRouter.cs:L25](file:///d:/MySrc/Workflows/Hosting/Workflows.Hosting.InProcess/WorkflowVersionRouter.cs#L25), we have:
```csharp
private static readonly ConcurrentDictionary<string, Assembly> _loadedAssemblies = new();
```
When an assembly is loaded into a collectible ALC, it remains pinned in memory as long as there is a reference to the `Assembly` object, its types, or instances of its types. Storing the `Assembly` in a static dictionary guarantees that the ALC and all loaded types will remain in memory permanently, completely negating `isCollectible: true`.

### 2. DI Container Creation Churn
Creating a `ServiceProvider` is intended to be a startup concern. In the current implementation:
```csharp
var alcSp = services.BuildServiceProvider();
```
is invoked inside the execution path of `ExecuteSxSAsync`. A workflow consisting of multiple wait-state loops will rebuild the entire DI container on *every single signal* it receives, causing heavy CPU overhead.

---

## 5. Technical Recommendations for Refactoring

To resolve the above issues, the following refactoring steps are recommended:

1. **Introduce Weak Reference Cache**:
   Store the ALC references in a cache utilizing `WeakReference` or a custom cache wrapper that tracks active usage, allowing unused version contexts to be garbage collected when no instances of that version are executing.
2. **Cache Isolated Service Providers**:
   Instead of caching the `Assembly` object directly, cache a structured container holding both the loaded assembly and its compiled `ServiceProvider` instance. Dispose of the `ServiceProvider` when unloading the ALC.
   ```csharp
   public class IsolatedVersionContext : IDisposable
   {
       public AssemblyLoadContext Alc { get; set; }
       public IServiceProvider ServiceProvider { get; set; }
       public void Dispose() => (ServiceProvider as IDisposable)?.Dispose();
   }
   ```
3. **Add Lock Guards for On-Demand Builds**:
   Introduce a semaphore lock per version to prevent multiple threads from triggering `dotnet build` on the same directory simultaneously.
4. **Pre-build Production Artifacts**:
   In production environments, disable on-demand compiling (`dotnet build`) entirely. Require all version plugins to be pre-built and packaged in a designated directory during CI/CD.
