# 🏗️ Workflows Engine: Runner Evaluation Logic

**Overview**
Because the engine avoids complex hydration overhead where possible and relies on snapshotting the state machine (`WorkflowRunContext`), the **Runner** acts purely as a **Logical Evaluation Engine**. 

Logically, the Runner sits in a `while` loop, processing the objects yielded by the workflow or manipulating its local state document. Its primary goal is to decide: **"Do I execute this immediately and keep spinning the loop, or do I pause and give the Full State Document back to the Orchestrator?"**

Here is exactly how the Runner logically evaluates each component.

---

### 1. The "Active" Control Flow (The Fast-Forward Loop)
Active operations do not pause the workflow. When the Runner evaluates these, it executes them instantly in memory and immediately asks the state machine for the *next* instruction (`MoveNextAsync()`).

* **Command**
    * **Logic:** The Runner inspects the Command. 
    * If it's a **Fast Command** (e.g., an internal calculation or fire-and-forget message), the Runner passes it to the `ICommandHandlerFactory`, executes it, and instantly advances the state machine.
    * If it's a **Slow Command** (e.g., an external API call requiring a callback), the Runner treats it like a passive wait. It returns control to the Orchestrator to dispatch the command and suspends the workflow until the result arrives.
* **CommandsGroup**
    * **Logic:** The Runner unpacks the group and evaluates the commands concurrently (or sequentially, based on configuration). 
    * If *all* commands are Fast, it executes them all and advances. If any are Slow, it suspends execution until the Orchestrator reports all slow commands have completed.
* **Compensation (Zero Round-Trips)**
    * **Trigger:** The workflow yields `Compensate("TokenA")` or an error triggers it.
    * **Logic:** Because the Runner already holds the complete `WorkflowRunContext` (including the history of executed commands), it does not ask the Orchestrator for anything. 
    * It filters its local history array for all successfully completed commands tagged with `"TokenA"`.
    * It extracts the saved `Result` of those historical commands.
    * It immediately executes the registered `Compensation` delegates in RAM, feeding them the historical results.
    * It finishes in the same compute cycle and instantly advances the state machine. The Orchestrator is completely unaware this happened until the final state is saved.
* **Cancellation via Method Call (`Cancel(Token)`)**
    * **Trigger:** The C# code inside the workflow calls `this.Cancel("Token_XYZ")`.
    * **Immediate Effect:** The `WorkflowContainer` synchronously adds `"Token_XYZ"` to its internal `CancelledTokens` hash set. The state machine does not pause.
    * **Runner Logic (New Waits):** If the workflow later yields a wait tagged with `"Token_XYZ"`, the Runner instantly sees the intersection with the local hash set, skips/ignores the wait, and loops again.
    * **Runner Logic (Old Waits & Database Sync):** When the Runner eventually suspends on a passive wait, it packages the `WorkflowRunResult` for the Orchestrator. The Runner logically instructs the Orchestrator: *"Here is my fully updated State Document. Also, `Cancel('Token_XYZ')` was called. Go into your database and proactively prune any existing relational waits linked to that token."*
* **Goto Statement (Replay)**
    * **Logic:** The native C# `goto` tells the compiler to jump back to an earlier line of code. Logically, the Runner doesn't "see" the `goto`; it just sees the workflow yielding a `Wait` it has evaluated before.
    * **Runner's Job:** The Runner must assign **new unique IDs** to these yielded waits. It cannot treat them as the "old" waits. It must treat a replayed `SignalWait` as a brand new requirement, clearing any previous match history for that specific IL line.

---

### 2. The "Passive" Suspensions (The Pausing Logic)
When the Runner hits these instructions, it realizes it cannot proceed. It builds the routing criteria and returns control to the Orchestrator to sleep.

* **SignalWait**
    * **Logic (Initial Yield):** The Runner evaluates the `MatchIf` expression structurally to create routing keys (e.g., `UserId == 5`) and hands it to the Orchestrator. The Runner halts.
    * **Logic (On Resume):** When the Orchestrator wakes the Runner with a matching payload, the Runner executes the compiled `MatchIf` delegate. If `true`, it executes the `AfterMatchAction` (injecting the payload into the closure), marks the wait as Complete, and advances.
* **SubWorkflowWait**
    * **Logic:** The Runner sees a child `IAsyncEnumerable`. 
    * Instead of advancing the parent workflow, the Runner shifts its evaluation context. It begins calling `MoveNextAsync()` on the **SubWorkflow**'s state machine.
    * The parent workflow is logically suspended in a "waiting" state until the child state machine reaches its implicit termination (no more yields).

---

### 3. The "Group" Aggregations (Wait Trees)
When a Group is yielded, the Runner logically treats it as a **Tree of Waits**. The Runner evaluates the *leaves* (children) to determine the status of the *root* (the Group).

* **GroupWaitAll (Match All)**
    * **Logic:** The Group is only marked Complete when `CompletedChildren == TotalChildren`. 
    * If a signal arrives and completes *Child A*, the Runner checks the Group. Since *Child B* is still pending, the Runner **does not** advance the parent state machine. It returns to the Orchestrator, waiting for *Child B*.
* **GroupWaitFirst (Match Any / Discriminator)**
    * **Logic:** The ultimate race condition. The Group is marked Complete the moment `CompletedChildren == 1`.
    * **Downward Pruning:** The millisecond *Child A* completes, the Runner logically forces a cancellation on *Child B*, *Child C*, etc. It strips them from the active wait tree so the Orchestrator stops routing signals to them.
    * The Runner then immediately advances the parent state machine.
* **GroupWaitWithExpression (Custom Match)**
    * **Logic:** The developer provided a custom rule (e.g., `(ChildA && ChildB) || ChildC`). 
    * Every time *any* child wait receives a signal and completes, the Runner executes this custom compiled expression in RAM. 
    * If the expression evaluates to `true`, the Runner executes Downward Pruning on any remaining incomplete children, marks the Group as Complete, and advances the parent.

---

### The Runner's "Single Tick" Summary

Viewed purely as a logical brain, the Runner acts like a highly-efficient CPU manipulating a local JSON document at lightning speed. Its evaluation cycle is:

1. **Look at the Wait or Command.**
2. **If it's a Fast Command, Token Cancellation, or Compensation** ➔ Do it immediately in memory, mutate the local state document, and loop again without talking to the database.
3. **If it's a Group** ➔ Evaluate children against the rule (All, First, Expression). Prune the losers. If the group rule is satisfied, loop again.
4. **If it's a Signal/Timer/SubWorkflow/Slow Command** ➔ Stop the loop. Hand the **Full State Document** (the completely serialized `WorkflowRunContext`), the new wait requests, and any cancelled tokens back to the Orchestrator and go to sleep.