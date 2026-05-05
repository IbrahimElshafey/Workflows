using System;
using System.Collections.Generic;
using System.Linq;

namespace Workflows.Runner.DataObjects
{
    public class MachineState
    {
        public int StateIndex { get; set; }
        public object Instance { get; set; }
        public Dictionary<string, object> Variables { get; set; } = new Dictionary<string, object>();
    }
}