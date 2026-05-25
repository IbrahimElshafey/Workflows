# Workflows.Analyzers

## 1. What is this?
A Roslyn static code analyzer and source generator library (targeting `netstandard2.0`) designed to run at compilation time. 

Because workflow state machines are serialized and executed asynchronously across different processes, they must follow strict engine rules. This analyzer automatically validates developers' C# workflow definitions to prevent common mistakes before they hit runtime.

Key validation checks include:
- Enforcing that all workflow container classes are declared as `sealed`.
- Ensuring lambda expressions (such as match filters) do not capture variables from external scopes (closures).
- Preventing usage of unserializable local types in lambda expressions.
- Disallowing forbidden runtime-specific calls (e.g., `AsyncLocal` or `HttpContext`) that break in distributed execution environments.

## 2. How to use?
Reference the project as an Analyzer inside your project's `.csproj` file. This prevents the analyzer assembly from being bundled as a direct runtime dependency while enabling build-time analysis.

### Example configuration in `.csproj`:
```xml
<ItemGroup>
  <ProjectReference Include="..\Workflows.Analyzers\Workflows.Analyzers.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```
