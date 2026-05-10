
## 2026-05-10 - [Optimizing reflection with FastExpressionCompiler]
**Learning:** Hardcoding argument lists in Expression.Call for methods that might be parameterless (like state machine entry points) leads to ArgumentException at runtime. Always check the MethodInfo parameters before building the call expression.
**Action:** Use conditional logic when building Expression.Call trees based on the target method's ParameterInfo[].
