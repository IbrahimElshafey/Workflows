# WorkflowExecutionRequest and WorkflowStateDto Refactoring

## Summary of Changes

Improved the design consistency and semantics of `WorkflowExecutionRequest` and `WorkflowStateDto` based on your feedback.

---

## 1. WorkflowExecutionRequest - Signal vs CommandResult Consistency ✅

### Problem
- `Signal` had its own dedicated property (`SignalDto Signal`)
- `CommandResult` was a generic `object` without clear documentation
- Inconsistent structure made it unclear what incoming events could contain

### Solution
Both incoming event types (Signal and CommandResult) are now clearly documented as **mutually exclusive** properties:

```csharp
public class WorkflowExecutionRequest
{
    /// <summary>
    /// The wait ID that is being triggered (Signal, Command, TimeWait, etc.)
    /// </summary>
    public Guid TriggeringWaitId { get; set; }

    /// <summary>
    /// The persisted workflow state to resume.
    /// </summary>
    public WorkflowStateDto WorkflowState { get; set; }

    /// <summary>
    /// Incoming signal payload (for SignalWait triggers).
    /// Mutually exclusive with CommandResult.
    /// </summary>
    public SignalDto Signal { get; set; }

    /// <summary>
    /// Result from external command execution (for deferred/dispatched CommandWait triggers).
    /// Mutually exclusive with Signal.
    /// </summary>
    public object CommandResult { get; set; }
}
```

### Benefits
- **Clear semantics**: Both signal and command result are documented as mutually exclusive
- **Explicit purpose**: Comments clarify when each property is used
- **Better structure**: TriggeringWaitId clearly identifies which wait is being resumed
- **Type safety**: Signal uses strongly-typed `SignalDto` while CommandResult remains flexible as `object`

---

## 2. WorkflowStateDto - CancelledTokens → CancellationHistory ✅

### Problem
- `CancelledTokens` was a `HashSet<string>` storing only token IDs
- No audit trail of **when** or **why** cancellations occurred
- Semantically wrong: cancellation is **history/audit data**, not **workflow state**
- Lost temporal information needed for debugging

### Solution
Replaced `HashSet<string> CancelledTokens` with `List<CancellationHistoryEntry> CancellationHistory`:

```csharp
public class WorkflowStateDto
{
    // ... other properties ...

    /// <summary>
    /// History of cancellation events for this workflow instance.
    /// Used to determine which waits should be cancelled during evaluation.
    /// </summary>
    public List<CancellationHistoryEntry> CancellationHistory { get; internal set; } = new();
}
```

#### New CancellationHistoryEntry Structure

```csharp
public class CancellationHistoryEntry
{
    /// <summary>
    /// The cancellation token that was triggered.
    /// </summary>
    public string Token { get; set; }

    /// <summary>
    /// UTC timestamp when the cancellation was triggered.
    /// </summary>
    public DateTime CancelledAt { get; set; }

    /// <summary>
    /// Optional reason or context for the cancellation.
    /// </summary>
    public string Reason { get; set; }
}
```

### Benefits
- **Temporal tracking**: Know **when** each cancellation occurred
- **Audit trail**: Full history for debugging and compliance
- **Contextual data**: Optional reason field for business context
- **Better semantics**: Clearly represents history, not state
- **Immutable design**: Append-only list preserves full history

---

## 3. Helper Extensions for Backward Compatibility ✅

Created `CancellationHistoryExtensions` to make working with the new structure easy:

```csharp
public static class CancellationHistoryExtensions
{
    /// <summary>
    /// Gets all cancelled tokens from the cancellation history.
    /// </summary>
    public static HashSet<string> GetCancelledTokens(this List<CancellationHistoryEntry> history)
    {
        if (history == null || history.Count == 0)
        {
            return new HashSet<string>();
        }

        return new HashSet<string>(history.Select(entry => entry.Token));
    }

    /// <summary>
    /// Checks if a specific token has been cancelled.
    /// </summary>
    public static bool IsTokenCancelled(this List<CancellationHistoryEntry> history, string token)
    {
        return history?.Any(entry => entry.Token == token) == true;
    }
}
```

### Usage Examples

**Before (old CancelledTokens):**
```csharp
if (state.CancelledTokens.Contains("order-timeout"))
{
    // ...
}
```

