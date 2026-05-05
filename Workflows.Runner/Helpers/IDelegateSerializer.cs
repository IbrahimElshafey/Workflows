using System;

namespace Workflows.Runner.Helpers
{
    internal interface IDelegateSerializer
    {
        string Serialize(Delegate callback);
    }
}
