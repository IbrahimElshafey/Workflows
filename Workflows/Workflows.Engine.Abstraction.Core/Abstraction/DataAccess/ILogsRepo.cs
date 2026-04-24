using Workflows.Handler.InOuts;

using System;using System.Threading.Tasks; namespace Workflows.Handler.Abstraction.Abstraction
{
    //todo: Replace this to be automatic so no diffrent interface  but standard ILogger with context
    public interface ILogsRepo
    {
        Task AddErrorLog(Exception ex, string errorMsg, int statusCode);
        Task AddLog(string msg, LogType logType, int statusCode);
        Task AddLogs(LogType logType, int statusCode, params string[] msgs);
        Task ClearErrorsForWorkflowInstance(int workflowInstanceId);
    }
}