**After (CancellationHistory):**
```csharp
if (state.CancellationHistory.IsTokenCancelled("order-timeout"))
{
    // ...
}

// Or get all tokens:
var cancelledTokens = state.CancellationHistory.GetCancelledTokens();
```

---

## 4. Updated Code Locations ✅

### Files Modified

1. **Workflows.Abstraction\DTOs\WorkflowExecutionRequest.cs**
   - Added clear documentation for Signal and CommandResult
   - Clarified mutually exclusive relationship

2. **Workflows.Abstraction\DTOs\WorkflowStateDto.cs**
   - Replaced `HashSet<string> CancelledTokens`
   - Added `List<CancellationHistoryEntry> CancellationHistory`

3. **Workflows.Abstraction\DTOs\CancellationHistoryEntry.cs** (NEW)
   - Complete cancellation event structure
   - Token, timestamp, and reason

4. **Workflows.Abstraction\DTOs\CancellationHistoryExtensions.cs** (NEW)
   - Helper extension methods
   - Backward-compatible API

5. **Workflows.Runner\WorkflowRunner.cs**
   - Updated to read from `CancellationHistory` on restore
   - Appends new cancellations to history (de-duplicated)
   - Uses extension methods for token queries

6. **Workflows.Runner\Pipeline\WorkflowStateService.cs**
   - Updated context creation to use `CancellationHistory`
   - Updates history on result mapping
   - De-duplicates tokens automatically

7. **Workflows.Runner\Pipeline\CancelHandler.cs**
   - Updated to use `CancellationHistory.GetCancelledTokens()`
   - Uses extension methods for queries

---

## 5. Migration Path for Existing Data

If you have existing persisted `WorkflowStateDto` instances with `CancelledTokens`, you'll need a data migration:

```csharp
// Pseudocode for migration
foreach (var state in existingWorkflowStates)
{
    if (state.CancelledTokens != null && state.CancelledTokens.Any())
    {
        state.CancellationHistory = state.CancelledTokens
            .Select(token => new CancellationHistoryEntry
            {
                Token = token,
                CancelledAt = DateTime.UtcNow, // or use state.Created if appropriate
                Reason = "Migrated from legacy CancelledTokens"
            })
            .ToList();

        // Optionally clear old property if schema allows
        state.CancelledTokens = null;
    }
}
```

---

## 6. Conceptual Benefits

### Before (Inconsistent Design)
```
WorkflowExecutionRequest {
    SignalDto Signal          ← Specific typed property
    object CommandResult      ← Generic unclear property
    ...
}

WorkflowStateDto {
    HashSet<string> CancelledTokens  ← Lost temporal info, wrong semantics
}
```

### After (Clean Design)
```
WorkflowExecutionRequest {
    SignalDto Signal          ← For SignalWait triggers (mutually exclusive)
    object CommandResult      ← For CommandWait triggers (mutually exclusive)
    ...
}

WorkflowStateDto {
    List<CancellationHistoryEntry> CancellationHistory  ← Full audit trail
}
```

---

## Key Improvements Summary

✅ **Consistency**: Signal and CommandResult are now symmetrical in documentation  
✅ **Clarity**: Mutually exclusive relationship is explicitly stated  
✅ **History**: Full cancellation audit trail with timestamps  
✅ **Semantics**: Cancellation is history data, not state data  
✅ **Backward Compat**: Extension methods provide familiar API  
✅ **Debuggability**: Know when and why cancellations happened  
✅ **Compliance**: Audit-friendly structure for production systems  

---

## Build Status

✅ **Build Successful** - All code updated and compiling

---

## Next Steps

1. Consider adding similar history tracking for:
   - Command execution history (already exists via `CommandHistoryEntry`)
   - Signal receipt history (for audit)
   - State transitions (for debugging)

2. Add query helpers for CancellationHistory:
   ```csharp
   // Get cancellations within time range
   public static IEnumerable<CancellationHistoryEntry> GetCancellationsSince(
       this List<CancellationHistoryEntry> history, 
       DateTime since)

   // Get cancellations by reason
   public static IEnumerable<CancellationHistoryEntry> GetCancellationsByReason(
       this List<CancellationHistoryEntry> history, 
       string reasonPattern)
   ```

3. Consider versioning strategy for WorkflowStateDto schema changes
