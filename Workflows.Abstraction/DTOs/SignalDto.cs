using Workflows.Handler.Helpers;
using System.Dynamic;
using System.Reflection;


using System;
using System.Collections.Generic;
using System.Linq;

namespace Workflows.Abstraction.DTOs
{
    public class SignalDto
    {
        public Guid Id { get; internal set; }
        public object Data { get; internal set; }
        public DateTime Created { get; internal set; }
        public string SignalIdentifier { get; internal set; }
    }
}