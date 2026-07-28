# Workflows Tools (`dotnet-wf`) CLI Suite

`dotnet-wf` (`Workflows.Tools.CLI`) is the official command-line tool suite for the **Workflows Engine**. It provides offline schema extraction, automated version-to-version state drift verification, pre-publish impact analysis, and C# migration boilerplate generation.

---

## 🚀 Installation & Usage

Run the executable directly via `dotnet`:

```bash
dotnet dotnet-wf.dll <command> [options]
# Or if installed as a dotnet global tool:
dotnet wf <command> [options]
```

---

## 🛠️ CLI Commands Reference

### 1. `verify` — CI/CD Pre-Publish Verification Gate
Compares two workflow assembly versions (`--old` vs `--new`) offline without needing a database connection. Performs recursive state data contract analysis to detect breaking drift (deleted properties, type mismatches, or structural changes).

```bash
dotnet wf verify --old "./bin/V1/OrderWorkflows.dll" --new "./bin/V2/OrderWorkflows.dll"
```

- **Output**: Logs detected schema differences.
- **CI/CD Behavior**: Exits with **Exit Code 0** if compatible, or **Exit Code 1** if breaking changes are detected without migration scaffolding (failing build pipelines automatically).

---

### 2. `schema` — Export Contract Schema JSON
Reflects over target workflow assemblies and exports versioned `WorkflowName_vX_Schema.json` contract files containing all state POCO definitions and property metadata.

```bash
dotnet wf schema --assembly "./bin/Release/net10.0/OrderWorkflows.dll" --out "./_Schemas"
```

- **Output**: Generates `<Assembly>_Schema.json` in the target directory.

---

### 3. `compare` — Generate Pre-Publish Deployment Manifest
Performs pre-publish impact analysis across versions and generates a `deployment-manifest.json` carrying static deployment intent for the Host Orchestrator and Worker Supervisor.

```bash
dotnet wf compare --old "./bin/V1/Workflows.dll" --new "./bin/V2/Workflows.dll" --out "./Publish"
```

- **Output**: Generates `deployment-manifest.json`.

---

### 4. `migrate` — Generate C# Migration Class Boilerplate
Auto-generates strongly-typed C# `WorkflowMigration` class boilerplate when breaking state changes occur between version transitions.

```bash
dotnet wf migrate --workflow "OrderProcessingWorkflow" --from 1 --to 2 --out "./Migrations"
```

- **Output**: Generates `OrderProcessingWorkflowMigration_V1_To_V2.cs` with fail-loud `_new.AutoMapFrom(old)` mapping logic.

---

## 📄 Deployment Manifest Format (`deployment-manifest.json`)

The generated deployment manifest guides the Host Orchestrator and Worker Supervisor during deployment:

```json
{
  "DeploymentId": "dep_20260728_221000",
  "SourceAssembly": "OrderWorkflows.V2.dll",
  "TargetVersion": 2,
  "CreatedUtc": "2026-07-28T22:10:00.000Z",
  "HasBreakingChanges": true,
  "Workflows": [
    {
      "WorkflowName": "OrderProcessingWorkflow",
      "Status": "Changed_BreakingDrift",
      "Strategy": "ExecuteMigrationScriptThenDropV1",
      "MigrationClass": "OrderProcessingWorkflowMigration_V1_To_V2",
      "Differences": [
        "Property 'ShippingDetails.ZipCode' data contract type changed from System.Int32 to System.String."
      ]
    }
  ],
  "SupervisorInstruction": {
    "DropOldWorkerImmediately": false,
    "TargetWorkerVersion": "2.0.0"
  }
}
```

---

## 🔄 Integration in CI/CD Pipelines

Add a verification step to your build script (e.g., GitHub Actions / Azure DevOps) before publishing:

```yaml
- name: Verify Workflow Version Drift
  run: |
    dotnet run --project Workflows.Tools.CLI -- verify \
      --old ./artifacts/v1/OrderWorkflows.dll \
      --new ./artifacts/v2/OrderWorkflows.dll
```

If breaking changes are detected without a corresponding migration script, `verify` exits with code `1` to prevent unsafe zero-downtime deployments.

---

## 📂 Related Architecture Documentation

- 📘 [Pre-Publish Comparison & Impact Wizard](file:///d:/MySrc/Workflows/_Documents/Roadmap/Versioning/Pre-Publish%20Comparison%20&%20Impact%20Wizard.md)
- 📘 [Workflow Schema Compatibility Verification](file:///d:/MySrc/Workflows/_Documents/Roadmap/Versioning/Workflow%20Schema%20Compatibility%20Verification.md)
- 📘 [Versioning - Workflow Migration](file:///d:/MySrc/Workflows/_Documents/Roadmap/Versioning/Versioning%20-%20Workflow%20Migration.md)
