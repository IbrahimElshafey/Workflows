# Workflows.Shared

## 1. What is this?
A utility library (targeting `netstandard2.1`) that provides default implementations for serialization and registers shared services for dependency injection.

Key features:
- **Expression Serialization**: Uses Nuqleon Bonsai and FastExpressionCompiler to serialize complex C# lambda expressions safely, allowing workflow match rules and execution trees to be saved to and loaded from database stores.
- **JSON Serialization**: Implements the `IObjectSerializer` interface using `System.Text.Json` to handle standard payload conversion.
- **DI Bootstrapping**: Exposes standard methods to set up core infrastructure services.

## 2. How to use?
Reference this project in your host startup projects and register the shared services using the dependency injection extensions.

### Example:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Workflows.Common;

var services = new ServiceCollection();

// Registers expression serializers and JSON serialization providers
services.AddWorkflowsShared();
```
