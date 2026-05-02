using System;
using System.Collections.Generic;
using Workflows.Definition.Data.DTOs;

namespace Workflows.Definition
{
    /// <summary>
    /// Represents a passive wait for a specified time duration or until a specific time.
    /// Time-based waits do not initiate side effects, so they can be safely combined
    /// with other passive waits in group scenarios.
    /// </summary>
    public class TimeWait : Wait, IPassiveWait
    {
        internal TimeWaitDto Data { get; }
        internal TimeWait(TimeWaitDto waitData) : base(waitData)
        {
            Data = waitData;
        }

        public HashSet<string> CancelTokens { get; set; }

        public TimeWait WithCancelToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return this;
            CancelTokens ??= new HashSet<string>();
            CancelTokens.Add(token);
            return this;
        }

        IPassiveWait IPassiveWait.WithCancelToken(string token) => WithCancelToken(token);
    }
}