using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;

namespace Workflows.Runner.Tests.Infrastructure
{
    /// <summary>
    /// In-memory command handler factory for testing
    /// </summary>
    internal class InMemoryCommandHandlerFactory : ICommandHandlerFactory
    {
        private readonly Dictionary<string, Func<object, Task<object>>> _handlers = new();

        public void RegisterHandler<TCommand, TResult>(string handlerKey, Func<TCommand, Task<TResult>> handler)
        {
            _handlers[handlerKey] = async (cmd) => await handler((TCommand)cmd);
        }

        public ICommandHandler GetHandler(string handlerKey)
        {
            if (_handlers.TryGetValue(handlerKey, out var handler))
            {
                return new InMemoryCommandHandler(handler);
            }

            // Return a default handler that creates mock results
            return new InMemoryCommandHandler(cmd => Task.FromResult<object>(new
            {
                Success = true,
                Message = $"Mock result for {handlerKey}"
            }));
        }

        private class InMemoryCommandHandler : ICommandHandler
        {
            private readonly Func<object, Task<object>> _handler;

            public InMemoryCommandHandler(Func<object, Task<object>> handler)
            {
                _handler = handler;
            }

            public async Task ExecuteAsync(ICommandWait commandWait, WorkflowExecutionRequest context)
            {
                // Extract command data from wait
                var commandWaitType = commandWait.GetType();
                var commandDataProperty = commandWaitType.GetProperty("CommandData",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var commandData = commandDataProperty?.GetValue(commandWait);

                if (commandData == null)
                {
                    return;
                }

                try
                {
                    var result = await _handler(commandData);
                    // Store result in context for command wait processing
                    context.CommandResult = result;
                }
                catch
                {
                    // Ignore exceptions in tests
                }
            }
        }
    }
}
