# Workflows.Common (Workflows.Shared)

A shared infrastructure implementation library (targeting `netstandard2.1`) for the **Workflows Engine**.

---

## 🚀 Key Features

* **Expression Serialization:** Integrates `Nuqleon.Linq.Expressions.Bonsai.Serialization` and `FastExpressionCompiler` to safely serialize and deserialize C# lambda match expressions and state machine trees across process boundaries.
* **JSON Serialization Helpers:** Implements `System.Text.Json` object serialization adapters (`IObjectSerializer`) handling custom state payloads and wait DTO conversions.
* **Dependency Injection Extensions:** Provides `AddWorkflowsShared()` service registration extension methods for host bootstrapping.

---

## 🛠️ Usage

Reference this project in runtime hosts or worker assemblies:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Workflows.Common;

var services = new ServiceCollection();

// Register expression tree serializers and JSON formatting services
services.AddWorkflowsShared();
```
