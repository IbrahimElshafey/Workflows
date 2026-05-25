# How Match Expression Transformation Works

When a workflow wait point is registered, its `matchExpression` lambda is transformed into a set of pre-compiled artifacts that allow the Orchestrator to route incoming signals efficiently — without waking up the full Runner unless it is truly needed.

The entry point is `MatchExpressionTransformer.Transform`, which produces an immutable `MatchTransformationResult`.

---

## The Three Tiers of Signal Matching

| Tier | Name | Evaluated By | Description |
|------|------|--------------|-------------|
| **1** | SQL Exact Match | Database / Orchestrator | Pure equality checks turned into indexed SQL predicates. Zero code execution. |
| **1.5** | RAM Pre-filter | Orchestrator (in-process) | JsonElement-based delegate that runs in memory without deserializing to POCOs. |
| **2** | Full Runner Execution | Runner process | The original compiled lambda, executed only when Tier 1 and 1.5 are insufficient. |

---

## Pipeline — Step by Step

```
matchExpression (user-defined lambda)
		│
		▼
 ┌─────────────────────────┐
 │  Step 0 — Normalizer    │
 │  MatchExpressionNormalizer
 └────────────┬────────────┘
			  │  Expression<Func<object, object, object, bool>>
			  │  parameters: [0]=signalData  [1]=state  [2]=instance
			  ▼
 ┌─────────────────────────┐
 │  Step 1 — DynamicMatchVisitor  (single tree walk)
 └──────┬──────────┬───────┘
		│          │
   Result          ResultTyped          IsFullMatch
 (JsonElement      (original typed      (bool)
  delegate)         lambda, untouched)
		│          │
		▼          ▼
 ┌─────────────────────────┐
 │  Step 2 — ExactMatchAnalyzer  (policy engine, no re-walk)
 └──────┬──────────┬───────┘
		│          │
  SignalExactMatchPaths    InstanceExactMatchExpression
  (List<string>)           (Expression<Func<object,object,string[]>>)
		│          │
		└────┬─────┘
			 ▼
 ┌─────────────────────────┐
 │  Step 3 — MatchTransformationResult  (immutable)
 └─────────────────────────┘
```

---

## Step 0 — Normalization (`MatchExpressionNormalizer`)

User-defined lambdas can have 1, 2, or 3 parameters with arbitrary POCO types:

```csharp
// Examples of valid user expressions
(OrderSignal s) => s.OrderId == 42
(OrderSignal s, MyState st) => s.OrderId == st.ExpectedOrderId
(OrderSignal s, MyState st, MyWorkflow inst) => s.OrderId == inst.OrderId && st.IsPending
```

The normalizer rewrites all of them into the single unified signature:

```csharp
Expression<Func<object, object, object, bool>>
// body sees: (Convert(signalData, OrderSignal), ConvertState(state, MyState), Convert(instance, MyWorkflow))
```

The `Convert` cast nodes are deliberately preserved in the tree. Downstream visitors must step through them when traversing member access chains.

---

## Step 1 — `DynamicMatchVisitor` (Tier 1.5 builder)

Walks the normalized tree **once** and produces two outputs simultaneously:

### `ResultTyped`
The normalized lambda as-is — preserved for `ExactMatchAnalyzer` to consume without any re-parsing.

### `Result` — `Expression<Func<JsonElement, JsonElement, JsonElement, bool>>`
Each typed member access (`signal.Order.Id`) is rewritten to a `JsonElementExtensions.Get<T>(jsonParam, "Order.Id")` call, allowing the Orchestrator to evaluate the expression directly against raw JSON payloads.

**Partial-match taint tracking:** If a sub-expression cannot be translated (e.g., a method call, an unsupported type), it is replaced with `UnknownNode`. In an `AndAlso` chain, the unknown branch is dropped and the remaining branch is marked *partial* (over-approximation — may produce false positives, never false negatives). Any other combination (e.g., `OrElse` with an unknown branch) causes `IsFullMatch = false`, meaning the result cannot be used as a standalone filter.

### `IsFullMatch`
`true` if every node in the tree was successfully translated. When `true`, the compiled `Result` delegate is a complete and correct filter on its own.

**Supported node types:**

