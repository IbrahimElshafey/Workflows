using System;
using System.Collections.Generic;

namespace Workflows.Definition
{
    /// <summary>
    /// Represents a passive wait for a specified time duration or until a specific time.
    /// Time-based waits do not initiate side effects, so they can be safely combined
    /// with other passive waits in group scenarios.
    /// </summary>
    public class TimeWait : Definition.Wait, Definition.IPassiveWait
    {
        internal DTOs.TimeWaitDto Data { get; }
        internal TimeWait(DTOs.TimeWaitDto waitData) : base(waitData)
        {
            Data = waitData;
        }
        public Definition.TimeWait WhenCancel(Action cancelAction)
        {
            CancelAction = cancelAction;
            return this;
        }

        public HashSet<string> CancelTokens { get; set; }

        public Definition.TimeWait WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens ??= new HashSet<string>();
            CancelTokens.Add(token);
            return this;
        }

        Definition.IPassiveWait Definition.IPassiveWait.WithCancelToken(string token) => WithCancelToken(token);
    }
}