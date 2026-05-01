using System;
using Workflows.Abstraction.Enums;

namespace Workflows.Abstraction.DTOs
{
    public class CommandRegistrationInput<TCommand, TResult>
    {
        public CommandRegistrationInput()
        {
            CommandInputType = typeof(TCommand);
            CommandResultType = typeof(TResult);
        }
        public CommandRegistrationInput(string handlerKey, CommandExecutionMode commandExecutionMode) : this()
        {
            HandlerKey = handlerKey;
            CommandExecutionMode = commandExecutionMode;
        }

        public Type CommandInputType { get; private set; }
        public Type CommandResultType { get; private set; }
        public string? HandlerKey { get; private set; }
        public CommandExecutionMode CommandExecutionMode { get; }
    }
}