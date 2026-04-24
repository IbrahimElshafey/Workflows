using Workflows.Handler;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;
using Workflows.Handler.Helpers;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using Workflows.Handler.Testing;

namespace Tests
{
    public class TimeWaitTests
    {
        [Fact]
        public async Task TestTimeWaitAtStart_Test()
        {
            using var test = new TestShell(nameof(TestTimeWaitAtStart_Test), typeof(TimeWaitWorkflow));
            await test.ScanTypes();
            var timeWaitId = await RoundTest(test, 1);

            await test.SimulateMethodCall<LocalRegisteredMethods>(
                x => x.TimeWait(new TimeWaitInput { TimeMatchId = timeWaitId }), true);
            timeWaitId = await RoundTest(test, 2);

            await test.SimulateMethodCall<LocalRegisteredMethods>(
                x => x.TimeWait(new TimeWaitInput { TimeMatchId = timeWaitId }), true);
            await RoundTest(test, 3);
        }

        private async Task<string> RoundTest(TestShell test, int round)
        {
            var signals = await test.GetSignals();
            var waits = await test.GetWaits(null, true);
            var instances = await test.GetInstances<TimeWaitWorkflow>(true);
            Assert.Equal(round - 1, signals.Count);
            Assert.Equal(round, waits.Count);
            Assert.Equal(round, instances.Count);
            var errors = await test.GetLogs();
            Assert.Empty(errors);
            return (waits.First(x => x.IsFirst) as MethodWaitEntity)?.MandatoryPart;
        }
    }

    public class TimeWaitWorkflow : WorkflowContainer
    {
        public string TimeWaitId { get; set; }

        [Workflow("TestTimeWait")]
        public async IAsyncEnumerable<Wait> TestTimeWaitAtStart()
        {
            int localCounter = 10;
            yield return
                WaitDelay(TimeSpan.FromDays(2), "Wait Two Days")
                .AfterMatch((x, _) =>
                {
                    TimeWaitId = x.TimeMatchId;
                    if (localCounter != 10)
                        throw new Exception("Local counter not get correct in  TimeWait.AfterMatch callback.");
                    localCounter += 10;
                });
            if (localCounter != 20)
                throw new Exception("Local counter not set correct in  TimeWait.AfterMatch callback.");
            Console.WriteLine("Time wait at start matched.");
        }
    }
}