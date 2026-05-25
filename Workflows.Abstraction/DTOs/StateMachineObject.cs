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

        private object _instance;

        public object Instance
        {
            get => _instance;
            set => _instance = value;
        }

        public StateMachineObject() : base() { }
        public StateMachineObject(IDictionary<string, object> dictionary) : base(dictionary)
        {
            if (dictionary != null && dictionary.TryGetValue("$this", out var inst))
            {
                _instance = inst;
                Remove("$this");
            }
        }
    }
}
