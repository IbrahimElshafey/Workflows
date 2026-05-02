using System;

namespace Workflows.Definition.Data.DTOs
{
    public class PrivateData
    {
        public object Value { get; internal set; }

        //Todo: If we could get type at runtime why we save it??
        public string TypeName { get; internal set; }
        public DateTime Created { get; internal set; }
    }
}
