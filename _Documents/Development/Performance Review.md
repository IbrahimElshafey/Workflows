# 🏗️ Workflows Engine - Performance Review & Optimizations

We have conducted a thorough review of the Workflows codebase. While the architecture is designed for high performance (hybrid model, compiled delegates, no replay overhead), we identified several critical bottlenecks—primarily **N+1 Database Queries** and **redundant reflection/compilation overhead** in execution hot paths.

Below is the detailed analysis of the performance bottlenecks, their architectural impact, and proposed solutions.

---

## Summary of Findings

| ID | Component | Bottleneck | Impact | Proposed Optimization |
|---|---|---|---|---|
| **1** | `WorkflowStore` | N+1 DB Queries in `SaveContextSyncAsync` candidate matching | **Severe** | Use in-memory wait comparison instead of querying `SignalWaits` and `ExternalChildWaits` inside a `foreach` loop. |
| **2** | `Orchestrator` | N+1 DB Queries in `ProcessSignalAsync` instance loading | **Severe** | Introduce batch-state loading (`GetInstancesStatesAsync`) to load all candidate instances in a single query. |
| **3** | `WorkflowStore` | N+1 DB Queries in `LoadInstanceStateFromDbAsync` external waits hydration | **Major** | Fetch all external child waits for the instance in a single query and hydrate in memory. |
| **4** | `TemplateRepository` | DB queries on static template lookups | **Medium** | Introduce a static thread-safe cache (`ConcurrentDictionary`) in `TemplateRepository`. |
| **5** | `Mapper` | Redundant compilation of `instanceExactMatchExpr` | **Medium** | Lookup in-memory checker caches (`SignalCache`/`CommandCache`) for compiled delegates before compiling. |
| **6** | `WorkflowRunLoop` | Reflection and `Activator.CreateInstance` in sub-workflows | **Low-Medium** | Implement a compiled factory & method cache for sub-workflow metadata. |

---

## Detailed Bottlenecks & Solutions

### 1. N+1 DB Queries in `SaveContextSyncAsync` Candidate Matching

