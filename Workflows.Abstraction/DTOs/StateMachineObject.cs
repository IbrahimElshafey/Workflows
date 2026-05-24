using System;
using System.Collections.Generic;

namespace Workflows.Abstraction.DTOs
{
    public class StateMachineObject : Dictionary<string, object>
    {
        public int StateIndex
        {
            get => TryGetValue("$state", out var val) ? Convert.ToInt32(val) : 0;
            set => this["$state"] = value;
        }

        public object Instance
        {
            get => TryGetValue("$this", out var val) ? val : null;
            set => this["$this"] = value;
        }

        public StateMachineObject() : base() { }
        public StateMachineObject(IDictionary<string, object> dictionary) : base(dictionary) { }
    }
}
