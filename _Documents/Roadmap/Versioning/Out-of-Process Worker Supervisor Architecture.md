# 🏛️ Out-of-Process Worker Supervisor Architecture

## 1. Executive Summary

To eliminate cross-version dependency conflicts, shared static state leakage, and memory reference traps associated with in-process dynamic assembly loading (such as `AssemblyLoadContext`), the Workflows Engine utilizes an **Out-of-Process Worker Supervisor Architecture**.

Under this model:
- The **Main Engine Host** acts as a **Supervisor (Control Plane)**.
- Each version of a workflow assembly runs inside a dedicated, isolated **Worker Sub-Process (Data Plane)**.
- Communication between the Host Supervisor and Worker processes uses ultra-fast local IPC (gRPC over Named Pipes or Unix Domain Sockets).
- Deployment actions are guided deterministically by reading **`deployment-manifest.json`** combined with live DB count evaluations.

---

## 2. Process Lifecycle & Architectural Guarantees

### 1. Archived Binary Respawn Guarantee
If a Worker process for `Version N` was previously decommissioned and a late trigger (such as a delayed saga compensation 3 days later) requires executing `Version N` logic:
- The Host Supervisor reads the exact version binary archive (`/Archive/Binaries/VN/Workflows.Worker.dll`) registered for `WorkflowVersion = N`.
- It spawns: `dotnet Workflows.Worker.dll --version N --archive-path /Archive/Binaries/VN`.
- **Version Purity:** The Host **never** spins up latest V2 code to run a V1 instance.

### 2. Rollback & Late Compensation Concurrency Guard
During the 15-minute `DrainingGracePeriod` post-migration:
- If a post-migration anomaly triggers a DB rollback to `StateObject_PreMigration_V1` while a late compensation event arrives concurrently:
- **`InstanceLockManager` Serialization:** All state migrations, rollbacks, and incoming signal/compensation dispatches for a given `WorkflowInstanceId` are strictly serialized using the Orchestrator's row-level `InstanceLockManager`.
- The rollback acquires the instance lock first. The incoming compensation request waits in queue until the rollback completes, then executes cleanly against the pre-migration V1 state on Worker V1. Zero race condition exists.

---

## 3. Advantages over In-Process ALC

| Dimension | In-Process ALC | Out-of-Process Worker Supervisor |
| :--- | :--- | :--- |
| **Dependency Isolation** | 🟡 Risk of assembly binding conflicts | 🟢 **100% Process Boundary Isolation** |
| **Static State Safety** | 🔴 Static variables bleed across ALCs | 🟢 **Zero Bleed** (Isolated memory) |
| **Memory Reclamation** | 🔴 Cooperative GC unloading traps | 🟢 **OS Level Reclamation** on Process Exit |
| **Crash Resilience** | 🔴 Unhandled Worker exception crashes host | 🟢 Worker crash isolated from Host Supervisor |
| **Deterministic Deploy** | 🟡 Host guesses runtime behavior | 🟢 **Guided by `deployment-manifest.json` + Live DB** |
