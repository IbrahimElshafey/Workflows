using Workflows.Sender.InOuts;
using System;
using System.Threading.Tasks;

namespace Workflows.Sender.Abstraction
{
    public interface ISignalSender
    {
        //todo: Candidate for Inbox/Outbox pattern
        //todo: this will be seprated to class library
        //we can use this interface to publish the method call to the queue
        //the queue will be consumed by the worker
        Task Send<TInput, TOutput>(
            Func<TInput, Task<TOutput>> methodToPush,
            TInput input,
            TOutput output,
            string methodUrn,
            params string[] toServices);
        Task Send(MethodCall MethodCall);
    }
}
