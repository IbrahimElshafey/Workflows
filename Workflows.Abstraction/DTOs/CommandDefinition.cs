using System;
using Workflows.Abstraction.Enums;

namespace Workflows.Abstraction.DTOs
{
    public class CommandDefinition
    {
        /// <summary>
        /// The unique name of the command (e.g., "EmailService.Send").
        /// </summary>
        public string CommandName { get; set; }

        /// <summary>
        /// The .NET type name of the request object sent to the external system.
        /// </summary>
        public string RequestTypeName { get; set; }

        /// <summary>
        /// The .NET type name of the result object expected back.
        /// </summary>
        public string ResultTypeName { get; set; }

        /// <summary>
        /// Metadata regarding the expected timeout for this command.
        /// </summary>
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);

        public CommandExecutionMode ExecutionMode { get; set; }
    }
}