#### Code Location
[WorkflowStore.cs:L82-L103](file:///d:/MySrc/Workflows/DataStore/Workflows.Storage.EntityFrameworkCore/WorkflowStore.cs#L82-L103)

#### The Problem
When checking for candidate running instances to match/reuse during context saving:
```csharp
var candidates = await _dbContext.WorkflowInstances
    .Where(i => i.WorkflowType == state.WorkflowType && i.Status == (int)WorkflowInstanceStatus.Running)
    .ToListAsync();

foreach (var candidate in candidates)
{
    // DB query inside a loop! 🐌
    bool hasFirstWait = await _dbContext.SignalWaits.AnyAsync(sw => sw.WorkflowInstanceId == candidate.Id && sw.IsFirstWait) ||
                        await _dbContext.ExternalChildWaits.AnyAsync(ec => ec.WorkflowInstanceId == candidate.Id && ec.IsFirstWait);
    ...
}
```
If there are 500 running instances of this type, this loop will execute up to **1,000 database queries** inside a single save transaction.

#### The Solution
Since `candidate.Waits` is already loaded and deserialized on the candidate entity, and there is already an in-memory `HasFirstWait` helper method in `WorkflowStore`, we can replace the database queries with:
```csharp
bool hasFirstWait = HasFirstWait(candidate.Waits);
```
This reduces the DB queries from **O(N) to 0** for this matching phase, executing entirely in memory.

---

### 2. N+1 DB Queries in `Orchestrator.ProcessSignalAsync` Instance Loading

#### Code Location
[Orchestrator.cs:L236-L250](file:///d:/MySrc/Workflows/Workflows.Orchestrator/Orchestrator.cs#L236-L250)

#### The Problem
When routing incoming signals, `ProcessSignalAsync` retrieves a list of candidate `instanceIds` and loads their states one-by-one in a loop:
```csharp
var instanceIds = await _workflowStore.FindInstancesWaitingForSignalAsync(signalDto.SignalIdentifier, signalDataJson);
...
foreach (var instanceId in instanceIds)
{
    var state = await _workflowStore.GetInstanceStateAsync(instanceId); // Individual DB Call 🐌
    ...
}
```
This is a textbook N+1 query problem, especially critical for broadcast signals or workflows matching many events.

#### The Solution
1. Add a bulk-loading method to the store contract:
   ```csharp
   Task<List<WorkflowStateDto>> GetInstancesStatesAsync(IEnumerable<Guid> instanceIds);
   ```
2. Implement it efficiently in `WorkflowStore.cs` using a single batch query for `WorkflowInstances` and `ExternalChildWaits`.
3. Update `Orchestrator.cs` to batch-fetch all candidate states in one call.

---

### 3. N+1 DB Queries in `WorkflowStore` External Waits Hydration

#### Code Location
[WorkflowStore.cs:L451-L470](file:///d:/MySrc/Workflows/DataStore/Workflows.Storage.EntityFrameworkCore/WorkflowStore.cs#L451-L470)

#### The Problem
To hydrate external child waits, the store traverses the wait hierarchy and queries the database for *each* external group wait it encounters:
```csharp
internal static async Task HydrateExternalChildWaitsAsync(WorkflowsDbContext dbContext, Guid instanceId, List<WaitInfrastructureDto> waits)
{
    foreach (var wait in waits)
    {
        if (wait is ExternalGroupWaitDto externalGroup)
        {
            // DB query inside recursion! 🐌
            var childEntities = await dbContext.ExternalChildWaits
                .Where(e => e.WorkflowInstanceId == instanceId && e.ParentWaitId == externalGroup.Id)
                .ToListAsync();
            ...
        }
    }
}
```

#### The Solution
Load all external child waits for the instance in **exactly one database query** during state loading, and then recurse in memory:
```csharp
var extChildWaits = await _dbContext.ExternalChildWaits
    .Where(e => e.WorkflowInstanceId == instanceId)
    .ToListAsync();

HydrateExternalChildWaitsFromList(waits, extChildWaits); // Pure in-memory mapping
```

---

### 4. ITemplateRepository Missing In-Memory Cache

#### Code Location
[TemplateRepository.cs](file:///d:/MySrc/Workflows/DataStore/Workflows.Storage.EntityFrameworkCore/TemplateRepository.cs)

#### The Problem
`TemplateRepository` is queried on every mapping operation and signal evaluation (via `SignalPreFilter`) to fetch expressions. Because it is scoped, it hits EF's `Find` on every call, requiring change-tracker lookups or database queries for static/immutable template rows.

#### The Solution
Since templates are static and immutable, add a static concurrent dictionary cache to the repository:
```csharp
private static readonly ConcurrentDictionary<string, TemplateCacheRecordDto?> _staticTemplateCache = new();
```
Check the cache first in `GetTemplate`, and populate/invalidate it in `SaveTemplate`. This guarantees near-instantaneous, zero-cost lookups after initial load.

---

### 5. Redundant Expression Compilation in `Mapper.cs`

#### Code Location
[Mapper.cs:L331-L340](file:///d:/MySrc/Workflows/Workflows.Runner/Mapper.cs#L331-L340) & [Mapper.cs:L546-L555](file:///d:/MySrc/Workflows/Workflows.Runner/Mapper.cs#L546-L555)

#### The Problem
When mapping command and signal waits, `Mapper.cs` compiles the `instanceExactMatchExpr` every single time to evaluate the exact match part, even though this delegate could be reused:
```csharp
if (instanceExactMatchExpr != null)
{
    var compiler = new ExpressionCompiler();
    var exactMatchFunc = compiler.CompiledInstanceExactMatchExpression(instanceExactMatchExpr);
    var exactMatchParts = exactMatchFunc(signalWait.WorkflowContainer, signalWait.ExplicitState);
    ...
}
```

#### The Solution
Since compiled delegates are already cached inside `SignalCompletionChecker.SignalCache` and `CommandCompletionChecker.CommandCache`, we should check the cache first. If found, reuse it; otherwise, compile and cache it.

---

### 6. Reflection and `Activator.CreateInstance` in Sub-workflows

#### Code Location
[WorkflowRunLoop.cs:L149-L167](file:///d:/MySrc/Workflows/Workflows.Runner/WorkflowRunLoop.cs#L149-L167) & [WorkflowStateService.cs:L116-L135](file:///d:/MySrc/Workflows/Workflows.Runner/Pipeline/WorkflowStateService.cs#L116-L135)

#### The Problem
When invoking a sub-workflow:
1. Reflection (`GetMethod`) is called to retrieve the workflow entry point.
2. Reflection (`GetParameters`) is called to get the sub-state type.
3. `Activator.CreateInstance` is called to instantiate the state object.

#### The Solution
Create a static method-cache and compiled-factory:
```csharp
internal static class SubWorkflowMethodCache
{
    private static readonly ConcurrentDictionary<(Type ContainerType, string MethodName), (MethodInfo Method, Type StateType, Func<object> StateFactory)> _cache = new();
    ...
}
```
This eliminates reflection and `Activator.CreateInstance` completely during sub-workflow execution.

---

## Verification Plan

We will verify these performance enhancements using:
1. **Existing Unit & Integration Tests**: Run all tests in the solution to ensure no regression or behavior changes.
2. **Benchmark / Throughput Verification**: Measure signal processing execution times and count sql database command roundtrips during massive workflows.
