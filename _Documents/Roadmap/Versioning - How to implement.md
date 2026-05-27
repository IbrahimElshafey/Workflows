## Workflow Versioning, Contract Compatibility & Build Tooling
### Internal Design Document · May 2026

---

## Table of Contents

1. [Core Philosophy](#1-core-philosophy)
2. [Git Branching Model](#2-git-branching-model)
3. [ContractManifest.json — Structure & Comparison](#3-contractmanifestjson--structure--comparison)
4. [Cases Where an Old Workflow Cannot Be Updated](#4-cases-where-an-old-workflow-cannot-be-updated)
5. [The Custom Build Tool — `dotnet workflow`](#5-the-custom-build-tool--dotnet-workflow)
6. [Live Analyzer Warnings in Visual Studio](#6-live-analyzer-warnings-in-visual-studio)
7. [Database — Instance Version Pinning](#7-database--instance-version-pinning)
8. [Roslyn Analyzer Rules](#8-roslyn-analyzer-rules)
9. [Summary of Responsibilities](#9-summary-of-responsibilities)

---

## 1. Core Philosophy

A workflow assembly is an **immutable binary unit**. Once compiled and deployed, a workflow DLL is never mutated in place. Running instances in the database are permanently pinned to the binary they were started on. The engine guarantees that a paused instance always resumes on the exact binary that created it — no exceptions.

The practical consequences:

- Old instances keep running on their original DLL.
- New instances are routed to the latest active DLL.
- Both generations run side by side inside isolated `AssemblyLoadContext`s.
- All DLLs referenced by the host are loaded into a **shared context**. Workflow version DLLs each get their own isolated context.

> **Key constraint:** Recompiling an old workflow against updated contracts does NOT change its runtime behaviour — it only validates that the old code still compiles cleanly against the new host. The binary that reaches production differs only in its contracts linkage, not in workflow logic or state machine layout.

### State Persistence

All workflow authors are **required** to use `.WithState<T>()` for any state that must persist across waits. This is enforced by the **WF1002** Roslyn analyzer (compiler error, not a warning). Closures and arbitrary local variable captures across yield points are forbidden.

The `Wait` base class and its descendants own an explicit state property:

```csharp
public class Wait
{
    public object ExplicitState { get; internal set; }
}
```

---

## 2. Git Branching Model

### 2.1 Branch Strategy

Every deployed workflow version lives on its own **long-lived branch**. These branches are **never merged back into main**. They exist solely to allow contract recompilation for already-running instances.

```
main
  └─ workflow/OrderApproval/v1        ← frozen logic; only contracts linkage may change
  └─ workflow/OrderApproval/v2        ← frozen logic; only contracts linkage may change
  └─ workflow/InvoiceApproval/v1      ← frozen logic; only contracts linkage may change
  └─ feature/new-payment-workflow     ← active development
```

**Naming convention:** `workflow/<WorkflowName>/v<N>`

**Protected branch policy:** Workflow version branches are protected — no force-push, no deletion, ever. The build tool verifies the branch and its pinned commit exist before proceeding. If either is missing, the build is blocked and the missing branch is reported explicitly.

### 2.2 Linking a Git Commit to a Workflow DLL

Every workflow DLL must be traceable to the exact git commit that produced it. The build tool embeds provenance as assembly-level attributes at compile time via a **source-generated file** (`WorkflowBuildInfo.g.cs`). The workflow author never writes these manually.

```csharp
[assembly: WorkflowBuildInfo(
    WorkflowName     = "OrderApproval",
    WorkflowVersion  = 2,
    GitCommitHash    = "a3f9d12",
    GitBranch        = "workflow/OrderApproval/v2",
    GitCommitMessage = "Recompile v2 against contracts V3",
    BuiltAt          = "2026-05-27T10:00:00Z",
    ContractsVersion = "V3",
    ContractsHash    = "sha256:abc123..."
)]
```

The build tool reads git metadata via `git log -1` and `git rev-parse HEAD` and injects them before invoking the compiler. The orchestrator reads these attributes at DLL load time and stores them in the version registry — making every running instance in the Admin UI traceable to a specific branch and commit.

> **Operational benefit:** When a production incident occurs, the on-call engineer opens the Admin UI, sees the exact git commit and branch for that instance's DLL, and checks out that branch immediately — no guesswork.

### 2.3 The `workflow-registry.json` File

This file lives in `main` and is the **source of truth** for which commit each workflow version was built from. The build tool reads it in Step 1 to know which commit to branch from — not git log, not branch history, this file.

```json
{
  "OrderApproval": {
    "activeFixBranch": "fix/contracts-V4",
    "versions": {
      "v1": {
        "gitBranch": "workflow/OrderApproval/v1",
        "gitCommit": "a3f9d12",
        "contractsVersion": "V2",
        "status": "Active",
        "fixBranches": [
          {
            "branch": "fix/contracts-V4",
            "targetContracts": "V4",
            "status": "InProgress"
          }
        ]
      },
      "v2": {
        "gitBranch": "workflow/OrderApproval/v2",
        "gitCommit": "f1b8c34",
        "contractsVersion": "V3",
        "status": "Active",
        "fixBranches": [
          {
            "branch": "fix/contracts-V4",
            "targetContracts": "V4",
            "status": "InProgress"
          }
        ]
      }
    }
  },
  "InvoiceApproval": {
    "activeFixBranch": null,
    "versions": {
      "v1": {
        "gitBranch": "workflow/InvoiceApproval/v1",
        "gitCommit": "b7c4e21",
        "contractsVersion": "V3",
        "status": "Active",
        "fixBranches": []
      }
    }
  }
}
```

The registry tracks open fix branches **per workflow family** (not per version) so the build tool knows the full picture before deciding what to auto-create vs. what to report as already in progress.

### 2.4 Fix Branches — One Per Contracts Version, Covering All Affected Workflow Versions

When a contracts change breaks multiple workflow versions, a **single fix branch per contracts version** is created — not one branch per workflow version. The fix branch contains all affected workflow versions as separate compilation targets, so the developer fixes them in one place.

```
fix/contracts-V4
  ├─ src/Workflows/OrderApproval/
  │     ├─ v1/OrderApprovalWorkflow_v1.cs   ← old logic, fixed call site
  │     └─ v2/OrderApprovalWorkflow_v2.cs   ← newer logic, same fixed call site
  ├─ src/Workflows/InvoiceApproval/
  │     └─ v1/InvoiceApprovalWorkflow_v1.cs ← if also affected
  ├─ ContractManifest.V4.json
  └─ MIGRATION_NOTES.md
```

This produces a deployment package containing all corrected DLLs:

```
/deploy/workflows/
  OrderApproval.v1.dll    ← compiled from v1 source + V4 contracts
  OrderApproval.v2.dll    ← compiled from v2 source + V4 contracts
  InvoiceApproval.v1.dll  ← compiled from v1 source + V4 contracts
```

Running v1 instances resume on their DLL. Running v2 instances resume on theirs. The fix code may be identical across versions (same call site change) or may vary per version — the migration notes call this out explicitly.

**Fix branches are never merged into main.** They feed the deployment pipeline directly for their affected workflow versions.

#### How Many Fix Branches Can Exist Per Workflow?

Normally: **one active fix branch** per contracts version. However, N fix branches can exist simultaneously if:

- Contracts versions V2 → V3 → V4 shipped rapidly and some workflow versions were never updated for V3 before V4 landed — the old versions must jump directly from V2 to V4.
- A fix branch itself was not deployed before the next contracts version shipped — the new fix branches from the previous fix branch, not from the original workflow branch.

> **Team policy:** Fix branches must be resolved and deployed within one contracts release cycle. A fix branch still open when the next contracts version ships becomes a compounding problem.

---

## 3. ContractManifest.json — Structure & Comparison

### 3.1 How It Is Generated and Distributed

There is **no `/manifest` endpoint** needed on the runner. Instead, every build of the production branch commits a new versioned manifest file:

```
/manifests/
  ContractManifest.V1.json
  ContractManifest.V2.json
  ContractManifest.V3.json    ← latest
  ContractManifest.latest.json  ← copy of V3, consumed by build tool and analyzer
```

Every build of the production branch appends a new versioned manifest. `ContractManifest.latest.json` is what the analyzer and build tool consume by default. The historical files give a full audit trail — you can diff `V2 → V3` at any time to see exactly what changed between any two contract releases.

The developer pulls the production branch and the manifest is already present — no manual download step required.

### 3.2 Manifest Structure

```json
{
  "generatedAt": "2026-05-27T10:00:00Z",
  "contractsVersion": "V4",
  "contractsAssemblyHash": "sha256:abc123...",

  "interfaces": [
    {
      "name": "ITaskHandler",
      "fullName": "ResumableWorkflows.Contracts.ITaskHandler",
      "hash": "sha256:def456...",
      "methods": [
        {
          "signature": "Task<TaskResult> ExecuteAsync(TaskRequest request, ExecutionOptions opts, CancellationToken ct)",
          "hash": "sha256:111aaa..."
        }
      ]
    },
    {
      "name": "IPaymentGateway",
      "fullName": "ResumableWorkflows.Contracts.IPaymentGateway",
      "hash": "sha256:789ghi...",
      "methods": [
        {
          "signature": "Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct)",
          "hash": "sha256:222bbb..."
        }
      ]
    }
  ],

  "serializedTypes": [
    {
      "name": "TaskRequest",
      "fullName": "ResumableWorkflows.Contracts.TaskRequest",
      "propertiesHash": "sha256:mmm777...",
      "properties": [
        { "name": "OrderId",  "type": "Guid",   "hasDefault": false },
        { "name": "Priority", "type": "int",    "hasDefault": true  }
      ]
    }
  ],

  "waitTypes": [
    {
      "name": "SignalWait",
      "fullName": "ResumableWorkflows.Contracts.SignalWait",
      "propertiesHash": "sha256:zzz999...",
      "properties": [
        { "name": "CorrelationId", "type": "string", "hasDefault": false },
        { "name": "EventName",     "type": "string", "hasDefault": false }
      ]
    },
    {
      "name": "TimerWait",
      "fullName": "ResumableWorkflows.Contracts.TimerWait",
      "propertiesHash": "sha256:yyy888...",
      "properties": [
        { "name": "TimeoutAfter", "type": "TimeSpan", "hasDefault": false }
      ]
    }
  ]
}
```

### 3.3 Comparison Layers & Severity

The comparison runs four layers in order. A hard conflict on any layer blocks the build and triggers auto-branch creation. The **fast path** is an identical `contractsAssemblyHash` — if the hash matches, all remaining layers are skipped.

| Layer | Change Type | Example | Severity |
|---|---|---|---|
| Assembly hash | Identical hash | Nothing changed | Fast pass — skip all layers |
| Interface methods | Method removed | `ExecuteAsync` deleted | **Hard — blocks build** |
| Interface methods | Signature changed | Return type changed | **Hard — blocks build** |
| Interface methods | Method added | New optional method | Safe — pass |
| Serialized types | Property removed | `OrderId` removed from `TaskRequest` | **Hard — blocks build** |
| Serialized types | Property type changed | `int → long` | **Hard — blocks build** |
| Serialized types | Property added, no default | Required field added | **Hard — blocks build** |
| Serialized types | Property added with default | `Priority = 0` added | Safe — pass |
| Wait types | **Any structural change** | `SignalWait` property altered | **Hard — always** |

> **Why wait types are unconditionally hard:** Changes here corrupt persisted wait rows in the database. Existing paused instances fail to deserialize on resume — silently in most cases. See [Section 4](#4-cases-where-an-old-workflow-cannot-be-updated) for concrete examples.

### 3.4 Comparison Output

The tool produces a structured JSON result written to `build-report.json` in the deployment package.

```json
{
  "compatible": false,
  "productionContractsVersion": "V3",
  "developmentContractsVersion": "V4",
  "breakingChanges": [
    {
      "layer": "InterfaceMethod",
      "interface": "ITaskHandler",
      "change": "SignatureChanged",
      "detail": "Task<TaskResult> ExecuteAsync(TaskRequest, CancellationToken) → ExecuteAsync(TaskRequest, ExecutionOptions, CancellationToken)",
      "severity": "Hard"
    }
  ],
  "nonBreakingChanges": [
    {
      "layer": "InterfaceMethod",
      "interface": "INotificationHandler",
      "change": "MethodAdded",
      "detail": "Task NotifyAsync(string message)",
      "severity": "Safe"
    }
  ],
  "affectedWorkflowVersions": [
    {
      "workflowName": "OrderApproval",
      "version": "v1",
      "gitBranch": "workflow/OrderApproval/v1",
      "gitCommit": "a3f9d12"
    },
    {
      "workflowName": "OrderApproval",
      "version": "v2",
      "gitBranch": "workflow/OrderApproval/v2",
      "gitCommit": "f1b8c34"
    }
  ]
}
```

The `affectedWorkflowVersions` field tells the orchestrator and Admin UI which instance groups are at risk before deployment.

---

## 4. Cases Where an Old Workflow Cannot Be Updated

There are four situations where a workflow version simply cannot be fixed and must instead be **retired**. The build tool must detect and report each of these explicitly.

### Case 1: Wait Type Structural Change (Silent Corruption)

This is the most dangerous case. A `SignalWait` or `TimerWait` row is serialized to the DB when the instance pauses. When the instance resumes, the engine deserializes that row back into the wait type. If the type layout changed between those two moments, deserialization either fails hard or — far worse — silently produces wrong data.

**The pattern across all sub-cases: no exception is thrown. The instance just never progresses.**

#### Sub-case A — Property Renamed

```csharp
// Contracts V1 — serialized to DB as: { "CorrelationId": "order-123" }
public class SignalWait
{
    public string CorrelationId { get; set; }
    public string EventName { get; set; }
}

// Contracts V2 — engine tries to deserialize old row into this
public class SignalWait
{
    public string TrackingKey { get; set; }  // renamed from CorrelationId
    public string EventName { get; set; }
}
```

`CorrelationId` in the DB row has no matching property. `TrackingKey` gets `null`. The engine tries to match an incoming signal against a `null` TrackingKey — the instance is permanently stuck. No exception. Silent stall.

#### Sub-case B — Property Type Changed

```csharp
// Contracts V1 — serialized as: { "TimeoutAfter": 3600 }  (int, seconds)
public class TimerWait
{
    public int TimeoutAfter { get; set; }
}

// Contracts V2
public class TimerWait
{
    public TimeSpan TimeoutAfter { get; set; }  // type changed
}
```

With MessagePack: hard `InvalidOperationException` at resume. With JSON: silently produces `TimeSpan.Zero` — the timer fires immediately. Either way the instance is dead.

#### Sub-case C — Property Removed

```csharp
// Contracts V1 — serialized as: { "OrderId": "abc", "CustomerId": "xyz", "Priority": 2 }
public class SignalWait
{
    public string OrderId { get; set; }
    public string CustomerId { get; set; }
    public int Priority { get; set; }   // used in mandatory part matching
}

// Contracts V2 — Priority removed
public class SignalWait
{
    public string OrderId { get; set; }
    public string CustomerId { get; set; }
}
```

The `Priority` field in the DB row is ignored on deserialization. But the mandatory part matching expression stored alongside it still references `Priority`. The match evaluator reads `0` (default) instead of `2`. Signals that should match this wait no longer do. Silent stall.

#### Sub-case D — Required Property Added (No Default)

```csharp
// Contracts V1 — serialized as: { "OrderId": "abc" }
public class SignalWait
{
    public string OrderId { get; set; }
}

// Contracts V2
public class SignalWait
{
    public string OrderId { get; set; }
    public string TenantId { get; set; }  // new, no default, now part of mandatory matching
}
```

Old rows deserialize with `TenantId = null`. Every incoming signal carries a real `TenantId` but the stored wait has `null` — no match ever succeeds. Silent stall.

**Action for all Case 1 sub-cases:** Instances cannot be recovered. They must be manually cancelled via the Admin UI and compensated externally (refund, rollback, notification). The old workflow version is retired as Dead.

> The build tool must warn at manifest comparison time when a wait type change is detected: *"Wait type changed — existing instances of these workflow versions cannot be recovered after deployment."*

---

### Case 2: Interface Semantics Fundamentally Changed

Sometimes the removed or changed method isn't just a signature tweak — the entire contract concept changed. Example: `IPaymentGateway.ChargeAsync` used to be fire-and-forget; now it returns a `ChargeResult` the workflow must inspect and branch on.

```csharp
// V1 — fire and forget
Task ChargeAsync(ChargeRequest request, CancellationToken ct);

// V2 — must inspect result and make a business decision
Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct);
```

The old workflow was written assuming no result. Adapting the old logic to use `ChargeResult` would **change the business behaviour** of already-running instances — which is not allowed for a pinned version.

**Action:** The old version must be retired. Running instances drain on the old binary, which means the old contracts DLL must stay loaded in the engine indefinitely until all instances complete. If the old contracts DLL is removed from production, those instances are permanently stuck — the build tool must surface this constraint explicitly before deployment.

---

### Case 3: Source Code or Git Branch Lost

If the `workflow/OrderApproval/v1` branch was deleted or the commit was force-pushed away, the build tool has nothing to check out. It cannot auto-create the fix branch.

**Action:** This is a process failure. The protected branch policy (Section 2.1) prevents it. If it occurs anyway, the build tool reports the missing branch explicitly and blocks. Manual recovery requires finding the commit hash from the deployed DLL's embedded `GitCommitHash` attribute and restoring the branch from there.

---

### Case 4: Fix Chain Requires Logic or Yield Point Changes

The developer checks out the fix branch, attempts to adapt the old workflow, but the adaptation requires changing **what the workflow decides** or **where it pauses** — not just how it calls an interface.

The rule: if fixing the old workflow requires changing any yield point or any business decision the workflow makes, it is no longer the same workflow version. It must become `v(N+1)`, and running `v(N)` instances must drain on the original binary.

---

### Summary

| Case | Fixable? | Action |
|---|---|---|
| Wait type structural change | No | Cancel instances, compensate externally, retire version |
| Interface semantics fundamentally changed | No | Keep old contracts DLL loaded, drain instances, retire |
| Source code / git branch lost | No (process failure) | Enforce protected branches; recover from embedded DLL metadata |
| Fix requires logic or yield point changes | No | Promote to new version `v(N+1)`, drain old instances |

---

## 5. The Custom Build Tool — `dotnet workflow`

### 5.1 Design Principles

- Single command replaces the entire manual process.
- Produces one deployment package: runner host, all workflow DLLs (old + new), manifests, build report.
- Git operations are first-class — branch reading, commit embedding, auto-branch creation.
- Every DLL carries embedded git provenance via `WorkflowBuildInfo` attributes (source-generated, never hand-written).
- Failures are actionable: error output names the exact interface, method, workflow version, and branch.
- Workflow projects reference `Workflows.Contracts` **by NuGet package version**, not by local project reference. This is the architectural decision that makes isolated fix branch compilation possible.

### 5.2 Repository Structure

```
ResumableWorkflows.sln
  ├─ src/
  │   ├─ Engine/                 ← host, runner, orchestrator
  │   ├─ Abstractions/           ← Workflows.Contracts (published to internal NuGet feed)
  │   └─ Workflows/
  │       ├─ OrderApproval/      ← references Workflows.Contracts by NuGet version
  │       └─ InvoiceApproval/
  ├─ manifests/
  │   ├─ ContractManifest.V1.json
  │   ├─ ContractManifest.V2.json
  │   └─ ContractManifest.latest.json
  └─ workflow-registry.json
```

The `Abstractions/` project is published as a NuGet package to an internal feed on every build of main. Workflow projects reference it by version:

```xml
<!-- OrderApproval.csproj -->
<PackageReference Include="Workflows.Contracts" Version="3.0.0" />
<AdditionalFiles Include="..\..\manifests\ContractManifest.latest.json" />
```

### 5.3 Commands

| Command | Description |
|---|---|
| `dotnet workflow build` | Full build: manifest comparison, compile all workflow versions, produce deployment package or create fix branch. |
| `dotnet workflow check-compat` | Dry-run: compare manifests and report conflicts without compiling or touching git. |
| `dotnet workflow list-versions` | List all workflow versions: git branch, commit hash, contracts version, status (Active / Dead). |
| `dotnet workflow retire <name> <version>` | Mark a workflow version Dead in the registry. Prevents new instance routing; draining instances continue. |

### 5.4 Build Pipeline — Step by Step

```
Step 1 — Manifest comparison
  Read ContractManifest.latest.json from /manifests/
  Generate ContractManifest.dev.json (reflection over Abstractions/ assembly)
  Run four-layer comparison (Section 3.3)
  If hard conflicts → record affected versions → go to Step 2
  If clean → go to Step 3

Step 2 — Auto-branch creation  [hard conflicts only]
  Check workflow-registry.json for existing activeFixBranch
  If no active fix branch exists for these contracts:
    git checkout -b fix/contracts-<newVersion>
    For each affected workflow version:
      Checkout source from stored gitCommit into src/Workflows/<Name>/v<N>/
      Update PackageReference in .csproj to new contracts version
    Copy ContractManifest.latest.json into branch root
    Generate MIGRATION_NOTES.md (per-version breakdown of breaking changes)
    git commit -m "chore: prepare fix branch for contracts <newVersion>"
    Update workflow-registry.json activeFixBranch fields
  If fix branch already exists:
    Report branch name, tell developer to resolve it
  Exit with non-zero code — developer resolves branch then re-runs

Step 3 — Inject git provenance
  For each workflow version (main + resolved fix branches):
    git log -1 --format="%H %D %s"  to read commit metadata
    Generate WorkflowBuildInfo.g.cs with [assembly: WorkflowBuildInfo(...)]

Step 4 — Compile all workflow DLLs
  dotnet build each workflow project against new contracts NuGet package
  Roslyn analyzers WF1001 and WF1002 run as part of compilation
  Any compile error aborts the build

Step 5 — Produce deployment package
  /deploy/
    runner-host/
    workflows/
      OrderApproval.v1.dll   (+ .pdb)
      OrderApproval.v2.dll   (+ .pdb)
      InvoiceApproval.v1.dll (+ .pdb)
    manifests/
      ContractManifest.latest.json
      ContractManifest.dev.json
    build-report.json   (full comparison output, Section 3.4)
```

### 5.5 Auto-Generated `MIGRATION_NOTES.md`

```markdown
# Migration Notes — Contracts V4

Generated : 2026-05-27
Fix branch: fix/contracts-V4

## Affected Workflow Versions

### OrderApproval / v1  (source: workflow/OrderApproval/v1 @ a3f9d12)

#### Breaking Changes

**ITaskHandler**
- REMOVED : `Task<TaskResult> ExecuteAsync(TaskRequest, CancellationToken)`
- REPLACED: `Task<TaskResult> ExecuteAsync(TaskRequest, ExecutionOptions, CancellationToken)`
- Action   : Add `new ExecutionOptions()` to every call site in `OrderApprovalWorkflow_v1.cs`.

---

### OrderApproval / v2  (source: workflow/OrderApproval/v2 @ f1b8c34)

#### Breaking Changes

**ITaskHandler**
- Same change as v1 above. Call site is identical — same fix applies.

---

## What to do

1. Apply fixes listed above per version file.
2. Run: `dotnet build`  — must compile cleanly.
3. Push this branch. CI validates and includes all DLLs in the deployment package.

> !! Do NOT merge this branch into main. !!
> This branch feeds the deployment pipeline directly.
> Fix branches for different workflow versions are resolved here together.
```

### 5.6 Fix Branch Internal Structure

A fix branch contains **only workflow source files** — no host or engine code. It references the host contracts via NuGet.

```
fix/contracts-V4/
  src/Workflows/OrderApproval/
    v1/
      OrderApprovalWorkflow_v1.cs     ← old logic, fixed call site
      OrderApproval.v1.csproj         ← PackageReference bumped to V4
    v2/
      OrderApprovalWorkflow_v2.cs     ← newer logic, same fixed call site
      OrderApproval.v2.csproj         ← PackageReference bumped to V4
  src/Workflows/InvoiceApproval/
    v1/
      InvoiceApprovalWorkflow_v1.cs
      InvoiceApproval.v1.csproj
  manifests/
    ContractManifest.V4.json
  MIGRATION_NOTES.md
  WorkflowBuildInfo.g.cs              ← regenerated with fix branch commit
```

The git operations the build tool performs:

```bash
# 1. Read pinned commits from workflow-registry.json
V1_COMMIT=$(jq -r '.OrderApproval.versions.v1.gitCommit' workflow-registry.json)
V2_COMMIT=$(jq -r '.OrderApproval.versions.v2.gitCommit' workflow-registry.json)

# 2. Create fix branch from main HEAD (host/contracts are always current)
git checkout main
git checkout -b fix/contracts-V4

# 3. For each affected version, restore source from its pinned commit
git checkout $V1_COMMIT -- src/Workflows/OrderApproval/
mv src/Workflows/OrderApproval/ src/Workflows/OrderApproval/v1/

git checkout $V2_COMMIT -- src/Workflows/OrderApproval/
mv src/Workflows/OrderApproval/ src/Workflows/OrderApproval/v2/

# 4. Bump contracts package version in each .csproj
sed -i 's/Workflows.Contracts" Version=".*"/Workflows.Contracts" Version="4.0.0"/' \
    src/Workflows/OrderApproval/v1/OrderApproval.v1.csproj

# 5. Drop in manifest and notes
cp manifests/ContractManifest.V4.json .
generate_migration_notes > MIGRATION_NOTES.md

# 6. Commit baseline — developer starts from here
git add -A
git commit -m "chore: prepare fix branch for contracts V4 [auto-generated]"
```

---

## 6. Live Analyzer Warnings in Visual Studio

`ContractManifest.latest.json` is declared as an `AdditionalFile` in every workflow `.csproj`:

```xml
<ItemGroup>
  <AdditionalFiles Include="..\..\manifests\ContractManifest.latest.json" />
</ItemGroup>
```

Roslyn passes `AdditionalFiles` to analyzers at analysis time — not just at build time. The **WF1001** analyzer reads the manifest during editing and compares every interface call site in the workflow source against the production contract model in real time.

```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ContractCompatibilityAnalyzer : DiagnosticAnalyzer
{
    public override void Initialize(AnalysisContext context)
    {
        context.RegisterCompilationStartAction(ctx =>
        {
            var manifest = ctx.Options.AdditionalFiles
                .FirstOrDefault(f => f.Path.EndsWith("ContractManifest.latest.json"));

            if (manifest == null) return;

            var productionContracts = ParseManifest(manifest.GetText().ToString());

            ctx.RegisterOperationAction(opCtx =>
            {
                if (opCtx.Operation is IInvocationOperation invocation)
                    CheckInvocationAgainstManifest(invocation, productionContracts, opCtx);
            }, OperationKind.Invocation);
        });
    }
}
```

The result: the developer sees a red squiggly in Visual Studio the moment they call a method that no longer exists in the production contracts — before running any build command, before pushing to CI.

Since the manifest is committed to the production branch, pulling main is sufficient for any developer to get the latest production contract model in their IDE automatically.

---

## 7. Database — Instance Version Pinning

Every persisted workflow instance row stores enough provenance to enforce version pinning at resume time and to populate the Admin UI with full traceability.

| Column | Type | Purpose |
|---|---|---|
| `WorkflowName` | string | Logical workflow name, e.g. `OrderApproval` |
| `WorkflowVersion` | int | Version number pinned at instance creation |
| `ContractsVersion` | string | Contracts version string, e.g. `V3` |
| `ContractsHash` | string | Hash from manifest at build time — must match loaded DLL at resume |
| `GitCommitHash` | string | Short SHA of the commit that produced this DLL |
| `GitBranch` | string | Branch the DLL was built from |
| `CurrentWaitId` | int | ID of the wait the instance is currently paused at |
| `Status` | enum | `Active` / `Draining` / `Completed` / `Cancelled` |

**Runtime safety net:** The engine refuses to resume an instance if the loaded DLL's `ContractsHash` does not match the row's `ContractsHash`. All other checks are build-time; this is the final guard against a deployment error slipping through.

---

## 8. Roslyn Analyzer Rules

| Rule | Name | Severity | Description |
|---|---|---|---|
| WF1001 | Contract Incompatibility | **Error** | Workflow calls a method or uses an interface removed or deprecated in `ContractManifest.latest.json`. Triggers in-editor (AdditionalFile) and at compile time. |
| WF1002 | Illegal State Capture | **Error** | Local variable or closure captured across a yield point cannot be safely serialized. Developer must use `.WithState<T>()`. |

Both rules are **compiler errors, not warnings.** Warnings get suppressed under deadline pressure. Either issue corrupts running instances and must never reach production.

---

## 9. Summary of Responsibilities

| Component | Responsibility |
|---|---|
| `Workflows.Contracts` | Immutable interface layer. Any breaking change here triggers the full compatibility gate. Published to internal NuGet feed on every main build. |
| `/manifests/ContractManifest.Vx.json` | Committed to production branch on every build. Source of truth for the developer build flow and VS analyzer. Replaces the need for a `/manifest` runtime endpoint. |
| `workflow-registry.json` | Maps every workflow version to its git branch, pinned commit, contracts version, status, and open fix branches. Read by the build tool in Step 1. |
| `dotnet workflow build` | Manifest comparison → auto-branch creation → git provenance injection → compile → deployment package. |
| `WorkflowBuildInfo.g.cs` | Source-generated file embedding git commit hash, branch, contracts version and hash into every DLL. Never written manually. |
| `fix/contracts-Vx` branch | One fix branch per contracts version covering all affected workflow versions. Never merged to main. Feeds deployment pipeline directly. |
| Orchestrator registry | Stores version-to-DLL mapping. Routes new instances to latest active version. Reads `WorkflowBuildInfo` attributes at DLL load time. |
| `ContractsHash` (DB column) | Runtime safety net: refuses to resume an instance if DLL hash does not match stored hash. |
| WF1001 Roslyn analyzer | Catches contract incompatibilities in-editor via `AdditionalFile` and at compile time. Compiler error. |
| WF1002 Roslyn analyzer | Catches illegal state captures across yield points. Compiler error. Enforces `.WithState<T>()` usage. |
| Protected branch policy | Workflow version branches cannot be force-pushed or deleted. Build tool verifies branch and commit exist before proceeding. |

---

*End of document. For questions contact the ResumableWorkflows core team.*
