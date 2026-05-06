using System;
using Workflows.Primitives;

namespace Workflows.Abstraction.DTOs.Registration
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
