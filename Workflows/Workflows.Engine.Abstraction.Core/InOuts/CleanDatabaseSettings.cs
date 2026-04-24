using System;
namespace Workflows.Handler.InOuts
{
    public class CleanDatabaseSettings
    {
        public string RunCleaningCron { get; set; } = Cron.Daily();
        public string MarkInactiveWaitTemplatesCron { get; set; } = Cron.Daily();

        public TimeSpan CompletedInstanceRetention { get; set; } = TimeSpan.FromDays(30);
        public TimeSpan SignalRetention { get; set; } = TimeSpan.FromDays(10);
        public TimeSpan DeactivatedWaitTemplateRetention { get; set; } = TimeSpan.FromDays(10);
    }
}