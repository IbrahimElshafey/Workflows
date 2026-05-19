You’ve got it. Let's pull back the curtain on the "magic." Here is the internal developer documentation detailing exactly how the Constant Folding engine operates under the hood.

---

# Internal Architecture: ExactMatchAnalyzer Engine

This document explains the internal mechanics of the `ExactMatchAnalyzer`. While the public API appears as a standard `ExpressionVisitor`, internally, it operates as a **Boolean Satisfiability (SAT) Engine** combined with **Constant Folding**.

## 1. The Execution Pipeline

When `Analyze()` is invoked, the execution follows a strict 4-step pipeline:

### Step 1: Unrestricted Traversal

The base `Visit()` method walks the `LambdaExpression` tree. Unlike earlier iterations of this class, there is no "whitelist" or "blacklist" of nodes. The visitor freely enters `AndAlso`, `OrElse`, `Conditional` (Ternary), and custom Method calls.

### Step 2: Candidate Identification

Whenever the visitor encounters an `ExpressionType.Equal` node (or an `.Equals()` method), it pauses and flags it as a **Candidate**.

* Example: `s.TenantId == 5` is found.
* It delegates this node to `TryExtract()`.

### Step 3: Mandatory Proofing (`IsMandatory`)

This is where the mathematical folding occurs. To prove the candidate is safe to extract, the engine must prove that if the candidate fails, the entire query fails.

1. It instantiates the **`TargetMutator`** to forcefully replace the candidate node with `false`.
2. It runs the **`BooleanFolder`** over this newly mutated tree to mathematically reduce it.
3. If the folder collapses the entire tree down to a single `ConstantExpression(false)`, the candidate is mathematically proven to be **Mandatory** and is extracted. Otherwise, it is ignored.

### Step 4: Full Match Evaluation (`EvaluateFullMatch`)

Once all mandatory nodes are extracted, the engine must answer: *"Did we extract everything, or is there leftover logic (like `Age > 30`) that requires RAM validation?"*

1. It uses the `TargetMutator` to replace **all** successfully extracted nodes with `true` in the original tree.
2. It runs the `BooleanFolder` again.
3. If the folder collapses the tree to `ConstantExpression(true)`, it mathematically proves there are no leftover constraints. `_isFullMatch` becomes `true`.

---

## 2. Deep Dive: The Components

### Component A: `TargetMutator` (The Scalpel)

The mutator is a lightweight visitor that searches for a specific node and replaces it with a boolean constant (`true` or `false`).

**Key Mechanic: Reference Equality**

```csharp
if (node == _target) return Expression.Constant(_replaceWithValue);

```

It does not look for "nodes that look like `TenantId == 5`". Because `Expression` objects are immutable and unique in memory during compilation, it uses standard `==` reference equality to swap the exact node instance in the tree. This guarantees 100% precision.

### Component B: `BooleanFolder` (The Brain)

The folder simplifies boolean logic without executing it. It applies standard algebraic reduction rules to the AST (Abstract Syntax Tree).

**1. AND Reduction (`&&`)**

* Rule: `false && [Unknown] = false`
* Rule: `true && [Unknown] = [Unknown]`
If it sees a `false` on either side of an `AndAlso`, it destroys the branch and returns `false`.

**2. OR Reduction (`||`)**

* Rule: `true || [Unknown] = true`
* Rule: `false || [Unknown] = [Unknown]`
If it sees a `false` on one side of an `OrElse`, it drops the `false` and returns whatever the `[Unknown]` branch is.

**3. Ternary Reduction (`? :`)**

* Rule: `true ? [Path A] : [Path B] = [Path A]`
* Rule: `false ? [Path A] : [Path B] = [Path B]`
* Rule: `[Unknown] ? [Path A] : [Path B] = Unchanged`

### Component C: Unwrapping utilities

The engine contains an `UnwrapConvert` method. The C# compiler invisibly wraps Enums and Nullables in a `UnaryExpression` of type `Convert` (e.g., `(int)s.Status == 1`).
The unwrap utility recursively strips these nodes so `IsSignalParameter` can accurately compare the underlying `MemberExpression` against the lambda's input parameter.

---

## 3. Step-by-Step Internal Trace

Let's trace this complex tree through the engine:
`s => s.TenantId == 5 && (s.Role == "Admin" || s.Age > 30)`

**Phase 1: Testing `TenantId == 5**`

1. **Mutator:** Swaps `TenantId == 5` with `false`.
2. **Tree:** `false && (s.Role == "Admin" || s.Age > 30)`
3. **Folder:** Sees `false && [Unknown]`. Collapses to `false`.
4. **Verdict:** `IsMandatory` returns `true`. `TenantId` is extracted.

**Phase 2: Testing `Role == "Admin"**`

1. **Mutator:** Swaps `Role == "Admin"` with `false`.
2. **Tree:** `s.TenantId == 5 && (false || s.Age > 30)`
3. **Folder (Inner):** Fold the OR. `false || s.Age > 30` becomes `s.Age > 30`.
4. **Folder (Outer):** Fold the AND. `s.TenantId == 5 && s.Age > 30`. Both sides are unknown. Stays as is.
5. **Verdict:** Final tree is not `false`. `IsMandatory` returns `false`. Node is skipped.

**Phase 3: `EvaluateFullMatch**`

1. **Extracted Nodes:** Only `TenantId == 5` was extracted.
2. **Mutator:** Swaps `TenantId == 5` with `true`.
3. **Tree:** `true && (s.Role == "Admin" || s.Age > 30)`
4. **Folder:** Sees `true && [Unknown]`. Collapses to `[Unknown]`.
5. **Verdict:** Final tree is `s.Role == "Admin" || s.Age > 30` (not `true`). `IsExactMatchFullMatch` becomes `false`.

**Final Output:** The Orchestrator receives `["TenantId"] = ["5"]` and knows it must run RAM validation for the rest. Zero data loss. Maximum SQL extraction.