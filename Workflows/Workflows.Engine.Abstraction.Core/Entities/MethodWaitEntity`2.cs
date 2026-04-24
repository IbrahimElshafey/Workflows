
using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Workflows.Handler.Attributes;
using Workflows.Handler.BaseUse;

using Workflows.Handler.Expressions;
namespace Workflows.Handler.InOuts.Entities
{
    public class MethodWaitEntity<TInput, TOutput> : MethodWaitEntity
    {

        internal MethodWaitEntity(Func<TInput, Task<TOutput>> method) => Initiate(method.Method);
        internal MethodWaitEntity(Func<TInput, TOutput> method) => Initiate(method.Method);
        internal MethodWaitEntity(MethodInfo methodInfo) => Initiate(methodInfo);

        private void Initiate(MethodInfo method)
        {
            var methodAttribute =
                method.GetCustomAttribute(typeof(EmitSignalAttribute));

            if (methodAttribute == null)
                throw new Exception(
                    $"You must add attribute [{nameof(EmitSignalAttribute)}] to method [{method.GetFullName()}]");

            MethodData = new MethodData(method);
            Name = $"#Wait Method `{method.Name}`";
        }

        internal MethodWaitEntity<TInput, TOutput> AfterMatch(Action<TInput, TOutput> afterMatchAction)
        {
            AfterMatchAction = ValidateCallback(afterMatchAction, nameof(AfterMatchAction));
            return this;
        }

        internal MethodWaitEntity<TInput, TOutput> MatchIf(Expression<Func<TInput, TOutput, bool>> matchExpression)
        {
            MatchExpression = matchExpression;
            MatchExpressionParts = new MatchExpressionWriter(MatchExpression, CurrentWorkflow).MatchExpressionParts;
            if (ClosureObject != null &&
                MatchExpressionParts.Closure != null &&
                ClosureObject.GetType() != MatchExpressionParts.Closure.GetType())
                throw new Exception(
                    $"For wait [{Name}] the closure must be same for AfterMatchAction,CancelAction and MatchExpression.");
            SetClosureObject(MatchExpressionParts.Closure);
            MandatoryPart = MatchExpressionParts.GetInstanceMandatoryPart(CurrentWorkflow);
            return this;
        }



        internal MethodWaitEntity<TInput, TOutput> WhenCancel(Action cancelAction)
        {
            CancelMethodAction = ValidateCallback(cancelAction, nameof(CancelMethodAction));
            return this;
        }

        internal MethodWaitEntity<TInput, TOutput> MatchAny()
        {
            MatchExpression = (Expression<Func<TInput, TOutput, bool>>)((_, _) => true);
            MatchExpressionParts = new MatchExpressionWriter(MatchExpression, CurrentWorkflow).MatchExpressionParts;
            return this;
        }

        internal MethodWaitEntity<TInput, TOutput> NoActionAfterMatch()
        {
            AfterMatchAction = string.Empty;
            return this;
        }

        internal MethodWait<TInput, TOutput> ToMethodWait() => new MethodWait<TInput, TOutput>(this);
    }
}