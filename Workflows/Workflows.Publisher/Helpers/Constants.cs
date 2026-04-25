using Workflows.Publisher.Helpers;
using Workflows;
using Workflows.Publisher;

namespace Workflows.Publisher.Helpers
{
    public static class Constants
    {
        public const string WorkflowsControllerUrl = "rfapi/Workflows";
        public const string ExternalCallAction = "ExternalCall";
        public const string FailedRequestsDb = "FailedRequests.litedb";
        public const string FailedRequestsCollection = "FailedRequestsCollection";
    }
}
