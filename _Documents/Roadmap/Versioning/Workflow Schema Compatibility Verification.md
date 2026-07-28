# 🔍 Workflow Schema Compatibility Verification (WF Tools)

## 1. Overview

The **Workflow Schema Compatibility Verification** module is integrated directly into **WF Tools** (`dotnet-wf`). It uses Roslyn Abstract Syntax Tree (AST) parsing to analyze workflow assemblies offline, verify state machine compatibility between versions, generate JSON schema baselines, and detect breaking state machine shifts before code is published to production.

---

## 2. WF Tools Command Options

```bash
# 1. Offline Comparison & Verification (Default Mode - No DB required)
dotnet wf verify --old ./bin/V1/Workflows.dll --new ./bin/V2/Workflows.dll

# 2. Extract and Output Versioned JSON Schemas
dotnet wf schema --assembly ./bin/V2/Workflows.dll --out ./_Schemas/

# 3. Auto-Generate C# Migration Boilerplate Classes for Breaking Changes
dotnet wf migrate --old ./bin/V1/Workflows.dll --new ./bin/V2/Workflows.dll --out ./Migrations/

# 4. Interactive Pre-Publish Wizard (Generates deployment-manifest.json)
dotnet wf compare --old ./bin/V1/Workflows.dll --new ./bin/V2/Workflows.dll [--db "Data Source=prod.db"]
```

---

## 3. Roslyn AST Analysis & JSON Schema Generation

The CLI tool loads assembly versions and leverages Roslyn's Semantic Model to extract the **Wait Topology** of each workflow into a standardized **`WorkflowName_vX_Schema.json`** file.

### 1. Wait Topology Mapping
For each `WorkflowContainer`, the analyzer maps:
*   The sequence of `yield return` statements and yield ordinals.
*   The wait primitive types (e.g., `SignalWait<T>`, `TimeWait`, `GroupWait`).
*   The correlation keys and wait names.
*   The domain state POCO property names and types.

### 2. Detection Rules
| Change Type | Risk Level | Description | WF Tools Action |
| :--- | :--- | :--- | :--- |
| **Insert Wait** | 🔴 Critical Break | Inserting a wait statement in the middle of an existing path. | **Fails Verification**: Requires a version bump + Migration class or SxS Worker. |
| **Delete Wait** | 🔴 Critical Break | Deleting an existing wait statement in an active path. | **Fails Verification**: Breaks state machine rehydration index of running instances. |
| **Append Wait** | 🟢 Safe | Appending a new wait *at the end* of the execution path. | **Permitted**: Does not affect prior state indexes. |
| **Rename Property** | 🟡 Warning / Break | Renaming a workflow container public property. | **Fails Verification**: Auto-maps if `[JsonPropertyName]` redirect is present. |

---

## 4. Manifest Persistence (`deployment-manifest.json`)

When verification and wizard selection complete, WF Tools outputs a **`deployment-manifest.json`** file. This manifest is stored in-project and packaged with published build artifacts, guiding the Host Supervisor process during deployment.
