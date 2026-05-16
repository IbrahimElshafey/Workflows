using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Workflows.Runner.Tests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")] // Required for Moq to create proxies of internal interfaces
