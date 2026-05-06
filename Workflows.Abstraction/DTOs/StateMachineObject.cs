using System.Collections.Generic;

namespace Workflows.Abstraction.DTOs
{
    public class StateMachineObject
    {
        public int StateIndex { get; set; }
        public object Instance { get; set; }
        public Dictionary<string, object> Variables { get; set; } = new Dictionary<string, object>();
    }
}
