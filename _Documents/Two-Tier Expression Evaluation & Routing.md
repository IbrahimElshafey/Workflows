Here is the complete, finalized version of the **Architectural Reference Document** with the updated, professional terminology. You can copy and paste this directly into your project's wiki or documentation folder.

---

# Architectural Reference: Two-Tier Expression Evaluation & Routing

## 1. Core Philosophy

The routing engine operates on a fundamental principle of distributed systems: **Databases are for fast O(1) indexing; RAM is for cheap, complex computing.** We do not force the SQL database to parse complex C# logic, evaluate math, or manipulate strings. Instead, we use a **Two-Tier Architecture**:

* **Tier 1 (The Orchestrator / SQL):** Acts as a high-speed traffic cop. It uses pure exact-match (`==`) indexing to filter millions of sleeping workflows down to a handful of candidates.
* **Tier 2 (The Runner / RAM):** Acts as the brain. It takes the candidates identified by Tier 1, loads them into memory, and evaluates the full, complex C# expression (>, <, ||, &&).

---

## 2. The Extraction Strategy (Expression Transformation)

When a workflow yields a `SignalWait`, the `MatchExpressionTransformer` (an `ExpressionVisitor`) parses the expression tree. It acts as an **Extractor**, carefully pulling out only the exact-match parameters (`==`) that the SQL database understands, and leaving the complex logic for the Runner.

The RAM delegate *always* evaluates the entire original expression. The extracted SQL indexes are only used to wake the Runner up.

### Scenario A: The Logical AND (`&&`)

* **Code:** `.MatchIf(s => s.OrderId == this.OrderId && s.Amount > 1000)`
* **Extraction:** The Visitor extracts the exact match: `["OrderId", "123"]`. It ignores the inequality.
* **SQL Behavior:** Registers 1 row in the `SignalWaits` table.
* **Execution:** SQL instantly finds the workflow when `OrderId = 123`. The Orchestrator wakes the Runner. The Runner evaluates the full `&&` expression in RAM to see if `Amount > 1000` is actually true.

### Scenario B: The Logical OR (`||`)

* **Code:** `.MatchIf(s => s.OrderId == this.OrderId || s.GlobalOverride == true)`
* **Extraction:** The Visitor splits the tree. It extracts `["OrderId", "123"]` AND `["GlobalOverride", "true"]`.
* **SQL Behavior (Multi-Indexing):** Registers **2 rows** in the `SignalWaits` table pointing to the same WaitId.
* **Execution:** If a signal matches *either* row, the Orchestrator does a `SELECT DISTINCT WaitId` to avoid waking the Runner twice, and routes it to the Runner to evaluate the full `||` logic in RAM.

### Scenario C: Pure Inequalities (`>`) (The Broadcast Fallback)

* **Code:** `.MatchIf(s => s.TotalAmount > 500)`
* **Extraction:** The Visitor finds zero exact matches. It returns an empty index list.
* **SQL Behavior (Broadcast):** Registers a generic "Type Listener" index (e.g., `Path='*', Value='*'`).
* **Execution:** *Every* signal of this type wakes up this workflow instance, forcing the Runner to evaluate it in RAM. **(Note: This is dangerous under high load and is mitigated via Defensive Design below).**

---

## 3. Data Structures (`MatchTransformationResult`)

Your `MatchExpressionTransformer` must output a DTO that clearly separates these two tiers.

```csharp
internal class MatchTransformationResult 
{
    // --------------------------------------------------------
    // TIER 1: The Orchestrator Indexes (SQL)
    // --------------------------------------------------------
    /// <summary>
    /// The exact key-value pairs extracted for database routing.
    /// Empty list means this wait requires "Broadcast" routing.
    /// </summary>
    public List<ExactMatchPath> SqlIndexes { get; set; } = new List<ExactMatchPath>();

    // --------------------------------------------------------
    // TIER 2: The Runner Evaluation (RAM)
    // --------------------------------------------------------
    /// <summary>
    /// The fully compiled native delegate of the ENTIRE expression.
    /// Signature: (SignalData, ExplicitState) => bool
    /// </summary>
    public Func<object, object, bool> CompiledMatchDelegate { get; set; }
}

public class ExactMatchPath 
{
    public string Path { get; set; }   // e.g., "CustomerId"
    public object Value { get; set; }  // e.g., "CUST-99"
}

```

---

## 4. Defensive Design (Protecting the Engine)

Because developers will inevitably write bad expressions (e.g., pure inequalities) that cause "Broadcast Storms" and crash the database, the engine implements three layers of defense.

### Defense 1: Fluent API Redesign (The "Pit of Success")

Do not use a single `.MatchIf()` for everything. Separate the SQL requirement from the RAM logic at the API level.

```csharp
// The developer is FORCED to provide a Tier 1 SQL Index
yield return WaitSignal<OrderSignal>("OrderUpdate")
    .MatchExact(s => s.OrderId == this.OrderId)  // Mandatory: Extracts to SQL
    .Where(s => s.Amount > 500 && s.IsValid);    // Optional: Evaluates in RAM

```

### Defense 2: Compile-Time Enforcement (Roslyn Analyzers)

If using a unified `.MatchIf()` API, implement a custom Roslyn Analyzer (`WF1005: Missing Exact Match Index`).

* **Rule:** The expression tree *must* contain at least one binary `==` or `.Equals()` operator comparing a signal property to state.
* **Action:** Throw a compile-time error if missing, preventing the developer from deploying a "Broadcast" wait.

### Defense 3: Run-Time Circuit Breaker (Orchestrator)

The Orchestrator acts as a final safeguard against bad data.

* **Rule:** When evaluating incoming signals against the `SignalWaits` table, count the resulting matches.
* **Action:** If a single signal matches more than `MaxFanOutLimit` (e.g., 50 instances), the Orchestrator intercepts it. It logs a `Critical` alert ("Broadcast Storm Detected") and routes the execution to a low-priority queue to prevent engine starvation.

---

## 5. Implementation Checklist

* [ ] Refactor `WaitSignal` builder to explicitly separate `.MatchExact()` and `.Where()`.
* [ ] Update `MatchExpressionTransformer` to recursively traverse the expression tree to populate `SqlIndexes`.
* [ ] Update `MatchExpressionTransformer` to compile the *combined* (`MatchExact && Where`) expression into the `CompiledMatchDelegate`.
* [ ] Update Orchestrator `ProcessSignalAsync` to handle multiple `SqlIndexes` for the same `WaitId` (using `DISTINCT` to avoid duplicate wakeups).
* [ ] Implement the Orchestrator Circuit breaker (throttle or log if matched instances > 50).