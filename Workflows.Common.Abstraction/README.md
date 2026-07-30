# Workflows.Common.Abstraction

The central abstraction and contract definition library (targeting `netstandard2.1`) for the **Workflows Engine**.

---

## 🚀 Key Features

* **Serialization Contracts:** Defines interfaces for expression serialization (`IExpressionSerializer`), JSON serialization (`IObjectSerializer`), and type converters.
* **Wait DTO Abstractions:** Defines shared internal/external wait DTO abstractions consumed by both `Workflows.Runner` and `Workflows.Orchestrator`.
* **Logging & Messaging Contracts:** Shared logging adapters and core abstraction contracts.

---

## 🛠️ Usage

Reference this package when building custom transport libraries, storage adapters, or client host extensions:

```xml
<ProjectReference Include="..\Workflows.Common.Abstraction\Workflows.Common.Abstraction.csproj" />
```
