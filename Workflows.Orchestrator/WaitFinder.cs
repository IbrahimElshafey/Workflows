using System;
using System.Collections.Generic;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;

namespace Workflows.Orchestrator
{
    public static class WaitFinder
    {
        public static WaitInfrastructureDto? FindWaitingRecordForSignal(IEnumerable<WaitInfrastructureDto>? waits, string signalPath)
        {
            if (waits == null) return null;

            foreach (var w in waits)
            {
                if (w is SignalWaitDto signalWait && signalWait.SignalIdentifier == signalPath && signalWait.Status == WaitStatus.Waiting)
                {
                    return signalWait;
                }
                if (w is TimeWaitDto timeWait && timeWait.UniqueMatchId == signalPath && timeWait.Status == WaitStatus.Waiting)
                {
                    return timeWait;
                }

                if (w.ChildWaits != null && w.ChildWaits.Count > 0)
                {
                    var child = FindWaitingRecordForSignal(w.ChildWaits, signalPath);
                    if (child != null) return child;
                }

                if (w is ExternalGroupWaitDto externalGroup && externalGroup.ExternalChildWaits != null)
                {
                    var child = FindWaitingRecordForSignal(externalGroup.ExternalChildWaits, signalPath);
                    if (child != null) return child;
                }
            }

            return null;
        }

        public static CommandWaitDto? FindWaitingRecordForCommand(IEnumerable<WaitInfrastructureDto>? waits, string commandWaitId)
        {
            if (waits == null) return null;

            foreach (var w in waits)
            {
                if (w is CommandWaitDto commandWait && commandWait.Id == commandWaitId)
                {
                    return commandWait;
                }

                if (w.ChildWaits != null && w.ChildWaits.Count > 0)
                {
                    var child = FindWaitingRecordForCommand(w.ChildWaits, commandWaitId);
                    if (child != null) return child;
                }

                if (w is ExternalGroupWaitDto externalGroup && externalGroup.ExternalChildWaits != null)
                {
                    var child = FindWaitingRecordForCommand(externalGroup.ExternalChildWaits, commandWaitId);
                    if (child != null) return child;
                }
            }

            return null;
        }

        public static WaitInfrastructureDto? FindWaitById(IEnumerable<WaitInfrastructureDto>? waits, string id)
        {
            if (waits == null) return null;

            foreach (var w in waits)
            {
                if (w.Id == id)
                {
                    return w;
                }

                if (w.ChildWaits != null && w.ChildWaits.Count > 0)
                {
                    var child = FindWaitById(w.ChildWaits, id);
                    if (child != null) return child;
                }

                if (w is ExternalGroupWaitDto externalGroup && externalGroup.ExternalChildWaits != null)
                {
                    var child = FindWaitById(externalGroup.ExternalChildWaits, id);
                    if (child != null) return child;
                }
            }

            return null;
        }
    }
}
