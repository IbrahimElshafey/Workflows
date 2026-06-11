Welcome to the **Workflows Engine** wiki!

The Workflows Engine is a high-performance, lightweight, snapshot-serialized workflow engine for .NET. Rather than using event-sourcing and expensive event replay (like Temporal or Durable Functions) to rebuild execution state, this engine directly serializes the compiler-generated C# state machine state and container properties into JSON. This results in sub-millisecond execution ticks and instant resumption.

## 📖 Wiki Navigation

Use the sidebar or the links below to navigate the documentation:

*   **[Architecture Overview](Architecture-Overview)**: Understand the hybrid database design, execution cycle, and stateless compute model.
*   **[Writing Workflows](Writing-Workflows)**: A developer guide to authoring workflows, including wait primitives, sub-workflows, and the strict no-closure rule.
*   **[In-Process Hosting](In-Process-Hosting)**: Learn how to set up and run the unified in-process SQLite-backed host.

---

## 🚀 Quick Start

To see the engine in action immediately:

1.  **Prerequisites**: Ensure you have the [.NET 10.0 SDK](https://dotnet.microsoft.com/download) installed.
2.  **Run the SQLite CLI Sample**:
    ```bash
    dotnet run --project Samples/InProcessSqliteSample/InProcessSqliteSample.csproj
    ```
3.  **Explore**: Use the interactive CLI dashboard to start workflows, inspect wait tables in SQLite, dispatch signals, and review step logs.
