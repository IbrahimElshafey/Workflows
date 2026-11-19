# Why You Need Both: Side-by-Side Execution AND Data Migration

This document explains why the versioning system provides **two distinct strategies** — Side-by-Side (SxS) execution and explicit Data Migration — and why neither alone is sufficient. Both strategies are always available and complement each other.

---

## The Core Problem

When you deploy a new workflow version, some instances of the old version are already suspended mid-execution in the database. You have to decide what happens to them.

There are only three honest answers:

| Option | Description | Problem |
| :--- | :--- | :--- |
| **Do nothing** | Pretend V1 instances can resume on V2 code | State machine index mismatch → corruption or wrong execution path |
| **Force migration** | Require every deployment to ship a migration class | Impossible for urgent fixes; dangerous for complex mid-flight state |
| **Give the developer a choice** | SxS (passive) or Migration (active) based on the situation | ✅ This is what we implement |

---

## Strategy 1 — Side-by-Side (SxS) Execution

The V1 instance is **pinned to the V1 assembly**, loaded in an isolated `AssemblyLoadContext`, and continues executing until it completes naturally. No developer action is required. The engine routes the instance based on its recorded version tag.

**Characteristics:**
- Zero developer effort
- Zero risk of state corruption
- V1 instance produces V1 business outcomes (which may be intentional)
- Requires the V1 assembly to be retained in the deployment artifact

---

## Strategy 2 — Data Migration

The developer writes a `WorkflowMigration<TOld, TNew>` class. The engine transforms the V1 instance's frozen state — class fields, active wait checkpoints, sub-workflow frames — into V2 format. The instance resumes on V2 code.

**Characteristics:**
- Full developer control over the transformation
- Instance produces V2 business outcomes going forward
- Requires explicit code; bugs in migration code can affect live instances
- Optional — only needed when the developer actively wants the instance on V2

---

## Decision Flow at Deployment

```
New version deployed → V1 instances detected in database
                                │
              ┌─────────────────┴──────────────────┐
              │                                    │
     Migration class exists?                Migration class exists?
              YES                                  NO
              │                                    │
              ▼                                    ▼
  Run MigrateInstance +              Route to SxS (V1 ALC)
  MigrateActiveWait +                Instance runs to completion
  MigrateSubWorkflowState            on original V1 code.
  → Instance becomes V2              No code needed.
```

> [!IMPORTANT]
> SxS is the **automatic fallback**. If no migration class is registered for a V1 instance, the engine silently routes it to SxS. The developer never needs to write anything to keep V1 instances alive — only to actively migrate them.

---

## Real-World Scenarios

The following scenarios illustrate when each strategy is the right call.

---

### Scenario 1 — Urgent Production Bug Fix

**Situation:** A critical null-reference bug is discovered in `OrderWorkflow`. It only affects new orders. A hotfix is deployed as V2 immediately.

**Active V1 instances:** 340 orders already in-flight, suspended at various steps. None are affected by the bug.

**Decision: SxS — no migration needed.**

```
V1 instances (340):  bug-free, running normally → SxS (coast to completion)
V2 instances (new):  bug fixed                  → starts fresh on V2
```

Writing a migration class here would be pure risk for zero benefit. The 340 existing orders are healthy. SxS protects them with zero developer effort.

---

### Scenario 2 — Domain Model Rename (Controlled Migration)

**Situation:** `OrderWorkflow.OrderId (Guid)` is renamed to `OrderWorkflow.OrderNumber (string)` in V2. Downstream systems now expect the new string format.

**Active V1 instances:** 200 orders suspended mid-flight. The business requires all of them to emit `OrderNumber` format events going forward to avoid breaking the downstream invoice service.

**Decision: Migration — all V1 instances must be moved to V2.**

```csharp
public class OrderWorkflowMigration_20260601
    : WorkflowMigration<OrderWorkflowV1, OrderWorkflowV2>
{
    [WorkflowMigration("OrderWorkflow", "1.0.0", "2.0.0")]
    public override void MigrateInstance(OrderWorkflowV1 old, OrderWorkflowV2 _new)
    {
        _new.AutoMapFrom(old);
        // Retype: Guid → prefixed string
        _new.Instance.OrderNumber = $"ORD-{old.Instance.OrderId:N}".ToUpper()[..16];
    }

    public override Wait MigrateActiveWait(WaitInfrastructureDto oldWait, OrderWorkflowV2 _new)
        => RecreateWait(oldWait.WaitName); // wait structure unchanged
}
```

