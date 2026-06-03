# Workflow Version Migration Strategy

This document details the architecture, auto-generation process, and API syntax for migrating active, suspended workflow instances from Version 1 (V1) to Version 2 (V2).

---

## 1. Migration Overview & Strategy

When workflow definitions evolve, existing active instances must either run to completion on their original version or be migrated to the new schema:

* **Side-by-Side (SxS) Execution:** The instance is pinned to V1 and executed inside an isolated `AssemblyLoadContext` (ALC) until termination. (For details on SxS routing and ALC isolation, see [Versioning.md](file:///d:/MySrc/Workflows/_Documents/Roadmap/Versioning/Versioning.md)).
* **Data Migration:** The suspended V1 instance's data and execution checkpoint are converted into a V2 format so it can resume running under the V2 engine.

---

## 2. Auto-Generation & Conflict Resolution Workflow

To minimize developer overhead, the migration class is **auto-generated** by comparing the V1 and V2 manifests (the generated workflow schemas). The Source Generator detects differences in properties and control flow, producing a migration file complete with automated maps and warning comments for any structural conflicts.

```
                  [V1 manifest] vs [V2 manifest]
                                │
                                ▼
                       [Conflict Detector]
                                │
        ┌────────────────────────┴────────────────────────┐
        ▼                                                 ▼
 [Compatible Fields]                               [Breaking Conflicts]
        │                                                 │
        ▼                                                 ▼
 Auto-generated maps                              Auto-generated warnings
 (e.g., AutoMapFrom)                              & TODO comment stubs
```

### 2.1 The Auto-Generated Draft (With Conflict Warnings)

When the Source Generator detects a version increment, it creates a migration stub. Below is what the generator produces when it notices that:
1. `OrderId` (V1) was renamed or replaced by `OrderNumber` (V2).
2. `CustomerName` (V1) was deleted in V2.
3. A new property `Amount` was added in V2.
4. A wait checkpoint named `VerifyStock` in V1 was replaced by `ParallelVerification` in V2.

```csharp
public class OrderWorkflowMigration_20260529 : WorkflowMigration
{
    [WorkflowMigration("OrderWorkflow", "1.0", "2.0")]
    public override void MigrateInstance(OrderWorkflowV1 old, OrderWorkflowV2 _new)
    {
        // ----------------------------------------------------------------------
        // AUTO-MAPPED FIELDS
        // Copies all matching properties by name and type automatically.
        // ----------------------------------------------------------------------
        _new.AutoMapFrom(old);

        // ----------------------------------------------------------------------
        // SCHEMA CONFLICTS (Manual Resolution Required)
        // ----------------------------------------------------------------------
        
        // TODO: Property 'OrderId' exists in V1 but was not found in V2.
        // If it was renamed or replaced, map it manually.
        
        // WARNING: Property 'CustomerName' was deleted from V2. 
        // If you need to archive its value, write it to a database or map it to a metadata bag.
        
        // TODO: Property 'Amount' (Decimal) is new in V2 and requires initialization.
        // Initialize it below (e.g., _new.Instance.Amount = 0.0m;).
    }

    public override Wait MigrateActiveWait(WaitInfrastructureDto oldWait, OrderWorkflowV2 _new)
    {
        // ----------------------------------------------------------------------
        // WAIT STATE CONFLICTS (Manual Resolution Required)
        // ----------------------------------------------------------------------
        
        // TODO: Wait point 'VerifyStock' exists in V1 active paths but is missing in V2 CFG.
        // Provide mapping rules to re-route this suspended wait by returning a new V2 Wait checkpoint.
        if (oldWait.WaitName == "VerifyStock")
        {
            // return _new.WaitSignal<InventoryAllocatedEvent>("ParallelVerification");
        }
        
        // Default: Recreate the current wait in V2 if compatible
        return _new.RecreateWait(oldWait.WaitName);
    }
}
```

---

## 3. The Clever & User-Friendly Migration API

Once the draft is generated, the developer resolves the conflicts using a clean, expressive C# syntax combining direct C# assignment with fluent helpers:

### 3.1 The Resolved Migration Class

```csharp
public class OrderWorkflowMigration_20260529 : WorkflowMigration
{
    [WorkflowMigration("OrderWorkflow", "1.0", "2.0")]
    public override void MigrateInstance(OrderWorkflowV1 old, OrderWorkflowV2 _new)
    {
        // 1. Automatic Copy of Compatible Fields
        _new.AutoMapFrom(old);

        // 2. Custom Mappings (Direct C# Assignments for clean syntax)
        _new.Instance.OrderNumber = old.Instance.OrderId.ToString();
        _new.Instance.Amount = 0.0m; // Initialize new property

        // 3. Dynamic Property Injection/Fallbacks (when working with raw state bags)
        AddToOld(old.Instance).Property("MigratedAt", DateTime.UtcNow);
    }

    public override Wait MigrateActiveWait(WaitInfrastructureDto oldWait, OrderWorkflowV2 _new)
    {
        // 1. Transition the old 'VerifyStock' step to the new 'ParallelVerification' in V2
        if (oldWait.WaitName == "VerifyStock")
        {
            return _new.WaitSignal<InventoryAllocatedEvent>("ParallelVerification");
        }

        // 2. Inspect active wait DTO properties (e.g., checks if a specific child wait is completed)
        if (oldWait.WaitName == "StockConfirmed" && oldWait.Status == WaitStatus.Completed)
        {
            // CRITICAL: Since we jump directly to the AuthorizePayment step, the engine
            // must explicitly dispatch the payment authorization command to the bus.
            _new.ScheduleCommand(new RequestPaymentCommand(_new.Instance.OrderNumber, _new.Instance.Amount));
            
            // Return the V2 Wait object representing the next step
            return _new.WaitSignal<PaymentResult>("AuthorizePayment");
        }
        
        // Default Fallback: Recreate the same wait name in the V2 topology
        return _new.RecreateWait(oldWait.WaitName);
    }
}
```

---

## 4. Command Dispatching during Migration

When you migrate a workflow state to a new execution checkpoint that yields a command (such as a `CommandWait` or a step that fires an external message), the C# iterator yield statement that normally triggers the command dispatch is bypassed. 

Because of this, **the migration engine must explicitly fire the command** to keep the workflow state aligned with the external systems:
* **The `ScheduleCommand` API** allows developers to manually construct and queue commands during the migration transaction.
* The migration engine intercepts these scheduled commands and publishes them onto the out-of-process service bus *only after* the database state update has successfully committed.

---

## 5. API Reference and Execution Mechanics

### 5.1 `MigrationContainer` — The Migration DSL Base

The migration class inherits from `MigrationContainer`, which exposes the **exact same wait factory methods** as `WorkflowContainer`. This means the developer writes identical DSL syntax inside `MigrateActiveWait` as they do when authoring workflow code — no new vocabulary to learn.

```csharp
// MigrationContainer exposes the same DSL as WorkflowContainer:
public abstract class MigrationContainer
{
    // Same signature as WorkflowContainer.WaitSignal<T>()
    protected SignalBuilder<T> WaitSignal<T>(string signalIdentifier, string name = null);

    // Same signature as WorkflowContainer.WaitDelay()
    protected TimeWait WaitDelay(TimeSpan delay, string name = null);

    // Same signature as WorkflowContainer.WaitGroup()
    protected GroupWait WaitGroup(Wait[] childWaits, string name = null);

    // Optional: when the migration needs to return a SubWorkflowWait it can provide
    // the V2 runner. If runner is omitted, engine resolves it from the V2 manifest by name.
    protected SubWorkflowWait WaitSubWorkflow(string name, IAsyncEnumerable<Wait> runner = null);

    // Rebuilds a wait that is structurally identical in V2 — resolves StateIndex via V2 CFG
    protected Wait RecreateWait(string waitName);

    // Rebuilds a checkpoint inside a sub-workflow method
    protected Wait SubWorkflow_RecreateWait(string methodName, string waitName);

    // Schedule commands dispatched after successful migration commit
    protected void ScheduleCommand(object command);
}

// The typed state wrappers only carry the instance data; no factory methods:
public class WorkflowStateWrapper<TInstance>
{
    public TInstance Instance { get; }
    void AutoMapFrom(WorkflowStateWrapper<TOtherInstance> source);
}
```

> [!NOTE]
> Because `MigrationContainer` owns the DSL, `WaitSignal<T>` / `WaitGroup` / `WaitDelay` wire the resulting `Wait` object to the migration execution context — not to a live V2 `WorkflowContainer` instance.
> When the engine applies the returned `Wait`, it transfers the `WorkflowContainer` reference to the real V2 instance at resume time.

#### When does a V2 container reference matter?

Only when returning a `SubWorkflowWait` with a **live runner**. The runner (`IAsyncEnumerable<Wait>`) must come from an actual method call on a V2 container instance. In all other wait types, the migration DSL is self-contained:

```csharp
// ✅ Signal wait — pure DSL, no V2 container needed
return WaitSignal<PaymentReceived>("PaymentReceived", "WaitForPayment")
    .MatchIf(s => s.Amount > 0)
    .AfterMatch(sig => _new.Instance.Amount = sig.Amount);

// ✅ Group wait — pure DSL, no V2 container needed
return WaitGroup(
    [
        WaitSignal<StockEvent>("StockReserved", "ReserveStock"),
        WaitSignal<FraudEvent>("FraudCleared",  "FraudCheck"),
    ],
    "ParallelChecks"
).MatchAll();

// ✅ Sub-workflow — runner omitted, engine resolves V2 method from manifest
return WaitSubWorkflow("ShippingSubWorkflow");

// ✅ Sub-workflow — runner provided explicitly (optional, used when method was renamed)
return WaitSubWorkflow("ShippingSubWorkflow", v2Container.ShippingSubWorkflow());
```

### 5.2 Runtime Migration Execution Sequence

1. **Instantiation:** The migration runner reads the target migration class based on the registered `[WorkflowMigration]` metadata.
2. **Instance State Migration:** The engine loads the V1 and V2 assemblies via ALC, reconstructs the typed state wrappers `OrderWorkflowV1` and `OrderWorkflowV2`, and invokes `MigrateInstance(old, _new)` to map the class instance variables.
3. **Active Wait Migration:** The engine loops over all active waits (status `Waiting`) in the V1 `WorkflowStateDto`. For each active wait, it invokes `MigrateActiveWait(oldWait, _new)` to map the wait DTO to its V2 equivalent. If the active wait is a `SubWorkflowWaitDto`, the engine additionally calls `MigrateSubWorkflowState` (see Section 6).
4. **Execution Position Resolution:** For each returned V2 `Wait` object, the engine resolves its compiler-generated `<>1__state` index (e.g. `4`) by matching it against V2's control flow graph (CFG) manifest, updates the DTO's state index, and saves the new V2 wait state in the database.
5. **State Persistence:** The V2 wrapper converts the migrated object back into the updated `WorkflowStateDto` schema, completing the migration.

---

## 6. Sub-Workflow Migration

### 6.1 The Two-Layer State Problem

When a parent workflow is suspended inside a `WaitSubWorkflow`, there are **two completely independent state machines** frozen at the same time:

```
WorkflowStateObject (Parent)
│
├── StateIndex          → parent's <>1__state (which yield point in Run())
├── Instance            → parent's class properties (OrderId, CustomerEmail, etc.)
│
└── StateMachinesObjects["<sub-id>"]  → SubWorkflowStateObject (Child)
        │
        ├── StateIndex  → child's own <>1__state (which yield point in ShippingSubWorkflow())
        └── Instance    → child's own local variables
```

The child sub-workflow has its own execution frame persisted inside the parent's `StateMachinesObjects` dictionary, keyed by the `SubWorkflowWaitDto.StateMachineObjectId`. The child's `StateIndex` is entirely independent of the parent's — it is mapped against the sub-workflow method's own CFG in the manifest.

This means migration must address **both layers separately**.

---

### 6.2 Migration Scenarios

| Scenario | What Changed | Migration Action |
| :--- | :--- | :--- |
| Sub-workflow method renamed | `MethodFullPath` changed but internal structure identical | Override `MigrateActiveWait` — return a new `SubWorkflowWait` pointing to the renamed method. Internal state auto-remaps if CFG matches. |
| Sub-workflow internal steps changed | Steps added/removed inside the sub-workflow method | Override `MigrateSubWorkflowState` — remap the child's `StateIndex` and local variables. |
| Sub-workflow replaced by different type | Old sub-workflow replaced by a signal wait or command | Override `MigrateActiveWait` — discard the sub-workflow DTO and return a new `WaitSignal` or other wait type instead. |
| Sub-workflow promoted to root wait | The child steps were inlined into the parent | Override `MigrateActiveWait` — return the equivalent root-level wait. The orphaned child state machine object is discarded. |

---

### 6.3 The `MigrateSubWorkflowState` Override

When the internal structure of the sub-workflow method changes between V1 and V2, the child's frozen `StateIndex` and local variables require their own migration. The engine detects this when `MigrateActiveWait` returns a `SubWorkflowWait` and the sub-workflow method name appears in the V2 manifest with a different CFG than V1.

The migration class can override `MigrateSubWorkflowState` to handle this:

```csharp
public class OrderWorkflowMigration_20260529
    : WorkflowMigration<OrderWorkflowV1, OrderWorkflowV2>
{
    [WorkflowMigration("OrderWorkflow", "1.0", "2.0")]
    public override void MigrateInstance(OrderWorkflowV1 old, OrderWorkflowV2 _new)
    {
        _new.AutoMapFrom(old);
        _new.Instance.OrderNumber = old.Instance.OrderId.ToString();
    }

    public override Wait MigrateActiveWait(WaitInfrastructureDto oldWait, OrderWorkflowV2 _new)
    {
        // If suspended inside ShippingSubWorkflow — return it by name.
        // Engine will call MigrateSubWorkflowState next to remap the child's checkpoint.
        if (oldWait is SubWorkflowWaitDto sub && sub.MethodFullPath.EndsWith("ShippingSubWorkflow"))
            return WaitSubWorkflow("ShippingSubWorkflow");  // DSL from MigrationContainer

        return RecreateWait(oldWait.WaitName);              // DSL from MigrationContainer
    }

    // Called by the engine after MigrateActiveWait returns a SubWorkflowWait.
    public override Wait MigrateSubWorkflowState(SubWorkflowWaitDto oldSubWait, OrderWorkflowV2 _new)
    {
        // "ConfirmPickup" was added before "OrderShipped" in V2 — StateIndex shifted.
        if (oldSubWait.WaitName == "OrderShipped")
            return SubWorkflow_RecreateWait("ShippingSubWorkflow", "OrderShipped");

        return SubWorkflow_RecreateWait("ShippingSubWorkflow", oldSubWait.WaitName);
    }

}
```

---

### 6.4 Full API Contract

```csharp
/// <summary>
/// Base class for all workflow migration classes.
/// Inherits MigrationContainer to expose the standard wait DSL directly.
/// </summary>
public abstract class WorkflowMigration<TOld, TNew> : MigrationContainer
    where TOld : WorkflowStateWrapper
    where TNew : WorkflowStateWrapper
{
    // Phase 1: Map the parent workflow's class-level instance fields.
    // Called once per migrated instance.
    public abstract void MigrateInstance(TOld old, TNew _new);

    // Phase 2: Called once per active wait DTO (and once per child inside a GroupWaitDto).
    // Returns the V2 Wait object the engine should substitute.
    public abstract Wait MigrateActiveWait(WaitInfrastructureDto oldWait, TNew _new);

    // Phase 3 (optional): Called after MigrateActiveWait returns a SubWorkflowWait,
    // to migrate the child's own frozen StateIndex and local variables.
    // Default auto-remaps by wait name against the V2 CFG.
    public virtual Wait MigrateSubWorkflowState(SubWorkflowWaitDto oldSubWait, TNew _new)
        => SubWorkflow_RecreateWait(oldSubWait.MethodFullPath, oldSubWait.WaitName);
}
```

> [!NOTE]
> `MigrateSubWorkflowState` has a default implementation that auto-remaps the child's checkpoint by name. The developer only needs to override it when the sub-workflow's internal step sequence or naming changed between versions.

---

## 7. Group Wait Containing Multiple Sub-Workflows

### 7.1 The Problem

A `GroupWaitDto` carries a `ChildWaits` collection which can contain any mix of wait types — including multiple `SubWorkflowWaitDto` children, each with its own independent frozen execution frame:

```
GroupWaitDto  ("ParallelFulfillment")  ← active, MatchAll
│
├── SubWorkflowWaitDto ("ShippingSubWorkflow")   ← Waiting, StateIndex=2, StateMachineObjectId=A
│       └── frozen local vars: { trackingNumber, carrier }
│
├── SubWorkflowWaitDto ("BillingSubWorkflow")    ← Waiting, StateIndex=1, StateMachineObjectId=B
│       └── frozen local vars: { invoiceId }
│
└── SignalWaitDto       ("FraudCleared")         ← Completed already
```

All three children are tracked independently. The engine must walk the entire `ChildWaits` tree recursively and apply migration to each node.

---

### 7.2 Engine Recursive Walk Strategy

The migration engine owns the recursion — the developer does **not** need to iterate children manually. The walk follows this pattern:

```
For each root active wait in WorkflowStateDto.Waits:
    MigrateActiveWait(rootWait)
        │
        ├── If GroupWaitDto  → recursively walk each child in ChildWaits:
        │       MigrateActiveWait(child)
        │           ├── If SubWorkflowWaitDto → also call MigrateSubWorkflowState(child)
        │           ├── If SignalWaitDto      → remap as usual
        │           └── If another GroupWaitDto → recurse again (nested groups)
        │
        └── If SubWorkflowWaitDto at root → also call MigrateSubWorkflowState
```

The developer's `MigrateActiveWait` is called once per **leaf or node** — it handles the decision for that individual wait, and the engine handles stitching the resulting children back into a rebuilt group structure.

---

### 7.3 Migrating the Group Structure Itself

If the group shape is **unchanged** (same number of children, same types), the engine auto-rebuilds it. The developer only needs to handle individual child mappings via `MigrateActiveWait` and `MigrateSubWorkflowState`.

If the **group structure itself changed** (e.g. a third sub-workflow was added, or the group policy changed from `MatchAll` to `MatchAny`), the developer intercepts the `GroupWaitDto` directly in `MigrateActiveWait` and returns a fully rebuilt group using the native DSL — same syntax as in workflow code:

```csharp
public override Wait MigrateActiveWait(WaitInfrastructureDto oldWait, OrderWorkflowV2 _new)
{
    // Intercept the group when its structure changed
    if (oldWait is GroupWaitDto group && group.WaitName == "ParallelFulfillment")
    {
        // V2 adds a WarehouseSubWorkflow to the group.
        // WaitGroup / WaitSubWorkflow come from MigrationContainer — identical DSL to workflow code.
        return WaitGroup(
            [
                WaitSubWorkflow("ShippingSubWorkflow"),
                WaitSubWorkflow("BillingSubWorkflow"),
                WaitSubWorkflow("WarehouseSubWorkflow"),   // new in V2
            ],
            "ParallelFulfillment"
        ).MatchAll();
        // Engine then calls MigrateSubWorkflowState for each child that maps to a V1 frozen frame
    }

    return RecreateWait(oldWait.WaitName);
}
```

---

### 7.4 Partial Completion Within a Group

Because `MatchAll` groups accumulate completions over time, some children may already be `Completed` when migration runs. The engine only calls `MigrateSubWorkflowState` for children still in `Waiting` status — completed child frames have no frozen state machine to migrate:

```
ChildWaits during migration:
├── SubWorkflowWaitDto "ShippingSubWorkflow"  Status=Completed  → skipped, already done
├── SubWorkflowWaitDto "BillingSubWorkflow"   Status=Waiting    → MigrateSubWorkflowState called
└── SignalWaitDto       "FraudCleared"        Status=Completed  → skipped, already done
```

> [!IMPORTANT]
> The V2 rebuilt group must preserve the `Completed` status of already-finished children. If the developer returns a fully rebuilt `WaitGroup`, they must explicitly carry over the completion state from the old group's `ChildWaits` via `old.Child("ShippingSubWorkflow").Status` or the engine will reset all children to `Waiting`, breaking the `MatchAll` accumulation logic.
