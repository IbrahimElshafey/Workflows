# How to Migrate Workflows: A Step-by-Step Developer Guide

This document explains how to handle workflow updates, versioning, and state migration when migrating existing in-flight workflow instances to a newer definition version.

---

## 1. High-Level Concept

When a workflow definition changes, you have two options for handling existing, running instances:
1. **Side-by-Side (SxS) Execution (Default):** Let them run to completion on their original codebase using isolated dynamic loading.
2. **Active State Migration:** Upgrade them to the latest definition version, migrating their database state object and mapping their active suspension points (waits) to match the new workflow logic.

---

## 2. Step-by-Step Migration Guide by Sample

We will use the **`OrderWorkflow`** transition from **Version 1** to **Version 2** as our sample.

### Scenario Details:
* **Version 1** has fields: `OrderId (Guid)`, `CustomerName (string)`, and `Amount (decimal)`.
* **Version 2** introduces a new field: `OrderNumber (string)` (format: `ORD-{OrderId}`), keeping other fields.
* **Suspension Point:** Instances are suspended at an active `SignalWait` named `"WaitApproved"`.

---

### Step 1: Increment the Version and Apply the Code Fix

1. Open your active workflow file (e.g., `OrderWorkflow.cs`) and increment the version in the `[Workflow]` attribute:
   ```csharp
   [Workflow("OrderWorkflow", version: 2)]
   public class OrderWorkflow : WorkflowContainer
   {
       // ...
   }
   ```
2. The IDE will immediately raise a warning diagnostic:
   > **WF300**: Workflow 'OrderWorkflow' version bumped from 1 to 2. Apply 'Archive V1 and generate schema' code fix.
3. Trigger the Roslyn **Code Action / Quick Fix** (`Ctrl+.` or click the lightbulb) and select:
   > **Archive 'OrderWorkflow' V1 and generate schema**

#### What the Code Fix automatically does:
* Copies the V1 code snapshot to `_Documents/Archive/OrderWorkflow/V1/OrderWorkflow_V1.cs` and rewrites the namespace to `Archive.OrderWorkflow.V1`.
* Generates `OrderWorkflowV1_Layout.cs` containing `OrderWorkflowV1Instance` (POCO state) and `OrderWorkflowV1` (strongly typed state wrapper).
* Extracts the structural JSON schema and writes it to `OrderWorkflow_V1_Schema.json`.
* Generates a standalone `.csproj` for the V1 assembly so it can be compiled/run isolated.

---

### Step 2: Implement the Workflow Migration Class

Create a new migration class inheriting from `WorkflowMigration<TOld, TNew>` where `TOld` is the archived layout wrapper for V1, and `TNew` is the layout wrapper for V2.

```csharp
using System;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Definition;

namespace MyCompany.Workflows.Migrations
{
    [WorkflowMigration("OrderWorkflow", fromVersion: 1, toVersion: 2)]
    public class OrderWorkflowMigration_V1_To_V2 : WorkflowMigration<OrderWorkflowV1, OrderWorkflowV2>
    {
        /// <summary>
        /// Phase 1: Migrate the parent instance state (primitive/POCO fields).
        /// </summary>
        public override void MigrateInstance(OrderWorkflowV1 old, OrderWorkflowV2 _new)
        {
            // 1. Automatically copy matching properties (CustomerName, Amount)
            _new.AutoMapFrom(old);

            // 2. Map new/changed properties explicitly
            _new.Instance.OrderNumber = $"ORD-{old.OrderId:N}".ToUpper().Substring(0, 16);

            // 3. (Optional) Schedule commands/events to execute after migration commits
            ScheduleCommand("OrderMigratedNotification");
        }

        /// <summary>
        /// Phase 2: Map/Remap active suspension waits.
        /// </summary>
        public override Wait MigrateActiveWait(WaitInfrastructureDto oldWait, OrderWorkflowV2 _new)
        {
            if (oldWait.WaitName == "WaitApproved")
            {
                // Recreate wait by name with updated V2 behavior/match criteria
                return RecreateWait("WaitApproved");
            }

            throw new NotSupportedException($"Wait {oldWait.WaitName} migration is not supported.");
        }
    }
}
```

---

### Step 3: Register the Migration in Dependency Injection

To wire up the migration executor so the engine knows how to execute it, register it during service collection setup:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // ... other services

    // Register the migration transition (V1 -> V2)
    services.AddWorkflowMigration<OrderWorkflowV1, OrderWorkflowV2, OrderWorkflowMigration_V1_To_V2>();
}
```

---

## 3. How the Runtime Executes Migrations

When an existing V1 workflow instance receives a trigger (signal, timer, message) or is loaded for execution:
1. The **`WorkflowVersionRouter`** intercepts the request and detects that the database state is running V1, whereas the host definition is registered as V2.
2. It looks up the DI container for a keyed migration executor registered with key `"OrderWorkflow:1:2"`.
3. If the executor is found:
   * It loads the V1 state DTO.
   * Runs the `MigrateInstance` method to upgrade fields.
   * Runs `MigrateActiveWait` for any active waits.
   * Persists the migrated V2 state back to the database in a single database transaction.
   * Executes the workflow under the host's V2 engine path.
4. If no executor is registered, the router falls back to **SxS execution**, compiling and running the instance against the archived V1 assembly inside its isolated `AssemblyLoadContext`.
