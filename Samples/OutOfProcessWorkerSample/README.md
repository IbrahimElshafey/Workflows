# OutOfProcessWorkerSample

A sample application demonstrating **Out-of-Process Worker Supervision** (`WorkerProcessSupervisor`) and **CLI Tooling Integration** (`dotnet-wf`).

---

## 🚀 Overview

This sample showcases:
1. **`dotnet-wf` Schema & Artifact Extraction:** Calling `WorkflowSchemaGenerator`, `DeploymentManifestGenerator`, and `MigrationBoilerplateGenerator` programmatically to generate JSON schemas, pre-publish manifests, and C# migration code.
2. **Worker Process Supervision (`WorkerProcessSupervisor`):** Spawning isolated out-of-process worker sub-processes, establishing Named Pipe IPC connections, executing handshakes, and dispatching execution commands.
3. **Graceful Sub-Process Management:** Shutting down worker sub-processes gracefully via IPC signals.

---

## 🛠️ How to Run

```bash
dotnet run --project Samples/OutOfProcessWorkerSample/OutOfProcessWorkerSample.csproj
```

---

## 📄 Key Steps Demonstrated in Code

```csharp
// 1. Programmatic CLI schema & artifact extraction
string schemaFile = WorkflowSchemaGenerator.GenerateSchemaJson(sampleAsm, outputDir);
string manifestFile = DeploymentManifestGenerator.GenerateManifest("V1.dll", "V2.dll", outputDir);
string migrationFile = MigrationBoilerplateGenerator.GenerateMigrationClass("OrderProcessingWorkflow", 1, 2, outputDir);

// 2. Initialize Worker Process Supervisor
var supervisor = new WorkerProcessSupervisor(workerDllPath);

// 3. Spawn and handshake with worker sub-process
var workerInfo = await supervisor.EnsureWorkerAsync("1.0.0", assemblyPath: null, ct: cancellationToken);

// 4. Send Named Pipe IPC command to sub-process
bool success = await supervisor.SendIpcCommandAsync("1.0.0", "Handshake", "", cancellationToken);

// 5. Graceful shutdown
await supervisor.ShutdownWorkerAsync("1.0.0", cancellationToken);
```
