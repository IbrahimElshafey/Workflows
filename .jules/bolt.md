## 2025-05-15 - [Reflection Overhead in Workflow Runner]
**Learning:** Using `MethodInfo.Invoke` on the hot path (workflow entry points and signal match actions) introduces significant overhead that can be avoided by caching delegates compiled via `FastExpressionCompiler`.
**Action:** Always prefer cached compiled expressions over direct reflection for frequently executed code paths. Ensure that dependencies like `FastExpressionCompiler` are already present in the project or explicitly allowed before adding them.
