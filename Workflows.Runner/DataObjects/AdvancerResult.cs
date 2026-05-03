using System;
using System.Linq;
using Workflows.Definition;

namespace Workflows.Runner.DataObjects
{
    internal class AdvancerResult : MachineState
    {
        public Wait Wait { get; set; }
    }
}