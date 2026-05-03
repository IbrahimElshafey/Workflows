using System;
using System.Collections.Generic;
using System.Linq;

namespace Workflows.Runner.DataObjects
{
    internal class MachineState
    {
        public int State { get; set; }
        public Dictionary<string, object> Closures { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> Locals { get; set; } = new Dictionary<string, object>();
    }
}