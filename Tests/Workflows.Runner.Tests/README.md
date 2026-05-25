# Workflows.Runner.Tests

## 1. What is this?
The main unit and integration test suite (targeting `net10.0` using xUnit) for the `Workflows.Runner` and surrounding workflow execution frameworks.

Key components covered:
- Workflow state machine execution loops.
- Variable scopes and state serialization.
- Match conditions and compilation performance.
- Wait registration, resolution, and compensation logic.

## 2. How to use?
Run the tests using Visual Studio Test Explorer or through the command line via the .NET CLI.

### Run command:
```bash
dotnet test Tests/Workflows.Runner.Tests/Workflows.Runner.Tests.csproj
```
