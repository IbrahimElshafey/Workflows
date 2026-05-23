### 1. `MatchExpressionNormalizer`

This class is the "Entry Point" of the pipeline. Its primary responsibility is to create a common, standard format for all incoming match expressions, regardless of their original definition.

* **Role:** Normalization.
* **Functionality:**
* It takes an arbitrary `LambdaExpression` (which may involve various POCO types and parameter structures) and transforms it into a unified, predictable `Expression<Func<object, object, object, bool>>`.
* It handles **Parameter Mapping** (e.g., mapping `signalData`, `stateData`, and `workflowInstance` to their respective unified object slots).
* It injects **Type Casting** (`Expression.Convert`) so that the subsequent visitors can safely cast these object parameters back to the expected concrete POCO types without losing type information during traversal.


* **Design Rationale:** This guarantees that the rest of the transformer logic never needs to worry about "unknown" or mismatched parameter signatures.

### 2. `DynamicMatchVisitor`

This is the "Capabilities Visitor." Its job is to figure out if the expression is simple enough to be translated into something the engine can execute in RAM (using `JsonElement`) for Tier 1.5 filtering.

* **Role:** Compilation & Capability Analysis.
* **Functionality:**
* It walks the normalized expression tree in a single pass.
* It builds two representations simultaneously:
1. `Result`: A `JsonElement` delegate that can evaluate complex POCO logic in memory using `System.Text.Json`.
2. `TypedResult`: A safe, pruned version of the original typed expression tree (with unsupported/impure branches stripped out).


* **"Taint Tracking" Logic:** If it encounters a method call or logic it cannot convert (e.g., a custom `ExternalDatabase.Check()`), it doesn't fail; it marks that branch as "tainted" and continues. If it encounters a dangerous operation like `!` (NOT) on a tainted branch, it safely aborts that specific branch to prevent incorrect routing.


* **Design Rationale:** This allows the engine to be as aggressive as possible with RAM-based filtering while ensuring it never incorrectly drops a signal due to over-eager optimization.

### 3. `ExactMatchAnalyzer`

This class acts as the "SQL Indexer." It consumes the `TypedResult` produced by the `DynamicMatchVisitor` and extracts the mandatory "Equality" paths that can be used for fast SQL routing.

* **Role:** Index Extraction (Tier 1).
* **Functionality:**
* It consumes the `TypedResult` (the strongly-typed, safe expression tree).
* It flattens `AND` logic and extracts strictly equality-based pairs (`SignalProperty == Constant`).
* It ignores complex logic (`OR`, `NOT`, method calls) that cannot be translated into a reliable database index.
* It produces a compiled `InstanceMatchExpression` that allows the engine to generate indexing metadata for the Orchestrator without ever doing reflection at runtime.


* **Design Rationale:** By operating on the cleaned, safe tree from the `DynamicMatchVisitor`, this class avoids "Expression Visitor hell" and provides the absolute source of truth for mandatory routing constraints.

---

### Pipeline Workflow Summary

1. **Normalization:** The incoming expression is cleaned and casted.
2. **Tier 1.5 (RAM):** The `DynamicMatchVisitor` builds a RAM-based evaluator. If it determines the logic is too complex, it sets `IsFullMatch = false`.
3. **Tier 1 (SQL):** The `ExactMatchAnalyzer` uses the output of the visitor to extract mandatory equality paths. If the visitor already signaled that the tree is complex, the analyzer adopts a conservative approach to ensure index integrity.

This pipeline ensures **maximum performance through in-memory filtering** and **maximum efficiency through SQL indexing**, while maintaining strict safety guarantees for your state machine.