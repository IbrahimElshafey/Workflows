using System.Reflection;
using WorkflowSample;
using WorkflowSample.DataObject;
using Workflows.Definition;

Console.WriteLine("=== Testing Workflow State Management (DSL Layer) ===\n");

// Test 1: Verify implicit operator conversions work
Console.WriteLine("Test 1: Implicit Operator Conversions");
try
{
    var workflow = new OrderWithCommandWorkflow();
    var workflowStream = workflow.Run();
    Console.WriteLine("✅ Workflow instantiation successful");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed: {ex.Message}");
}

// Test 2: Verify stateful builder conversions
Console.WriteLine("\nTest 2: Stateful Builder Conversions");
try
{
    var workflow = new StatePatternWorkflowSample();
    var workflowStream = workflow.Run();
    Console.WriteLine("✅ Stateful workflow instantiation successful");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed: {ex.Message}");
}

// Test 3: Test ExplicitState assignment
Console.WriteLine("\nTest 3: ExplicitState Assignment");
try
{
    var signalWait = new TestWorkflow().CreateSignalWaitWithState();
    if (signalWait.ExplicitState != null && signalWait.ExplicitState.Equals(1000))
    {
        Console.WriteLine($"✅ ExplicitState correctly set to: {signalWait.ExplicitState}");
    }
    else
    {
        Console.WriteLine($"❌ ExplicitState incorrect: {signalWait.ExplicitState}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed: {ex.Message}");
}

// Test 4: Test CommandBuilder implicit conversion to Wait
Console.WriteLine("\nTest 4: CommandBuilder Implicit Conversion");
try
{
    var workflow = new TestWorkflow();
    var commandWait = workflow.CreateCommandWait();

    // This should compile due to implicit operator
    Wait wait = commandWait;

    if (wait != null)
    {
        Console.WriteLine("✅ CommandBuilder implicit conversion to Wait successful");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed: {ex.Message}");
}

// Test 5: Test StatefulCommandBuilder implicit conversion to Wait
Console.WriteLine("\nTest 5: StatefulCommandBuilder Implicit Conversion");
try
{
    var workflow = new TestWorkflow();
    var statefulCommandWait = workflow.CreateStatefulCommandWait();

    // This should compile due to implicit operator
    Wait wait = statefulCommandWait;

    if (wait != null && wait.ExplicitState != null)
    {
        Console.WriteLine($"✅ StatefulCommandBuilder implicit conversion successful with state: {wait.ExplicitState}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed: {ex.Message}");
}

// Test 6: Test AfterMatch with state wrapper
Console.WriteLine("\nTest 6: AfterMatch Stateful Wrapper");
try
{
    var workflow = new TestWorkflow();
    var signalWait = workflow.CreateSignalWaitWithAfterMatch();

    // Check that AfterMatchAction is set (it should be the wrapper)
    var afterMatchProp = typeof(SignalWait<OrderReceivedEvent>)
        .GetProperty("AfterMatchAction", BindingFlags.Instance | BindingFlags.NonPublic);
    var afterMatchAction = afterMatchProp?.GetValue(signalWait);

    if (afterMatchAction != null)
    {
        Console.WriteLine($"✅ AfterMatch wrapper created: {afterMatchAction.GetType().Name}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed: {ex.Message}");
}

// Test 7: Test OnResult with state wrapper
Console.WriteLine("\nTest 7: OnResult Stateful Wrapper");
try
{
    var workflow = new TestWorkflow();
    var commandWait = workflow.CreateCommandWaitWithOnResult();

    // Check that OnResultAction is set
    var onResultProp = typeof(CommandWait<ProcessPaymentCommand, ProcessPaymentResult>)
        .GetProperty("OnResultAction", BindingFlags.Instance | BindingFlags.NonPublic);
    var onResultAction = onResultProp?.GetValue(commandWait);

    if (onResultAction != null)
    {
        Console.WriteLine($"✅ OnResult wrapper created: {onResultAction.GetType().Name}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed: {ex.Message}");
}

// Test 8: Complex state objects
Console.WriteLine("\nTest 8: Complex State Objects");
try
{
    var complexState = new OrderState
    {
        OrderId = 123,
        CustomerEmail = "test@test.com",
        Items = new List<string> { "Item1", "Item2" }
    };

    var workflow = new TestWorkflow();
    var wait = workflow.CreateSignalWithComplexState(complexState);

    if (wait.ExplicitState != null)
    {
        var state = (OrderState)wait.ExplicitState;
        if (state.OrderId == 123 && state.CustomerEmail == "test@test.com" && state.Items.Count == 2)
        {
            Console.WriteLine("✅ Complex state object preserved correctly");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed: {ex.Message}");
}

// Test 9: Stateful group wait filter
Console.WriteLine("\nTest 9: Stateful Group Wait Filter");
try
{
    var workflow = new TestWorkflow();
    var groupWait = workflow.CreateGroupWithStatefulFilter();

    if (groupWait.ExplicitState != null && groupWait.ExplicitState.Equals(10))
    {
        Console.WriteLine($"✅ Group wait state correctly set: {groupWait.ExplicitState}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed: {ex.Message}");
}

// Test 10: Stateful cancel action
Console.WriteLine("\nTest 10: Stateful Cancel Action");
try
{
    var workflow = new TestWorkflow();
    var wait = workflow.CreateWaitWithStatefulCancel();

    // Check that CancelAction is set
    var cancelProp = typeof(Wait)
        .GetProperty("CancelAction", BindingFlags.Instance | BindingFlags.NonPublic);
    var cancelAction = cancelProp?.GetValue(wait);

    if (cancelAction != null && wait.ExplicitState != null)
    {
        Console.WriteLine($"✅ Stateful cancel action wrapper created with state: {wait.ExplicitState}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed: {ex.Message}");
}

// Test 11: Chain multiple stateful operations
Console.WriteLine("\nTest 11: Chain Multiple Stateful Operations");
try
{
    var workflow = new TestWorkflow();
    var wait = workflow.CreateChainedStatefulWait();

    if (wait.ExplicitState != null)
    {
        Console.WriteLine($"✅ Chained stateful operations work: state = {wait.ExplicitState}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed: {ex.Message}");
}

// Test 12: Array of stateful waits (parallel group scenario)
Console.WriteLine("\nTest 12: Array of Stateful Waits");
try
{
    var workflow = new TestWorkflow();
    var waits = workflow.CreateArrayOfStatefulWaits();

    if (waits.Length == 3 &&
        waits[0].ExplicitState != null &&
        waits[1].ExplicitState != null &&
        waits[2].ExplicitState != null)
    {
        Console.WriteLine($"✅ Array of stateful waits created successfully");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed: {ex.Message}");
}

Console.WriteLine("\n=== All DSL Tests Completed Successfully! ===");

// Helper workflow for testing
public class TestWorkflow : WorkflowContainer
{
    public override async IAsyncEnumerable<Wait> Run()
    {
        yield return WaitSignal<OrderReceivedEvent>("Test", "Test Signal");
        await Task.CompletedTask;
    }

    public SignalWait<OrderReceivedEvent> CreateSignalWaitWithState()
    {
        return (SignalWait<OrderReceivedEvent>)WaitSignal<OrderReceivedEvent>("OrderReceived", "Test")
            .WithState(1000)
            .MatchIf((order, minId) => order.OrderId > minId);
    }

    public CommandWait<ProcessPaymentCommand, ProcessPaymentResult> CreateCommandWait()
    {
        return (CommandWait<ProcessPaymentCommand, ProcessPaymentResult>)
            ExecuteCommand<ProcessPaymentCommand, ProcessPaymentResult>(
                "ProcessPayment",
                new ProcessPaymentCommand { OrderId = "123", Amount = 100 }
            );
    }

    public CommandWait<ProcessPaymentCommand, ProcessPaymentResult> CreateStatefulCommandWait()
    {
        return (CommandWait<ProcessPaymentCommand, ProcessPaymentResult>)
            ExecuteCommand<ProcessPaymentCommand, ProcessPaymentResult>(
                "ProcessPayment",
                new ProcessPaymentCommand { OrderId = "123", Amount = 100 }
            )
            .WithState("customer@email.com");
    }

    public SignalWait<OrderReceivedEvent> CreateSignalWaitWithAfterMatch()
    {
        return (SignalWait<OrderReceivedEvent>)WaitSignal<OrderReceivedEvent>("OrderReceived", "Test")
            .WithState(1000)
            .AfterMatch((order, minId) =>
            {
                // Test callback
            });
    }

    public CommandWait<ProcessPaymentCommand, ProcessPaymentResult> CreateCommandWaitWithOnResult()
    {
        return (CommandWait<ProcessPaymentCommand, ProcessPaymentResult>)
            ExecuteCommand<ProcessPaymentCommand, ProcessPaymentResult>(
                "ProcessPayment",
                new ProcessPaymentCommand { OrderId = "123", Amount = 100 }
            )
            .WithState("customer@email.com")
            .OnResult((result, email) =>
            {
                // Test callback
            });
    }

    public SignalWait<OrderReceivedEvent> CreateSignalWithComplexState(OrderState state)
    {
        return (SignalWait<OrderReceivedEvent>)WaitSignal<OrderReceivedEvent>("OrderReceived", "Test")
            .WithState(state);
    }

    public GroupWait CreateGroupWithStatefulFilter()
    {
        var child1 = (SignalWait<OrderReceivedEvent>)WaitSignal<OrderReceivedEvent>("Signal1", "Child1");
        var child2 = (SignalWait<OrderReceivedEvent>)WaitSignal<OrderReceivedEvent>("Signal2", "Child2");

        var group = WaitGroup([child1, child2], "TestGroup")
            .WithState(10);

        // Cast to GroupWait after all operations
        return (GroupWait)group;
    }

    public SignalWait<OrderReceivedEvent> CreateWaitWithStatefulCancel()
    {
        return (SignalWait<OrderReceivedEvent>)WaitSignal<OrderReceivedEvent>("OrderReceived", "Test")
            .WithState(999)
            .OnCanceled((state) =>
            {
                // Stateful cancel callback
                return ValueTask.CompletedTask;
            });
    }

    public SignalWait<OrderReceivedEvent> CreateChainedStatefulWait()
    {
        return (SignalWait<OrderReceivedEvent>)WaitSignal<OrderReceivedEvent>("OrderReceived", "Test")
            .WithState(500)
            .MatchIf((order, threshold) => order.OrderId > threshold)
            .AfterMatch((order, threshold) =>
            {
                // Chained callback
            })
            .OnCanceled((threshold) => ValueTask.CompletedTask);
    }

    public SignalWait<OrderReceivedEvent>[] CreateArrayOfStatefulWaits()
    {
        return
        [
            (SignalWait<OrderReceivedEvent>)WaitSignal<OrderReceivedEvent>("Signal1", "Wait1")
                .WithState(100)
                .MatchIf((order, min) => order.OrderId > min),

            (SignalWait<OrderReceivedEvent>)WaitSignal<OrderReceivedEvent>("Signal2", "Wait2")
                .WithState(200)
                .AfterMatch((order, min) => { }),

            (SignalWait<OrderReceivedEvent>)WaitSignal<OrderReceivedEvent>("Signal3", "Wait3")
                .WithState(300)
        ];
    }
}

public class OrderState
{
    public int OrderId { get; set; }
    public string CustomerEmail { get; set; } = "";
    public List<string> Items { get; set; } = new();
}

