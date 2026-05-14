## 2025-05-14 - [Optimizing Reflection with FastExpressionCompiler]
**Learning:** Reflection-based method and delegate invocation (`MethodInfo.Invoke` or `Delegate.DynamicInvoke`) in high-frequency paths like signal matching and workflow advancement significantly impacts performance. Since `FastExpressionCompiler` is already a project dependency, it should be used to compile and cache invokers.
**Action:** Always prefer cached compiled delegates over direct reflection for workflow execution logic. Use `WorkflowTemplateCache` to store these delegates.
