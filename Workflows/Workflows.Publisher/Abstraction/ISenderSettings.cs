using System;
using System.Collections.Generic;
using Workflows.Publisher.Abstraction;
using Workflows;
using Workflows.Publisher;

namespace Workflows.Publisher.Abstraction;

public interface ISenderSettings
{
    Dictionary<string, string> ServicesRegistry { get; }
    Type SignalSenderType { get; }
    TimeSpan CheckFailedRequestEvery { get; }
}
