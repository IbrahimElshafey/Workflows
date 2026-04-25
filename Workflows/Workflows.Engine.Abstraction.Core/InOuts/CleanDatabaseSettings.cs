using System;
namespace Workflows.Handler.InOuts
{
    public class CleanDatabaseSettings
    {
        public string RunCleaningCron { get; set; } = "0 0 * * *";
        public string MarkInactiveWaitTemplatesCron { get; set; } = "0 0 * * *";

        public TimeSpan CompletedInstanceRetention { get; set; } = TimeSpan.FromDays(30);
        public TimeSpan SignalRetention { get; set; } = TimeSpan.FromDays(10);
        public TimeSpan DeactivatedWaitTemplateRetention { get; set; } = TimeSpan.FromDays(10);
    }
}