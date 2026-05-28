### Task: Implement Deterministic Callback Registry to Eliminate "Version Hell"

#### **Objective**

Replace the current reliance on compiler-generated delegate/closure names (e.g., `<>c__DisplayClass44_3`) for state persistence. These names change upon recompilation, which "bricks" sleeping workflow instances. We need a **Deterministic Callback Registry** that maps a stable hash key to the workflow's callback delegates.

#### **Context**

Currently, when a workflow yields a wait, we rely on the internal structure of the generated state machine to track closures. Since Roslyn can re-index these classes after code changes, we need to generate a unique, stable **Hash Key** at compile-time (using `CallerArgumentExpression`) and store only that key in our `WaitDto`.

#### **Implementation Steps**

**1. Create the Registry Service**
Create `ICallbackRegistry` and a thread-safe `CallbackRegistry` implementation.

* **Store:** `ConcurrentDictionary<string, Delegate>`.
* **Requirement:** This registry must be populated during the `RegisterWorkflow` phase.

**2. Implement Stable Hashing Helper**
Implement a static helper `WorkflowHashCalculator`.

* **Signature:** `CalculateHash(string expressionText, string callerName, string signalOrCommandId)`
* **Logic:**
* Normalize `expressionText` (e.g., strip whitespaces, tabs, newlines).
* Combine with `callerName` (the method name) and `SignalIdentifier` or `CommandIdentifier`.
* Return a SHA256 or similar stable string hash.



**3. Update Wait Builders (The "Magic")**
Update the Fluent Builders (`SignalWaitBuilder`, `CommandWaitBuilder`) to generate this hash automatically.

* Use `[CallerArgumentExpression]` to capture the match expression string.
* Use `[CallerMemberName]` to capture the method name.
* **Action:** When `.MatchIf()` or `.AfterMatch()` is called, calculate the hash and assign it to a new `public string HandlerKey { get; }` property on the `Wait` object.

**4. Update Registration Flow**
Modify `RegisterWorkflow` to iterate over all registered waits, calculate their `HandlerKey`, and ensure the delegate is stored in the `CallbackRegistry`.

**5. Update WorkflowRunner**

* The Runner must stop relying on reflection to resolve callbacks.
* **Action:** When a workflow resumes, read the `HandlerKey` from the `WaitDto`.
* Use `ICallbackRegistry.Get(handlerKey)` to resolve the `Delegate` to invoke.

#### **Acceptance Criteria**

* **Zero Volatility:** Recompiling the workflow (adding comments, whitespace, or even re-ordering methods) must result in the exact same `HandlerKey` for existing waits.
* **No Reflection:** The Runner must fetch delegates from the `CallbackRegistry` dictionary at runtime; no `MethodInfo.Invoke` or runtime closure searching should occur for callbacks.
* **Compiler-Verified:** Ensure the `MatchExpression` normalization (stripping spaces) is robust enough so that `x => x.Id == 1` and `x=>x.Id==1` generate the same hash.

---

### Why this is the correct architectural path

* **Resilience:** The database now stores a "business key" (`HandleOrder_OrderSubmitted`) instead of a "compiler key" (`<>c__DisplayClass0_0`).
* **Performance:** Dictionary lookup is $O(1)$ and eliminates reflection.
* **Cleanliness:** Developers don't need to manually define keys; the build-time attributes handle it implicitly.

---

### Optional: Technical Note for the Developer

You may use the following logic to compute the stable hash in your builder:

```csharp
private static string CalculateHash(string expressionText, string callerName, string identifier)
{
    // 1. Normalize the expression text (remove whitespaces/formatting)
    var normalized = Regex.Replace(expressionText, @"\s+", "");
    
    // 2. Combine into a deterministic string
    var raw = $"{normalized}_{callerName}_{identifier}";
    
    // 3. Compute Hash (SHA256 is overkill; a stable hash code or simple Base64 MD5 is fine)
    using var sha256 = System.Security.Cryptography.SHA256.Create();
    var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
    return Convert.ToBase64String(bytes);
}

```

**Do you have any specific concerns about the hashing algorithm or how it should be stored in the `Wait` class?**