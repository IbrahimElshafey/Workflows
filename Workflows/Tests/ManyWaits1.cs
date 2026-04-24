using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;
using Workflows.Handler.Testing;

namespace Tests
{

    public partial class ManyWaits
    {
        [Fact]
        public async Task WaitThreeMethodsAtStart_Test()
        {
            using var test = new TestShell(nameof(WaitThreeMethodsAtStart_Test), typeof(WaitThreeMethodsAtStart));
            await test.ScanTypes("WaitThreeAtStart");
            var errors = await test.GetLogs();
            Assert.Empty(errors);

            var wms = new WaitThreeMethodsAtStart();
            wms.Method1("1");
            wms.Method2("2");
            wms.Method3("3");

            var signals = await test.GetSignals();
            Assert.Equal(3, signals.Count);
            errors = await test.GetLogs();
            Assert.Empty(errors);
            var waits = await test.GetWaits();
            Assert.Equal(4, waits.Count);
            var instance = await test.GetFirstInstance<WaitThreeMethodsAtStart>();
            Assert.Equal(3, instance.Counter);
            Assert.Equal(0, instance.CancelCounter);

            wms.Method1("1");
            wms.Method2("2");
            wms.Method3("3");

            signals = await test.GetSignals();
            Assert.Equal(6, signals.Count);
            errors = await test.GetLogs();
            Assert.Empty(errors);
            waits = await test.GetWaits();
            Assert.Equal(8, waits.Count);
        }

        public class WaitThreeMethodsAtStart : WorkflowContainer
        {
            [Workflow("WaitThreeAtStart")]
            public async IAsyncEnumerable<Wait> WaitThreeAtStart()
            {
                int cancelCounter = 10;
                int afterMatchCounter = 10;
                yield return WaitGroup(new[]
                    {
                    WaitMethod<string, string>(Method1, "Method 1")
                        .AfterMatch((_, _) => { Counter++; afterMatchCounter++; })
                        .WhenCancel(() => { CancelCounter++; cancelCounter++; }),
                    WaitMethod<string, string>(Method2, "Method 2")
                        .AfterMatch((_, _) => { Counter++; afterMatchCounter++; })
                        .WhenCancel(() => { CancelCounter++; cancelCounter++; }),
                    WaitMethod<string, string>(Method3, "Method 3")
                        .AfterMatch((_, _) => { Counter++; afterMatchCounter++; })
                        .WhenCancel(() => { CancelCounter++; cancelCounter++; })
                    }
,
                    "Wait three methods").MatchAll();
                if (afterMatchCounter != 13)
                    throw new Exception("Local variable not saved in after match in wait many group.");
                if (cancelCounter != 10)
                    throw new Exception("Local variable not saved in cancel in wait many group.");
                await Task.Delay(100);
                Console.WriteLine("Three method done");
            }

            public int Counter { get; set; }
            public int CancelCounter { get; set; }

            [EmitSignal("RequestAdded")]
            public string Method1(string input) => "RequestAdded Call";
            [EmitSignal("Method2")]
            public string Method2(string input) => "Method2 Call";
            [EmitSignal("Method3")] public string Method3(string input) => "Method3 Call";
        }
    }

}