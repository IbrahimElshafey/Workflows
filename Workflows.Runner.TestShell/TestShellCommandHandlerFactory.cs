using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Abstraction.Runner;

namespace Workflows.TestShell
{
    public class TestShellCommandHandlerFactory : ICommandHandlerFactory
    {
        private readonly Dictionary<string, object> _handlers = new();

        public void RegisterHandler<TCommand, TResult>(string handlerKey, Func<TCommand, Task<TResult>> handler)
        {
            _handlers[handlerKey] = handler;
        }

        public object GetHandler(string handlerKey)
        {
            _handlers.TryGetValue(handlerKey, out var handler);
            return handler ?? null!;
        }
    }
}
