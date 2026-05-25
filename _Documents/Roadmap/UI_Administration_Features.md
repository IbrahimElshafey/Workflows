# UI Administration Dashboard: Planned Features

This document outlines the planned features, user interface screens, and administrative operations for the **Workflows Engine Administration Dashboard**. 

The dashboard provides administrators and developers with complete visibility into the workflow cluster, execution tracing, and manual lifecycle control.

---

## 1. Dashboard Overview & Analytics
A high-level dashboard displaying cluster health, execution metrics, and throughput.

* **Key Metrics**:
  - Total Active Workflows (Running vs. Suspended).
  - Failure Rate / Faulted Instances Count (with daily trend charts).
  - Average execution latency per step (seconds/milliseconds).
  - Total database size and index statistics (e.g., number of active wait rows).
* **Alert Center**: Highlight active "Broadcast Storms" (listeners matching high numbers of candidates) and instances stuck in retries or infinite loop crash states.

---

## 2. Workflow Definitions & Topology Explorer
Allows operators to browse the registered workflow definitions across the cluster.

* **Version History**: Group definitions by `WorkflowName` and list all registered version semver strings (e.g., `1.0.0`, `1.1.0`).
* **Visual Topology Diagram**: Render a visual DAG (Directed Acyclic Graph) showing the workflow execution path. Highlight yield steps (WaitSignal, TimeWait, SubWorkflowWait) and command nodes.
* **Metadata Inspector**: View assembly details, registration date, and custom developer documentation strings.

---

## 3. Workflow Instance Monitor
A searchable database list of every active and completed workflow execution run.

* **Search & Filter**: Find instances by Instance ID, Registration ID, status (`Running`, `Suspended`, `Completed`, `Faulted`), or custom variable metadata tags.
* **Variable Inspector (JSON)**: Inspect the current state of variables in the `WorkflowContainer` closure, updated after the last execution tick.
* **Wait Status Tree**: Expand the current active wait state. For composite waits (`GroupWait`), show which child conditions have been completed and which are still pending.

---

## 4. Visual Execution Trace Viewer
A timeline and flow representation of a single workflow instance's execution history.

* **Linear Timeline**: A step-by-step history showing when signals were received, when commands were executed, and how long waits remained active.
* **Saga Compensation Visualizer**: Highlight command steps that have registered compensation logic. Show the LIFO (Last-In-First-Out) execution tree when compensation gets triggered, marking which compensation tasks succeeded or failed.
* **Nested Sub-Workflow Cascader**: Drill down into nested sub-workflow instances within a parent run, viewing their isolated execution logs and state objects inline.

---

## 5. Instance Control Panel (Manual Actions)
Provides administrators with operational commands to recover or control instances.

* **Manual Signal Dispatcher**: Select a suspended workflow, view its active `SignalWait` paths, and manually post a signal payload directly to that wait point to force-resume execution.
* **Lifecycle Management**:
  - **Start Instance**: Kick off a new workflow run by selecting a definition, specifying a version, and inputting starting JSON payloads.
  - **Pause / Resume**: Suspend active processing or resume a paused engine state.
  - **Terminate**: Force-cancel a workflow, terminating execution and scheduling database pruning of its indexes.
* **Manual Bypass & Cancellation**: Trigger manual cancellation tokens (`CancelToken`) to prune execution branches or skip specific wait points.
* **Force-Compensate**: Trigger compensation for specific sagas or tokens manually for emergency rollback.
