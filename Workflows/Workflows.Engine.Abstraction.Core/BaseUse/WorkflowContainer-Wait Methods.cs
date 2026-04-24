using Workflows.Handler.BaseUse;
using Workflows.Handler.InOuts;
using Workflows.Handler.InOuts.Entities;
using System.Runtime.CompilerServices;

using System;using System.Threading.Tasks; namespace Workflows.Handler
{
    public abstract partial class WorkflowContainer
    {

        protected MethodWait<TInput, TOutput> WaitMethod<TInput, TOutput>(
            Func<TInput, TOutput> method,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = ""
            )
        {
            return new MethodWaitEntity<TInput, TOutput>(method)
            {
                Name = name ?? $"#Wait Method `{method.Method.Name}`",
                WaitType = WaitType.MethodWait,
                CurrentWorkflow = this,
                InCodeLine = inCodeLine,
                CallerName = callerName,
                Created = DateTime.UtcNow,
            }.ToMethodWait();
        }

        protected MethodWait<TInput, TOutput> WaitMethod<TInput, TOutput>(
            Func<TInput, Task<TOutput>> method,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            return new MethodWaitEntity<TInput, TOutput>(method)
            {
                Name = name ?? $"#Wait Method `{method.Method.Name}`",
                WaitType = WaitType.MethodWait,
                CurrentWorkflow = this,
                InCodeLine = inCodeLine,
                CallerName = callerName,
                Created = DateTime.UtcNow
            }.ToMethodWait();
        }

        protected WaitsGroup WaitGroup(
            Wait[] waits,
            string name = null,
            [CallerLineNumber] int inCodeLine = 0,
            [CallerMemberName] string callerName = "")
        {
            if (waits.Any(x => x == null))
            {
                throw new ArgumentNullException($"The group wait named [{name}] contains wait that is null.");
            }
            var group = new WaitsGroupEntity
            {
                Name = name ?? $"#Wait Group `{inCodeLine}` by `{callerName}`",
                ChildWaits = waits.Select(x => x.WaitEntity).ToList(),
                WaitType = WaitType.GroupWaitAll,
                CurrentWorkflow = this,
                InCodeLine = inCodeLine,
                CallerName = callerName,
                Created = DateTime.UtcNow,
            };
            group.ChildWaits.ForEach(wait => wait.ParentWait = group);
            return group.ToWaitsGroup();
        }
    }
}