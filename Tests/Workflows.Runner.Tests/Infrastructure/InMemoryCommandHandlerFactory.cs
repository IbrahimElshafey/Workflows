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

        public object GetHandler(string handlerKey)
        {
            if (_handlers.TryGetValue(handlerKey, out var handler))
            {
                return handler;
            }

            // Return a default handler that creates mock results
            return new Func<object, Task<object>>(cmd => Task.FromResult<object>(new
            {
                Success = true,
                Message = $"Mock result for {handlerKey}"
            }));
        }
    }
}
