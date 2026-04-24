using System;
using System.Collections.Generic;

namespace Workflows.Sender.Abstraction;

public interface ISenderSettings
{
    Dictionary<string, string> ServicesRegistry { get; }
    Type SignalSenderType { get; }
    TimeSpan CheckFailedRequestEvery { get; }
}
