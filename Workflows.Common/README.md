# Workflows.Common (Workflows.Shared)

A shared infrastructure implementation library (targeting `netstandard2.1`) for the **Workflows Engine**. The project file is `Workflows.Shared.csproj` and the public namespace is `Workflows.Shared` — not `Workflows.Common`.

---

## 🚀 Key Features

* **Expression Serialization:** integrates `Nuqleon.Linq.Expressions.Bonsai.Serialization` and `FastExpressionCompiler` to serialize/deserialize C# lambda match expressions across process boundaries.
* **JSON Serialization Helpers:** `System.Text.Json` based `IObjectSerializer` handling state payloads and wait DTOs.
* **Dependency Injection Extensions:** `AddWorkflowsShared()` registers the serializers and the default transport factory used by hosts.

---

## 🛠️ Usage

Reference this project from runtime hosts or worker assemblies:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Workflows.Shared;   // note: namespace is Workflows.Shared

var services = new ServiceCollection();

// Register expression-tree serializers, JSON serializers and ITransportFactory
services.AddWorkflowsShared();
```

`AddWorkflowsShared()` is the base registration; `AddWorkflowsRunner()` (Workflows.Runner) and `AddWorkflowsInProcessHost(connectionString)` (Workflows.Hosting.InProcess) build on top of it.