using System;
using System.Threading.Tasks;

namespace Workflows.Runner.DataObjects
{
    internal class CommandTemplateCacheRecord
    {
        public Func<object, object, object> OnResultAction { get; set; }
        public Func<object, object, ValueTask> CompensationAction { get; set; }
    }
}
