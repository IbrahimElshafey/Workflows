# Workflows.Runner

## 1. What is this?
The core runtime execution engine (targeting `netstandard2.1`) for the workflows system. It is responsible for driving individual workflow instances forward through their defined state machines.

Key features:
- **State Machine Execution**: Hydrates workflow instances, interprets the yield-returned yields/waits, and manages step execution context.
- **Match Compilation**: Dynamically compiles signal and result matching lambda expressions using `FastExpressionCompiler` for high-performance execution.
- **Wait Resolution**: Coordinates matching of incoming events (signals, command execution results, delays) to current active waits.

## 2. How to use?
Register runner services in your DI container. Typically, the runner is used by a hosting application or worker node that process signals and commands.

### Example:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Workflows.Runner;

var services = new ServiceCollection();

// Register runner services
services.AddWorkflowsRunner();
```
