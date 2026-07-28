# 🧙‍♂️ WF Tools: Pre-Publish Comparison & Impact Wizard

## 1. Overview

The **Pre-Publish Comparison & Impact Wizard** is a core component of the **WF Tools suite** (`dotnet-wf` CLI and embedded Admin UI Wizard). It analyzes workflow assemblies, verifies schema compatibility, generates schema contracts, creates migration boilerplate classes, and produces a deterministic **`deployment-manifest.json`** for deployment pipelines.

---

## 2. Key Capabilities & Workflow

### 1. Offline Assembly Comparison (Default Mode)
- Runs completely **offline by default** without requiring a live database connection access.
- Ideal for CI/CD pipelines, local developer environments, and git pre-commit hooks.
- Compares `WorkflowV1.dll` vs `WorkflowV2.dll` using Roslyn AST parsing to detect yield checkpoint shifts, property renames, and structural state machine drift.

### 2. Versioned JSON Schema Contract Generation
When WF Tools analyzes an assembly version, it automatically generates a standardized **`WorkflowName_vX_Schema.json`** file for each workflow definition saved in-project under `_Schemas/`.

### 3. Automated Migration Boilerplate Generation (`AutoMapFrom` Blast Radius Contract)
When breaking structural changes are detected between V1 and V2, WF Tools provides an option (`dotnet wf migrate --old V1.dll --new V2.dll --out ./Migrations`) to **automatically generate strongly-typed C# migration classes**:

- **Developer Scaffolding:** Emitted `.cs` files are scaffolding for developer review, NOT magic runtime fallbacks.
- **Fail-Loud Property Assertion:** If a property cannot be mapped with 100% confidence, `AutoMapFrom` throws an explicit `WorkflowMigrationException`.
- **Instance Blast Radius Isolation:** During batch migrations against live DB rows, each instance executes in an isolated savepoint. If instance #105 throws `WorkflowMigrationException`:
  - Instance #105's migration rolls back to `StateObject_PreMigration_V1`.
  - Status is updated to `MigrationFailed` / `Quarantined` and logged to `WorkflowExecutionErrors`.
  - Instance #105 remains on Version 1 and continues executing on Worker V1.
  - Sibling instances (#101–#104, #106+) migrate normally.

### 4. Manifest Persistence (`deployment-manifest.json`) vs Live DB Evaluation
- **Manifest = Static Intent:** `deployment-manifest.json` carries static AST analysis metadata and deployment intentions.
- **Host = Live Evaluator:** The Host Supervisor reads the manifest intent on startup, but **always queries live DB counts at execution time** (`SELECT COUNT(*)`) to decide whether to execute an Instant Drop, Migration, or SxS Process Spin-Up.

---

## 3. Recommended Deployment Decision Matrix

| Scenario | Offline Analysis Result | Live DB Active V1 Instances | Migration Script Present? | Manifest Strategy | Host Supervisor Action |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **1. Unchanged** | Identical AST | N/A | N/A | `InstantFullReplacement` | Activate V2 Worker, drop V1 instantly. |
| **2. Changed Workflow** | Breaking Drift | **0 instances** | N/A | `InstantFullReplacement` | Drop V1 Worker instantly, activate V2 Worker. |
| **3. Changed Workflow** | Breaking Drift | **> 0 instances** | ✅ Yes (`WorkflowMigration`) | `ExecuteMigrationThenDropV1` | Run DB migration script, upgrade DB rows to V2. Hold 15-min grace window before dropping V1 Worker. |
| **4. Changed Workflow** | Breaking Drift | **> 0 instances** | ❌ No | `SideBySideWorkerSpinUp` | Spin up V2 Worker alongside V1 Worker. Drain V1 instances to 0, then auto-terminate V1. |
