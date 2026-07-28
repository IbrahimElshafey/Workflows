# Out-of-Process Worker Supervisor & WF Tools Plan

## Key Architecture & Features

### 1. WF Tools (`dotnet-wf`) Suite
- **Offline DLL Comparison (Default Mode)**: Compares V1 vs V2 assemblies offline via Roslyn AST parsing without requiring a live DB connection access. Perfect for local dev and CI/CD pipelines.
- **Static Schema Verification**: Incorporates `Workflow Schema Compatibility Verification` to detect breaking yield checkpoint shifts and variable type changes.
- **Versioned JSON Schema Generator**: Automatically emits `WorkflowName_vX_Schema.json` contract baselines into `_Schemas/`.
- **Auto-Generated Migration Classes**: Automatically generates strongly-typed C# `WorkflowMigration` boilerplate classes (with `_new.AutoMapFrom(old)` and wait recreation stubs) when breaking state changes are detected.
- **Manifest Persistence**: Saves pre-publish decisions to **`deployment-manifest.json`** in-project and with build artifacts.

### 2. Out-of-Process Worker Supervisor
- **Process Isolation**: Spawns isolated `Workflows.Worker` sub-processes per assembly version. Eliminates ALC leaks and cross-version dependency conflicts.
- **Manifest-Driven Deployment Execution**: Reads `deployment-manifest.json` on startup.
- **Instant Drop vs. SxS Process Execution**:
  - If `Active Instances in DB == 0` or `Migration Script` is run -> **Drops Worker V1 instantly and activates Worker V2**.
  - If `Active Instances in DB > 0` and no migration script -> **Runs Worker V1 & V2 side-by-side** until V1 active instances drain to 0, then auto-terminates V1.

---

## Documentation Files Updated in `_Documents/Roadmap/Versioning/`

- 📘 [How SxS (Side-by-Side) Execution Works.md](file:///d:/MySrc/Workflows/_Documents/Roadmap/Versioning/How%20SxS%20%28Side-by-Side%29%20Execution%20Works.md)
- 🏛️ [Out-of-Process Worker Supervisor Architecture.md](file:///d:/MySrc/Workflows/_Documents/Roadmap/Versioning/Out-of-Process%20Worker%20Supervisor%20Architecture.md)
- 🧙‍♂️ [Pre-Publish Comparison & Impact Wizard.md](file:///d:/MySrc/Workflows/_Documents/Roadmap/Versioning/Pre-Publish%20Comparison%20&%20Impact%20Wizard.md)
- 🔍 [Workflow Schema Compatibility Verification.md](file:///d:/MySrc/Workflows/_Documents/Roadmap/Versioning/Workflow%20Schema%20Compatibility%20Verification.md)

---

## Component Structure

```
├── Workflows.Orchestrator/           # Database persistence & state transactions
├── Workflows.Host.Supervisor/        # Host Supervisor: Process manager & IPC router
├── Workflows.Worker/                 # Isolated runner executable (single version)
├── Workflows.Tools.CLI/              # WF Tools (`dotnet-wf` CLI for verify, schema, migrate, compare)
└── Workflows.Admin.UI/               # Admin web UI featuring the interactive Impact Wizard
```

---

## Verification Plan

### Automated Unit & Integration Tests
1. **Roslyn AST Diff & Verification Tests:** Verify offline comparison detecting identical, non-breaking, and breaking yield structural shifts between assemblies.
2. **JSON Schema Generator Tests:** Validate emitted `WorkflowName_vX_Schema.json` file structure against reference state machine types.
3. **Auto-Migration Boilerplate Generator Tests:** Test dynamic generation of C# `WorkflowMigration` source code.
4. **Worker Supervisor & IPC Tests:** Test process spawning, gRPC/Named Pipe IPC communication, and graceful process termination upon active instance count reaching 0.