SxS would leave V1 instances producing `OrderId (Guid)` events — breaking the downstream service. Migration is the correct choice here.

---

### Scenario 3 — Long-Running Workflow, Mid-Compensation

**Situation:** `InsuranceClaimWorkflow` manages insurance claims. It can run for months. The workflow was suspended inside a `Compensation` scope — it's actively rolling back a failed sub-process when the new version is deployed.

**Active V1 instances:** 12 claims in active compensation/rollback.

**Decision: SxS — migration is too dangerous here.**

A mid-compensation migration could leave the claim in a state where:
- The compensation recorded in V1 terms is no longer semantically valid in V2
- The V2 code doesn't know how far the rollback progressed under V1
- Duplicate reversal events could be emitted

```
V1 instances (in compensation): SxS — let them complete their rollback under V1 semantics
V2 instances (new claims):      Start fresh on V2
```

Even with an elegant migration API, the *business correctness* of migrating mid-compensation is not guaranteed. SxS is the safe answer.

---

### Scenario 4 — New Parallel Step Added, Gradual Rollout

**Situation:** `FulfillmentWorkflow` gains a new parallel fraud check in V2. The business wants new orders to go through fraud checking. Existing in-flight orders are already past the stage where fraud checking would apply — adding it retroactively is meaningless and would stall them waiting for a signal that will never come.

**Active V1 instances:** 1,200 orders at various stages. ~900 are past the fraud-check insertion point.

**Decision: Split — SxS for most, migration optional for early-stage ones.**

```
V1 instances suspended before "VerifyStock" (300):
    → Optionally migrate to V2 with the fraud check added
    → Or SxS and let them skip fraud check (acceptable business decision)

V1 instances suspended after "VerifyStock" (900):
    → SxS (migrating would insert a retroactive fraud check they've already passed — wrong)
```

```csharp
public override Wait MigrateActiveWait(WaitInfrastructureDto oldWait, OrderWorkflowV2 _new)
{
    // If already past VerifyStock — do not insert the fraud check retroactively
    if (oldWait.WaitName == "ArrangeShipping" || oldWait.WaitName == "RunBilling")
        return RecreateWait(oldWait.WaitName);

    // If still at or before VerifyStock — insert the new parallel group
    if (oldWait.WaitName == "VerifyStock")
        return WaitGroup(
            [
                WaitSignal<StockVerifiedEvent>("StockVerified",  "VerifyStock"),
                WaitSignal<FraudClearedEvent>("FraudCleared",    "FraudCheck"),   // new in V2
            ],
            "ParallelVerification"
        ).MatchAll();

    return RecreateWait(oldWait.WaitName);
}
```

---

### Scenario 5 — Version Accumulation Over Years

**Situation:** `LoanApprovalWorkflow` has been running for 3 years. Versions exist: 1.0, 1.1, 1.2, 2.0, 2.1. Active instances exist at every version.

**Decision: SxS keeps all versions alive without compounding migration complexity.**

Without SxS, you would need to write migration chains:
- `1.0 → 2.1`: extremely complex, high-risk
- `1.1 → 2.1`: still complex
- `1.2 → 2.1`: manageable
- `2.0 → 2.1`: easy

With SxS, each version's instances simply run to completion on their original assembly. No migration chains. No combinatorial explosion of migration paths.

```
v1.0 instances → SxS on v1.0 ALC  (7 remaining, will complete in ~2 months)
v1.1 instances → SxS on v1.1 ALC  (23 remaining)
v1.2 instances → SxS on v1.2 ALC  (140 remaining)
v2.0 instances → Migrate to v2.1  (easy, one-step, low risk)
v2.1 instances → running on latest
```

---

## Summary: When to Use Each

| Use SxS when... | Use Migration when... |
| :--- | :--- |
| No migration class written yet | Business requires instances to produce V2 outcomes |
| Instance is in compensation/rollback | Domain model renamed and downstream systems expect V2 schema |
| Change does not affect existing in-flight instances | Regulatory or audit requirement to operate uniformly on V2 |
| Migration would insert a retroactive step with wrong semantics | A bug in V1 affects active instances and must be corrected |
| Many versions active — SxS avoids migration chain complexity | Instance count is small and migration risk is low |
| You need to ship immediately without testing a migration | You have time to write, review, and test the migration class |

> [!NOTE]
> These are not mutually exclusive per deployment. You can migrate a subset of V1 instances (e.g. those in an early stage) while SxS-routing the rest. The engine decides per-instance based on which migration classes are registered and whether the instance matches their `[WorkflowMigration]` criteria.
