using System.Linq.Expressions;

using System;using System.Threading.Tasks; namespace Workflows.Handler.Core.Abstraction
{
    public interface IBackgroundProcess
    {
        public string Enqueue(Expression<Func<Task>> methodCall);
        bool Delete(string jobId);
        string Schedule(Expression<Func<Task>> methodCall, TimeSpan delay);
        string Schedule(Expression<Action> methodCall, TimeSpan delay);
        void AddOrUpdateRecurringJob<TClass>(
            string recurringJobId,
            Expression<Func<TClass, Task>> methodCall,
            string cronExpression);
    }
}