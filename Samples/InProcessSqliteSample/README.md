# InProcessSqliteSample

An interactive console sample application showing the real-world usage of `Workflows.Hosting.InProcess` with a SQLite database provider.

## What is this?
This sample runs the workflow orchestrator, background scheduler, loopback message transport, and execution runner entirely in-process. It persists workflow state and indexes active waits using SQLite.

It defines a rich, multi-step **E-Commerce Order Processing Workflow** that demonstrates:
1. **Deferred Commands**: Executing asynchronous tasks outside the workflow engine boundary (e.g. `AuthorizePayment`, `ShipOrder`).
2. **Parallel Signaling (`WaitGroup`)**: Waiting for multiple incoming signals (`StockConfirmed` and `CustomerVerified`) concurrently.
3. **Domain State Preservation**: Retaining custom workflow variables across suspensions and resumes.
4. **Execution Logs**: Accumulating steps in a log list that is persisted as part of the instance's state.

## How to use?
Run the project from the command line from the root of the repository:

```bash
dotnet run --project Samples/InProcessSqliteSample/InProcessSqliteSample.csproj
```

Once started, the interactive CLI dashboard allows you to:
1. **Start a new Order workflow** by entering an Order ID, customer email, and purchase amount.
2. **List all active instances** and inspect their active Phase 1 DB wait index records (`SignalWaitEntity`, `CommandWaitEntity`).
3. **Simulate signals** like stock confirmation and customer verification.
4. **View intercepted deferred commands** and simulate successful or failed external system execution results.
5. **Inspect the detailed execution log** and domain-level state variables of a selected workflow instance.
