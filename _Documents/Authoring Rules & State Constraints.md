Yes, absolutely. Building a **Roslyn Analyzer** is not just a good idea; it is the industry standard for this exact architectural pattern. Frameworks like Azure Durable Functions and Temporal rely heavily on Roslyn Analyzers to enforce state machine rules at compile-time because runtime serialization exceptions are too late in the feedback loop.

By writing a custom analyzer, you can parse the syntax tree as the author types in Visual Studio, instantly red-squiggling the `await foreach` and forcing them to use `WaitSubWorkflow`.

Here is the formal documentation for your engine's DSL constraints, combined with the blueprint for the Roslyn Analyzer rules you should build to enforce them. You can add this directly to your project wiki.

---

# 📖 Workflows.Definition: Authoring Rules & State Constraints

## Overview
The Workflows engine leverages the C# compiler's native `IAsyncEnumerable<Wait>` state machine to pause and resume execution. Because workflows can be suspended for days or weeks, dehydrated to a database, and resumed on an entirely different server, workflow authors must adhere to strict state and execution rules. 

Violating these rules will result in serialization failures, memory leaks, or lost execution state. 

---

## 1. Composition and Sub-Workflows
**Rule:** Workflows must remain structurally flat. You cannot use native C# delegation to execute nested `IAsyncEnumerable` state machines.

When delegating logic to another workflow or local method that returns `IAsyncEnumerable<Wait>`, you must use the engine's native `WaitSubWorkflow` primitive. 

❌ **Invalid (Nested State Machine):**
```csharp
public async IAsyncEnumerable<Wait> ParentWorkflow()
{
    // FATAL: This creates a nested C# state machine that the engine cannot hydrate.
    await foreach(var wait in ChildWorkflow()) 
    {
        yield return wait;
    }
}
```

✅ **Valid (Engine Primitive):**
```csharp
public async IAsyncEnumerable<Wait> ParentWorkflow()
{
    // SAFE: The Orchestrator tracks the parent/child execution flatly in the database.
    yield return WaitSubWorkflow(ChildWorkflow());
}
```

---

## 2. Serializable State (The POCO Rule)
**Rule:** All properties on the `WorkflowContainer` and all local variables captured in the workflow method must be pure data structures (POCOs) that can be serialized to JSON.

When a workflow yields, the engine serializes the compiler's hidden closure (`<>c__DisplayClass`) and local variables. 

❌ **Invalid (Ephemeral Resources):**
```csharp
public async IAsyncEnumerable<Wait> ProcessDataWorkflow()
{
    // FATAL: FileStreams, HttpClients, and DbConnections cannot be serialized to the database.
    var stream = new FileStream("data.txt", FileMode.Open);
    
    yield return Wait("Wait For Approval"); 
    
    // When resumed on a different server, 'stream' will be null or broken.
    stream.Read(...); 
}
```

✅ **Valid (Stateless Resources):**
All resource-heavy or I/O operations should be wrapped in external stateless services that are invoked *between* yields, not held in state across yields.

---

## 3. Scope and Resource Management (`using` blocks)
**Rule:** Do not `yield return` inside a `using` block.

The C# compiler generates complex routing for `IDisposable` resources. If the workflow is administratively cancelled (deleted from the database), the `Dispose()` method on the resource will never execute, leading to leaks.

❌ **Invalid:**
```csharp
using (var context = new MyDbContext()) 
{
    yield return Wait("User Input"); // Engine pauses. Context is lost.
}
```

---

## 4. Execution Context (`AsyncLocal<T>`)
**Rule:** Do not rely on `AsyncLocal<T>` or `HttpContext` to pass data across a `yield return`.

The `yield return` represents a physical boundary where the thread dies and the state is persisted. Any ambient context tied to the thread will be wiped. All required data (like Tenant IDs or User IDs) must be explicitly mapped to properties on the `WorkflowContainer`.

---

# 🛠️ Roslyn Analyzer Blueprint

To ensure workflow authors follow these rules without needing to memorize the documentation, you should implement the following rules in a custom Roslyn Analyzer (e.g., `Workflows.Analyzers`):

### **WF001: Unsupported Sub-Workflow Execution**
*   **Trigger:** The analyzer detects an `await foreach` loop where the expression evaluates to `IAsyncEnumerable<Wait>`.
*   **Message:** *"Native iteration of sub-workflows is not supported. Yield a WaitSubWorkflow instead."*
*   **Code Fix Provider:** Automatically refactor the `await foreach` block into `yield return WaitSubWorkflow(...)`.

### **WF002: Unserializable Workflow State**
*   **Trigger:** A variable declaration or assignment inside a method returning `IAsyncEnumerable<Wait>` where the type implements `IDisposable` (and is not a simple container like `List<T>`).
*   **Message:** *"Ephemeral resources (IDisposable) cannot be held as local variables across workflow yield boundaries."*

### **WF003: Yield Inside Using Block**
*   **Trigger:** A `yield return` statement is found inside the syntax tree of a `UsingStatementSyntax`.
*   **Message:** *"Workflows cannot suspend execution inside a using block. Manage the resource explicitly before yielding."*

### **WF004: Invalid AsyncLocal Capture**
*   **Trigger:** Accessing `AsyncLocal<T>.Value` or `HttpContext.Current` inside a workflow method.
*   **Message:** *"Ambient thread context does not survive workflow suspension. Map this data explicitly to the WorkflowContainer."*