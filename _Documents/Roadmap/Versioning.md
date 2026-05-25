# Workflows Engine: Versioning and Migration Strategy

## 1. Architectural Philosophy

For a production-grade workflow engine, versioning must handle two core challenges:
1. **Side-by-Side Execution (SxS)**: Old, in-flight workflow instances must continue running on the exact version of the C# code they started with, while new workflow instances are spawned using the latest version.
2. **In-Flight State Migration**: Long-running workflow instances (which may run for weeks or months) must optionally be upgradable mid-flight to a newer version when critical business logic or bug fixes are deployed.

To achieve this while maintaining a stateless runner and keeping database operations simple, we employ a **Dual-Key Routing and Explicit Version Snapshot** pattern.

---

## 2. Version Declaration via Attributes

We separate the C# class identity from the logical workflow identity using class-level attributes. This allows multiple versions of the same workflow to exist concurrently in the codebase without naming conflicts.

### Developer API Syntax
```csharp
namespace App.Workflows
{
    [Workflow(Name = "OrderProcessing", Version = "1.0.0")]
    public sealed class OrderProcessingV1 : WorkflowContainer
    {
        public override async IAsyncEnumerable<Wait> ExecuteWorkflowAsync() { ... }
    }

    [Workflow(Name = "OrderProcessing", Version = "2.0.0")]
    public sealed class OrderProcessingV2 : WorkflowContainer
    {
        public override async IAsyncEnumerable<Wait> ExecuteWorkflowAsync() { ... }
    }
}
```

* **`WorkflowAttribute` Properties**:
  - `Name` (string): The logical name of the workflow (e.g. `OrderProcessing`).
  - `Version` (string): Semantic version token (e.g., `1.0.0`, `2.0.0`).
  - `IsActive` (bool, optional): Determines if this version should automatically be routed to for new instances if not overridden.

---

## 3. Side-by-Side Execution & Routing

When the Runner node boots up, it scans the loaded assemblies for all sealed types inheriting from `WorkflowContainer` and decorated with the `[Workflow]` attribute. It registers this metadata with the Orchestrator.

### The Database Registry Schema

The `WorkflowRegistrations` table serves as the directory for available execution routes:

```sql
CREATE TABLE WorkflowRegistrations (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    WorkflowName NVARCHAR(100) NOT NULL,
    Version NVARCHAR(50) NOT NULL,
    AssemblyQualifiedName NVARCHAR(500) NOT NULL,
    IsActiveRoute BIT NOT NULL,
    CONSTRAINT UQ_WorkflowRegistrations UNIQUE (WorkflowName, Version)
);
```

### Routing Signals to the Correct Version

When a signal arrives, the Orchestrator joins the incoming wait condition with the instances table to resolve the exact version associated with the instance:

```
                  [ Incoming Signal ]
                           │
                           ▼
              [ Query DB: Join SignalWaits ]
              [ with WorkflowInstances     ]
                           │
                           ▼
            [ Resolves Instance Version ID ] (e.g., Instance #999 is 1.0.0)
                           │
                           ▼
          [ Packages RunWorkflowCommand ] (with AssemblyQualifiedName of V1)
                           │
                           ▼
                 [ Dispatched to Runner ]
                           │
                           ▼
             [ Runner hydrates V1 class ] (OrderProcessingV1)
```

By passing the `AssemblyQualifiedName` inside the `RunWorkflowCommand`, the Runner hydrates the exact class representing that version using reflection-free compiled factory constructors, executing the correct state machine logic.

---

## 4. Why Static Topology Extraction is Fragile

An earlier design suggested running a static Roslyn analyzer or reflection-based `TopologyExtractor` at startup to extract a JSON map of yields (`WaitSignal`, `TimeWait`). 
However, **static analysis of C# code is fragile** because workflows can contain arbitrary dynamic loops, conditional branches (`if`/`else`), and runtime checks:

```csharp
// Static analysis cannot predict which wait is next!
if (DateTime.UtcNow.DayOfWeek == DayOfWeek.Friday) {
    yield return WaitSignal<FridaySignal>("WeekendGate");
} else {
    yield return TimeWait.Delay(TimeSpan.FromDays(1));
}
```

### The Solution: Runtime Definition Mapping
Instead of analyzing code statically, the workflow's structure is defined *implicitly* by the code itself during execution. The Orchestrator does not need to know the entire future topology of the workflow; it only needs to register the **immediate next waits** yielded by the state machine at the end of each execution tick.

---

## 5. In-Flight State Migration (The Migration Plan Protocol)

When a workflow version `2.0.0` is deployed, we may want to migrate active, suspended instances of version `1.0.0` to the new version without waiting for them to complete. Since the state is stored as a serialized JSON `StateMachineObject`, we can implement a state migration utility.

### The Migration Interface
Developers define a migration class implementing `IWorkflowStateMigration`:

```csharp
public interface IWorkflowStateMigration
{
    string WorkflowName { get; }
    string SourceVersion { get; }
    string TargetVersion { get; }

    /// <summary>
    /// Performs state mutations, closure variables transformations, 
    /// or index adjustments on the raw serialized state.
    /// </summary>
    void MigrateState(StateMachineObject stateObject);
}
```

### Migration Implementation Example
```csharp
public class OrderMigration_1_To_2 : IWorkflowStateMigration
{
    public string WorkflowName => "OrderProcessing";
    public string SourceVersion => "1.0.0";
    public string TargetVersion => "2.0.0";

    public void MigrateState(StateMachineObject stateObject)
    {
        // 1. Mutate local variables (e.g., renaming variables in the closure)
        if (stateObject.WaitStatesObjects.TryGetValue("oldVariable", out var val))
        {
            stateObject.WaitStatesObjects["newVariable"] = val;
            stateObject.WaitStatesObjects.Remove("oldVariable");
        }

        // 2. Adjust State Machine Instruction Index if a new yield was inserted
        if (stateObject.StateIndex >= 3)
        {
            stateObject.StateIndex += 1; // Shift index forward to match V2 compilation layout
        }
    }
}
```

### Execution of Migration
An administrative endpoint (triggered via the UI Admin Dashboard) runs the migration:
1. **Locate**: Fetch the target `WorkflowInstance` and its `StateObject` JSON.
2. **Transform**: Deserialise the state, execute `MigrateState(stateObject)`, and serialise it back.
3. **Re-route**: Update the instance's associated version foreign key to `2.0.0` in the database.
4. **Commit**: Save changes in a single database transaction. The next signal will automatically load the new version.