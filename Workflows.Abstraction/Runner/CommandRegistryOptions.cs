using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Workflows.Abstraction.Runner
{
    public record CommandMetadata(
        Type InputType,
        Type OutputType,
        bool IsAsync,
        bool IsExternal,
        LambdaExpression? MatchExpression // AST representation for external webhooks
    );

    public class CommandRegistryOptions
    {
        public Dictionary<string, CommandMetadata> Commands { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
