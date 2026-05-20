## 2025-05-15 - [Reflection Overhead in Workflow Runner]
**Learning:** Using `MethodInfo.Invoke` on the hot path (workflow entry points and signal match actions) introduces significant overhead that can be avoided by caching delegates compiled via `FastExpressionCompiler`.
**Action:** Always prefer cached compiled expressions over direct reflection for frequently executed code paths. Ensure that dependencies like `FastExpressionCompiler` are already present in the project or explicitly allowed before adding them.

## 2025-05-15 - [Robust Compiled Member Extraction]
**Learning:** When replacing reflection with compiled expression trees for performance, it's critical to handle variations in member types (Field vs. Property) and naming inconsistencies (e.g., `Tokens` vs. `CompensationTokens`). Automated extractors must be polymorphic enough to look for both to avoid breaking existing logic.
**Action:** Always check both `GetProperty` and `GetField` when building compiled member extractors for internal workflow components, and include alias checks for common property names used across different versions of the engine.
