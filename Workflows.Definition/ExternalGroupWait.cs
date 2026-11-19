using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Workflows.Primitives;

namespace Workflows.Definition
{
    /// <summary>
    /// Represents a massive fan-out wait whose child wait metadata is persisted
    /// in a dedicated relational table instead of the JSON state blob.
    /// </summary>
    public class ExternalGroupWait : Wait
    {
        internal ExternalGroupWait(
            string waitName,
            IReadOnlyList<Wait> childWaits,
            WaitType waitType,
            int inCodeLine,
            string callerName,
            string callerFilePath)
            : base(waitType, waitName, inCodeLine, callerName, callerFilePath)
        {
            ChildWaits = childWaits?.ToList() ?? new List<Wait>();
        }

        internal ExternalGroupWait()
        {
        }

        internal IReadOnlyList<Wait> ChildWaits { get; }
    }
}
