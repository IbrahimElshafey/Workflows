using Workflows.Publisher.Abstraction;
using Workflows.Publisher.InOuts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Workflows.Publisher.Implementation;
using Workflows;
using Workflows.Publisher;

namespace Workflows.Publisher.Implementation
{
    public class InMemoryFailedRequestRepo : Abstraction.IFailedRequestStore
    {
        private readonly ConcurrentDictionary<Guid, InOuts.FailedRequest> _failedRequests = new ConcurrentDictionary<Guid, InOuts.FailedRequest>();

        public Task Add(InOuts.FailedRequest request)
        {
            _failedRequests.TryAdd(request.Key, request);
            return Task.CompletedTask;
        }

        public IEnumerable<InOuts.FailedRequest> GetRequests()
        {
            var enumerator = _failedRequests.GetEnumerator();
            while (enumerator.MoveNext())
                yield return enumerator.Current.Value;
        }

        public Task<bool> HasRequests() => Task.FromResult(_failedRequests.Count > 0);

        public Task Remove(InOuts.FailedRequest request)
        {
            _failedRequests.TryRemove(request.Key, out _);
            return Task.CompletedTask;
        }

        public Task Update(InOuts.FailedRequest request) => Task.CompletedTask;
    }
}
