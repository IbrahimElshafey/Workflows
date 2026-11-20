using System;

namespace Workflows.Hosting.InProcess
{
    /// <summary>
    /// Configuration for <see cref="CompensationWorker"/> retry, backoff and sweep behaviour.
    /// Register a custom instance before calling AddWorkflowsInProcessHost to override the defaults:
    /// <code>services.AddSingleton(new CompensationWorkerOptions { MaxAttempts = 5 });</code>
    /// </summary>
    public class CompensationWorkerOptions
    {
        /// <summary>
        /// Maximum number of compensation attempts per compensation wait before the instance is
        /// transitioned to <c>WorkflowInstanceStatus.CompensationFailed</c> and a DLQ entry is emitted.
        /// Default: 3.
        /// </summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>
        /// Delay before the first retry. Default: 1 second.
        /// </summary>
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Multiplier applied to the delay after every failed attempt. Default: 2.
        /// </summary>
        public double BackoffMultiplier { get; set; } = 2;

        /// <summary>
        /// Upper bound for the computed backoff delay. Default: 30 seconds.
        /// </summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Interval between database sweeps that look for instances stuck in <c>InError</c>.
        /// Channel notifications provide the fast path; the sweep is the safety net. Default: 1 second.
        /// </summary>
        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(1);

        internal int EffectiveMaxAttempts => MaxAttempts < 1 ? 1 : MaxAttempts;

        internal TimeSpan EffectiveInitialDelay => InitialDelay <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : InitialDelay;

        internal double EffectiveBackoffMultiplier => BackoffMultiplier < 1 ? 2 : BackoffMultiplier;

        internal TimeSpan EffectiveMaxDelay => MaxDelay <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : MaxDelay;

        internal TimeSpan EffectiveSweepInterval => SweepInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : SweepInterval;
    }
}