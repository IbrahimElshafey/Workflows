using System;
using System.Linq;
using Workflows.Definition;

namespace Workflows.Runner.DataObjects
{
    public class AdvancerResult
    {
        public Wait Wait { get; set; }
        public MachineState State { get; set; }
    }
}