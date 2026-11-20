using Workflows.Definition;

namespace Workflows.Benchmarks
{
    /// <summary>
    /// Looping workflow used by the benchmarks: each signal completes one wait and yields the next, so a
    /// single instance can be resumed indefinitely without re-seeding. The wait name carries the tick count
    /// because the runner rejects a wait whose name collides with a wait still held by the snapshot - the
    /// wait being resumed is still present while its replacement is yielded.
    /// </summary>
    [Workflow("BenchmarkLoopWorkflow", 1)]
    public sealed class BenchmarkLoopWorkflow : WorkflowContainer
    {
        public int Ticks { get; set; }

        public async IAsyncEnumerable<Wait> Run(BenchmarkLoopState state = null!)
        {
            while (true)
            {
                yield return WaitSignal<BenchmarkSignal>("BenchTick", $"Tick {Ticks}")
                    .AfterMatch((BenchmarkSignal signal) => Ticks++);
            }
        }
    }

    public sealed class BenchmarkSignal
    {
        public int Value { get; set; }
    }

    public sealed class BenchmarkLoopState
    {
    }
}