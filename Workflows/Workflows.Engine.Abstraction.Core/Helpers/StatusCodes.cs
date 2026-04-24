using System.Collections.Generic;

namespace Workflows.Handler.Helpers
{
    internal static class StatusCodes
    {
        public const int MethodValidation = 1;
        public const int Scanning = 2;
        public const int WaitProcessing = 4;
        public const int Replay = 5;
        public const int Signal = 6;
        public const int FirstWait = 7;
        public const int WaitValidation = 8;
        public const int DataCleaning = 9;
        public const int Custom = -1000;

        internal static Dictionary<int, string> StatusCodeNames = new Dictionary<int, string>
        {
            {-1, "Any"},
            {MethodValidation, "Method Validation"},
            {Scanning, "Scanning Types"},
            {WaitProcessing, "Wait Processing"},
            {Replay, "Replay a wait"},
            {Signal, "Process Pushed Call"},
            {FirstWait, "First Wait Processing"},
            {WaitValidation, "Wait Request Validation"},
            {Custom, "Author Custom Log"},
            {DataCleaning, "Database Cleaning"},
        };

        internal static string NameOf(int errorCode) => StatusCodeNames[errorCode];
    }
}
