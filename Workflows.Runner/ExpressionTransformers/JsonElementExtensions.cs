using System;
using System.Linq;
using System.Text.Json;

namespace Workflows.Runner.ExpressionTransformers
{
    /// <summary>
    /// Helper to cleanly extract values from a System.Text.Json.JsonElement
    /// You will implement the actual parsing (e.g. dot-notation path traversal) inside this method.
    /// </summary>
    public static class JsonElementExtensions
    {
        /// <summary>
        /// Traverses a dot-notation path (e.g. "Order.Id") through a JsonElement
        /// and deserializes the final value to <typeparamref name="T"/>.
        /// Returns <c>default</c> if any segment is missing.
        /// </summary>
        public static T Get<T>(this JsonElement element, string path)
        {
            var current = element;
            foreach (var segment in path.Split('.'))
            {
                if (current.ValueKind != JsonValueKind.Object ||
                    !current.TryGetProperty(segment, out current))
                    return default;
            }
            return JsonSerializer.Deserialize<T>(current.GetRawText());
        }
    }
}