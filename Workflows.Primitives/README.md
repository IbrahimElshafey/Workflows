# Workflows.Primitives

## 1. What is this?
A lightweight shared library (targeting `netstandard2.1`) containing fundamental enum, record, and struct definitions used across the entire workflow engine. This project has zero external dependencies, making it safe to reference from any layer of your application.

Key definitions include:
- `WaitType` (e.g., Command, Signal, Delay, Group, Compensation)
- `CommandExecutionMode` (e.g., Immediate, Deferred)
- `RegistrationSyncResult` and `RegistrationError`

## 2. How to use?
Add a reference to this project in any component of your system that needs access to the base workflow primitives.

### Example:
```csharp
using Workflows.Primitives;

// Using primitives to check wait types
if (wait.Type == WaitType.Signal)
{
    // Process signal logic
}
```
