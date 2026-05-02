using System;

namespace Workflows.Definition.Data.DTOs
{
    public class RegistrationError
    {
        public string EntityName { get; set; } // Workflow, Signal, or Command name
        public string ErrorType { get; set; } // "VersionConflict", "ValidationFailed", etc.
        public string Message { get; set; }
    }
}