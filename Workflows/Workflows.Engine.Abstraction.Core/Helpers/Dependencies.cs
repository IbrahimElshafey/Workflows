using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Workflows.Engine.Abstraction.Core.Abstraction.Serialization;
using Workflows.Handler.Abstraction.Serialization;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;

namespace Workflows.Handler.Helpers
{
    internal static class Dependencies
    {
        internal static IBinarySerializer BinarySerializer { get; }
        internal static IJsonSerializer JsonSerializer { get; }
        internal static ExpressionSerializer ExpressionSerializer { get; }
    }
}