| Node | Behaviour |
|------|-----------|
| `BinaryExpression` (AndAlso) | Recurse both sides; drop unknown branch with taint |
| `BinaryExpression` (other) | Translate if both sides resolve; else `UnknownNode` |
| `UnaryExpression` (Not) | Propagate; abort if operand is tainted (false-negative risk) |
| `MemberExpression` | Translate to `JsonElement.Get<T>("dot.path")` |
| `MethodCallExpression` (.Equals) | Rewrite to `BinaryExpression.Equal` |
| Anything else | `_isUnsupportedNodeFound = true` → `UnknownNode` |

---

## Step 2 — `ExactMatchAnalyzer` (Tier 1 policy engine)

Consumes `ResultTyped` and `IsFullMatch` from the visitor. It does **not** re-walk the tree independently — it acts as a *policy engine* on the already-visited output.

### What it extracts

For every equality node of the form:

```
signal.X  ==  <other>       (BinaryExpression.Equal)
signal.X.Equals(<other>)    (MethodCallExpression)
```

where `<other>` does **not** reference the signal parameter, a pair `(signalPath, otherSideExpression)` is recorded.

The `<other>` side can be:
- A constant: `signal.OrderId == 42`
- A state member: `signal.OrderId == state.ExpectedOrderId`
- An instance member: `signal.OrderId == instance.OrderId`
- Any non-signal expression

### Consistent sort

After walking, all pairs are **sorted by `signalPath` (ascending)**. This guarantees:

```
SignalExactMatchPaths[i]  ←→  InstanceExactMatchExpression result[i]
```

at every index, regardless of the order the user wrote the conditions.

### `SignalExactMatchPaths`
The sorted list of dot-notation paths into the signal (e.g., `["Order.CustomerId", "Order.Id"]`). Stored as SQL index columns on the wait point row.

### `InstanceExactMatchExpression`
```csharp
Expression<Func<object /*workflowInstance*/, object /*state*/, string[]>>
```
When compiled and invoked at wait-point registration time, produces the array of string values that correspond to `SignalExactMatchPaths` (same sort order). These are stored alongside the paths so the Orchestrator can do a pure SQL lookup:

```sql
WHERE signal_path_0 = 'Order.CustomerId' AND signal_value_0 = '99'
  AND signal_path_1 = 'Order.Id'         AND signal_value_1 = '42'
```

The non-signal expressions are rebased onto new `(workflowInstance, state)` object parameters via `ParameterReplacer`, and each value is wrapped in `Convert.ToString(object)` for null-safe string conversion.

### `IsExactMatchFullMatch`
`true` only when:
1. `DynamicMatchVisitor.IsFullMatch` was `true` (whole expression is translatable), **and**
2. Every node visited was either `AndAlso` or an extractable equality.

When `false`, Tier 1 SQL results are a *superset* — they must be refined by Tier 1.5 or Tier 2.

---

## Output — `MatchTransformationResult`

| Property | Source | Used by |
|----------|--------|---------|
| `MatchExpression` | Normalizer | Runner (Tier 2 full execution) |
| `SignalExactMatchPaths` | ExactMatchAnalyzer | Orchestrator — SQL index column names |
| `InstanceExactMatchExpression` | ExactMatchAnalyzer | Registration — evaluates SQL index values |
| `IsExactMatchFullMatch` | ExactMatchAnalyzer | Orchestrator — skip Tier 2 if `true` |
| `GenericMatchExpression` | DynamicMatchVisitor | Orchestrator — in-process RAM filter |
| `IsGenericMatchFullMatch` | DynamicMatchVisitor | Orchestrator — skip Tier 2 if `true` |

---

## Design Principles

- **Single traversal:** The expression tree is walked exactly once (by `DynamicMatchVisitor`). `ExactMatchAnalyzer` consumes the already-walked `ResultTyped` output, eliminating redundant parsing and ensuring both tiers can never disagree on what the expression means.
- **No false negatives:** Partial translation always over-approximates (lets more signals through) rather than under-approximating. A signal that should match is never discarded at Tier 1 or 1.5.
- **Immutability:** `MatchTransformationResult` is an init-only record. It holds live `Expression` trees and must **not** be stored in long-lived caches — doing so will cause memory leaks.
