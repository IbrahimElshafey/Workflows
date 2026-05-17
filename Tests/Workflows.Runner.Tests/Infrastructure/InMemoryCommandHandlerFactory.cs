using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.DTOs;
using Workflows.Abstraction.Runner;

namespace Workflows.Runner.Tests.Infrastructure
{
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

            public async Task<object> ExecuteAsync(object commandData)
            {
                if (commandData == null)
                {
                    return null;
                }

                try
                {
                    return await _handler(commandData);
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
