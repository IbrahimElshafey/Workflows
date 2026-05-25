using System;
using System.Reflection;

namespace Workflows.Abstraction.Helpers
{
    internal interface IDelegateSerializer
    {
        string Serialize(Delegate callback);
        MethodInfo Deserialize(string methodFullPath);
    }
}
