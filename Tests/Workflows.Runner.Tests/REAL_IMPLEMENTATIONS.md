# Test Infrastructure - Real Implementations (No Mocking)

The runner test suite uses **real implementations** behind every interface instead of a mocking framework. Integration-level bugs that mocks would hide (serialization round-trips, state cloning, lock contention) surface in the tests.

All helpers live in `Tests/Workflows.Runner.Tests/Infrastructure/`.

## Implementations

| Interface | Test implementation | Behaviour |
| :--- | :--- | :--- |
| `IWorkflowRegistry` | `InMemoryWorkflowRegistry` | Dictionary-backed registry for workflows, signals and command types; entries added directly, no setup ceremony. |
| `IWorkflowRunnerClient` | `InMemoryWorkflowRunnerClient` | No-op client that captures sent results for assertion instead of performing I/O. |
| `ICommandHandlerFactory` | `InMemoryCommandHandlerFactory` | Registers real command handlers via lambdas; synthesizes default results for unregistered commands. |
| `IServiceProvider` | `TestServiceProvider` | Builds workflow instances with `Activator.CreateInstance`, falling back to `Microsoft.Extensions.DependencyInjection` for services. |
| `IObjectSerializer` | `TestObjectSerializer` | `System.Text.Json`-based serialization honouring the `SerializationScope` parameter. |
| `IExpressionSerializer` | `TestExpressionSerializer` | Keeps expressions in memory; deserialization throws because tests reconstruct from source. |

`WorkflowTestBuilder` wires these into a runner instance and is the entry point most tests use.

## Removed helpers

Earlier revisions documented `TestDelegateSerializer` (`IDelegateSerializer`) and `TestClosureContextResolver` (`IClosureContextResolver`). Those interfaces no longer exist in the codebase — delegate and closure state is captured by the compiled expression pipeline — so the helpers were deleted.

## Fixtures

Reusable workflow definitions live in `Tests/Workflows.Runner.Tests/TestWorkflows/` (cancellation, compensation, versioning, fan-out, nested groups, sub-workflows, SxS). Shared DTOs live in `TestData/TestDataTypes.cs`.