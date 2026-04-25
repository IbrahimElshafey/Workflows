using Workflows.Handler.BaseUse;
using Workflows.Handler.Helpers;
using System.Linq.Expressions;

using System;
namespace Workflows.Handler.InOuts.Entities
{
    public class MethodWaitEntity : WaitEntity
    {
        internal MethodWaitEntity()
        {

        }

        public string AfterMatchAction { get; protected set; }

        public LambdaExpression MatchExpression { get; protected set; }

        public string CancelMethodAction { get; protected set; }
        public MethodWaitType MethodWaitType { get; internal set; } = MethodWaitType.NormalMethod;
        public string MandatoryPart { get; internal set; }

        internal WaitTemplate Template { get; set; }
        public int TemplateId { get; internal set; }

        internal MethodIdentifier MethodToWait { get; set; }

        internal int? MethodToWaitId { get; set; }

        internal MethodsGroup MethodGroupToWait { get; set; }
        internal int MethodGroupToWaitId { get; set; }

        internal MethodData MethodData { get; set; }

        public object Input { get; internal set; }

        public object Output { get; internal set; }

        public MatchExpressionParts MatchExpressionParts { get; protected set; }

        internal bool ExecuteAfterMatchAction()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(AfterMatchAction)) return true;
                InvokeCallback(AfterMatchAction, Input, Output);
                WorkflowInstance.StateObject = CurrentWorkflow;
                WorkflowInstance.AddLog($"After wait [{Name}] action executed.", LogType.Info, StatusCodes.WaitProcessing);
                return true;
            }
            catch (Exception ex)
            {
                var error = $"An error occurred when try to execute action after wait [{Name}] matched.";
                WorkflowInstance.AddError(error, StatusCodes.WaitProcessing, ex);
                //throw new Exception(error, ex);
                return false;
            }
        }
        internal override void OnAddWait()
        {
            IsRoot = ParentWait == null && ParentWaitId == null;

            if (ClosureObject == default) return;
            base.OnAddWait();
        }



        internal bool IsMatched()
        {
            try
            {
                LoadExpressions();
                if (WasFirst && MatchExpression == null)
                    return true;
                if (MethodToWait.MethodInfo ==
                    CoreExtensions.GetMethodInfo<LocalRegisteredMethods>(x => (object)x.TimeWait(null)))
                    return true;
                var check = MatchExpression.Compile();
                var closureType = MatchExpression.Parameters[3].Type;
                var closure = GetClosure(closureType);
                return (bool)check.DynamicInvoke(Input, Output, CurrentWorkflow, closure);
            }
            catch (Exception ex)
            {
                var error = $"An error occurred when try evaluate match expression for wait [{Name}].";
                WorkflowInstance.AddError(error, StatusCodes.WaitProcessing, ex);
                throw new Exception(error, ex);
            }
        }

        internal override void Cancel()
        {
            try
            {
                if (CancelMethodAction != null)
                {
                    InvokeCallback(CancelMethodAction);
                    CurrentWorkflow.AddLog($"Execute cancel method for wait [{Name}]", LogType.Info, StatusCodes.WaitProcessing);
                }
                base.Cancel();
            }
            catch (Exception ex)
            {
                var error = $"An error occurred when try to execute cancel action when wait [{Name}] canceled.";
                WorkflowInstance.AddError(error, StatusCodes.WaitProcessing, ex);
                throw new Exception(error, ex);
            }
        }


        internal override bool ValidateWaitRequest()
        {
            if (WasFirst is false && MatchExpression == null)
            {
                WorkflowInstance.AddError(
                        $"You didn't set the [{nameof(MatchExpression)}] for wait [{Name}]," +
                        $"This will lead to no match for any call," +
                        $"You can use method {nameof(MethodWait<int, int>.MatchIf)}(Expression<Func<TInput, TOutput, bool>> value) to pass the [{nameof(MatchExpression)}]," +
                        $"or use [{nameof(MethodWait<int, int>.MatchAny)}()] method.", StatusCodes.WaitValidation, null);
            }
            else if (WasFirst is true && MatchExpression == null)
            {
                WorkflowInstance.AddLog(
                                $"You didn't set the [{nameof(MatchExpression)}] for first wait [{Name}]," +
                                $"This will lead to all calls will be matched.",
                                LogType.Warning, StatusCodes.WaitValidation);
            }

            if (AfterMatchAction == null)
                WorkflowInstance.AddLog(
                    $"You didn't called the method [{nameof(MethodWait<int, int>.AfterMatch)}] for wait [{Name}], " +
                    $"Please use [{nameof(MethodWait<int, int>.NoActionAfterMatch)}()] if this is intended.", LogType.Warning, StatusCodes.WaitValidation);

            return base.ValidateWaitRequest();
        }

        internal void LoadExpressions()
        {
            CurrentWorkflow = (WorkflowContainer)WorkflowInstance.StateObject;

            if (Template == null) return;
            Template.LoadUnmappedProps();
            MatchExpression = Template.MatchExpression;
            AfterMatchAction = Template.AfterMatchAction;
            CancelMethodAction = Template.CancelMethodAction;
        }

        public override void CopyCommonIds(WaitEntity oldWait)
        {
            base.CopyCommonIds(oldWait);
            if (oldWait is MethodWaitEntity mw)
            {
                TemplateId = mw.TemplateId;
                MethodToWaitId = mw.MethodToWaitId;
            }
        }


    }
}