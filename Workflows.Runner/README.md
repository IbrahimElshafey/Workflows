# Workflows.Runner

## 1. What is this?
The stateless execution engine (targeting `net10.0`) for the workflows system. It drives individual workflow instances forward through their state machines and never touches the database directly.

Key features:
- **State Machine Execution**: hydrates workflow instances from a serialized snapshot, advances the `IAsyncEnumerable<Wait>` iterator and returns the new state plus the yielded waits.
- **Match Compilation**: compiles signal/command match expressions with `FastExpressionCompiler`, caching templates to avoid re-parsing.
- **Exact Matching**: validates that an incoming signal, command result or timer satisfies the workflow's `MatchIf` expressions.
- **Migration**: hosts `WorkflowMigrationExecutor`, which applies V1 → V2 migrations (see `_Documents/Roadmap/Versioning/Versioning - Workflow Migration.md`).

## 2. How to use?
The runner is normally registered by the host. Add it next to the shared services:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Workflows.Runner;
using Workflows.Shared;   // AddWorkflowsShared (Workflows.Common.Abstraction)

var services = new ServiceCollection();

services.AddWorkflowsShared();   // serializers + transport factory
services.AddWorkflowsRunner();   // state machine runner, wait serializers, match compiler
```

`AddWorkflowsInProcessHost(connectionString)` (from `Workflows.Hosting.InProcess`) builds on top of these registrations and adds the orchestrator, scheduler, workers and channels.