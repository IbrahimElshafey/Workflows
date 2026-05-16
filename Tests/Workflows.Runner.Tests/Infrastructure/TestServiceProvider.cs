using System;
using Microsoft.Extensions.DependencyInjection;

namespace Workflows.Runner.Tests.Infrastructure
{
    /// <summary>
    /// Simple service provider for testing
    /// </summary>
    internal class TestServiceProvider : IServiceProvider
    {
        private readonly IServiceProvider _inner;

        public TestServiceProvider()
        {
            var services = new ServiceCollection();
            _inner = services.BuildServiceProvider();
        }

        public object? GetService(Type serviceType)
        {
            // Try to create instances of workflow types directly
            try
            {
                return Activator.CreateInstance(serviceType);
            }
            catch
            {
                return _inner.GetService(serviceType);
            }
        }
    }
}
