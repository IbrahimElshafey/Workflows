# Telemetry & OpenTelemetry Integration

This document specifies the design for distributed tracing, metric collection, and structured logging in the Workflows engine, fully aligning with **OpenTelemetry (OTel)** standards.

---

## 1. Trace Context Propagation

In distributed deployments, execution starts with an external API signal, flows through a message queue, triggers Orchestrator database operations, and runs on a stateless Runner. To track the execution flow, we propagate **W3C TraceContext** headers (`traceparent`, `tracestate`) across boundaries.

```
[External Request] 
      │ (Inject Trace Header: traceparent=00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01)
      ▼
[SignalsController (API Gateway)]
      │ (Publish to Queue with Trace Headers)
      ▼
[Message Bus / Broker]
      │ (Consume Message & Extract Trace Context)
      ▼
[Orchestrator] ──(Create child span: "DeliverSignal")──> [Database (Index Lock)]
      │ (Dispatch RunWorkflowCommand with Trace Context)
      ▼
[Stateless Runner] ──(Create child span: "Runner.ExecuteTick")──> [C# Workflow Run]
```

---

## 2. Distributed Tracing Spans

The engine declares a dedicated `ActivitySource` named `Workflows.Engine`.

### Key Spans & Attributes
1.  **`Orchestrator.DeliverSignal`**
    *   `workflow.instance_id` (Guid)
    *   `signal.path` (String)
    *   `messaging.system` (e.g., `rabbitmq`, `http`)
2.  **`Runner.ExecuteTick`**
    *   `workflow.instance_id` (Guid)
    *   `workflow.type_name` (String)
    *   `workflow.state_machine.step_index` (Int)
    *   `workflow.runner.execution_mode` (e.g., `InProcess`, `Distributed`)
3.  **`CommandHandler.Execute`**
    *   `workflow.command.type` (String)
    *   `workflow.command.execution_mode` (Immediate/Deferred)

---

## 3. Metrics & OpenTelemetry Instruments

Metrics are gathered using `System.Diagnostics.Metrics` and exposed to Prometheus/OTel collectors.

### System Meters
*   **`workflows.instance.started`** (Counter): Total workflows initialized.
*   **`workflows.instance.completed`** (Counter): Total workflows completed.
*   **`workflows.instance.failed`** (Counter): Total workflows faulted.
*   **`workflows.tick.duration`** (Histogram): Milliseconds spent running the stateless compute tick (aiming for `<10ms` in 95th percentile).
*   **`workflows.state.size`** (Histogram): Byte size of the serialized `StateObject` to monitor payload size bloat.
*   **`workflows.wait.count`** (UpDownCounter): Active waits indexed in SQL routing tables, categorized by type (`Signal`, `Timer`, `Command`).

---

## 4. Structured Logging with Scopes

To ensure log searchability in aggregator tools (e.g., Elasticsearch, Seq, Datadog), every log statement emitted during execution must include correlation properties.

We use **Logging Scopes** (`ILogger.BeginScope`) in the Orchestrator pipeline to automatically append context parameters to all child log statements.

```csharp
public async Task ProcessSignalAsync(Guid instanceId, string signalPath, object payload)
{
    using (_logger.BeginScope(new Dictionary<string, object>
    {
        ["WorkflowInstanceId"] = instanceId,
        ["SignalPath"] = signalPath,
        ["TraceId"] = Activity.Current?.TraceId.ToString()
    }))
    {
        _logger.LogInformation("Processing incoming signal.");
        
        // All logs emitted inside this block (even deep in the Runner)
        // automatically inherit WorkflowInstanceId and TraceId columns.
        await _orchestrator.ExecuteAsync(instanceId, signalPath, payload);
    }
}
```
