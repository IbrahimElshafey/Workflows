using Workflows.Handler.BaseUse;
using Workflows.Handler.Core.Abstraction;
using Workflows.Handler.Helpers;
using Workflows.Handler.InOuts.Entities;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;

namespace Workflows.Handler.Core;

public class WorkflowRunner : IWorkflowRunner
{
    private IAsyncEnumerator<Wait> _workflowRunner;
    private readonly WaitEntity _oldMatchedWait;

    public WorkflowRunner(WaitEntity oldMatchedWait)
    {
        var workflowRunnerType = oldMatchedWait.CurrentWorkflow.GetType()
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.SuppressChangeType)
            .FirstOrDefault(type =>
            type.Name.StartsWith($"<{oldMatchedWait.RequestedByWorkflow.MethodName}>") &&
            typeof(IAsyncEnumerable<Wait>).IsAssignableFrom(type));

        if (workflowRunnerType == null)
            throw new Exception(
                $"Can't find resumable workflow [{oldMatchedWait?.RequestedByWorkflow?.MethodName}] " +
                $"in class [{oldMatchedWait?.CurrentWorkflow?.GetType().FullName}].");

        _oldMatchedWait = oldMatchedWait;
        ResumeLocals(oldMatchedWait.Locals, workflowRunnerType);
        CreateRunnerIfNull(workflowRunnerType);
        SetRunnerCallerRfCalss(oldMatchedWait.CurrentWorkflow);
        ResumeClosure(oldMatchedWait.ClosureData);
        State = oldMatchedWait.StateAfterWait;
        if (_workflowRunner == null)
            throw new Exception($"Resumable workflow ({oldMatchedWait?.RequestedByWorkflow.MethodName}) not exist in code");
    }

    public WorkflowRunner(
        WorkflowContainer classInstance,
        MethodInfo resumableWorkflow)
    {
        var workflowRunnerType = classInstance.GetType()
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.SuppressChangeType)
            .FirstOrDefault(x => x.Name.StartsWith($"<{resumableWorkflow.Name}>"));
        CreateRunnerIfNull(workflowRunnerType);
        SetRunnerCallerRfCalss(classInstance);
        State = int.MinValue;
        if (_workflowRunner == null)
            throw new Exception($"Can't initiate runner.");
    }

    public WorkflowRunner(IAsyncEnumerator<Wait> runner)
    {
        _workflowRunner = runner;
        if (_workflowRunner == null)
            throw new Exception($"Can't initiate runner.");
    }

    public Wait Current => _workflowRunner.Current;
    public WaitEntity CurrentWaitEntity => _workflowRunner.Current.WaitEntity;

    public ValueTask DisposeAsync()
    {
        return _workflowRunner.DisposeAsync();
    }

    public async ValueTask<bool> MoveNextAsync()
    {
        var hasNext = await _workflowRunner.MoveNextAsync();
        if (hasNext)
        {
            CurrentWaitEntity.StateAfterWait = State;
            //set locals for the new incoming wait
            SetLocalsForIncomingWait();

            //set closure
            SetClosureForIncomingWait();
        }
        return hasNext;
    }

    private void SetClosureForIncomingWait()
    {
        var closureContinuation =
                        _oldMatchedWait != null &&
                        _oldMatchedWait.CallerName == CurrentWaitEntity.CallerName &&
                        _oldMatchedWait.ClosureData != null;

        var closureFields = GetClosureFields();
        var activeClosure = closureFields?.
             Select(x => x.GetValue(_workflowRunner)).
             LastOrDefault(x => x != null);
        //we may have two closures like <>c__DisplayClass0_0,<>c__DisplayClass0_1
        //but this code is based on one closure will be active and not equal to one

        if (activeClosure == null) return;
        var callerName = CurrentWaitEntity.CallerName;

        if (closureContinuation)
        {
            _oldMatchedWait.ClosureData.Value = activeClosure;
            CurrentWaitEntity.ClosureData = _oldMatchedWait.ClosureData;
        }

        CurrentWaitEntity.ActionOnChildrenTree(wait =>
        {
            if (wait.CallerName == callerName)
                wait.SetClosureObject(activeClosure);
        });
    }

    private void SetLocalsForIncomingWait()
    {
        var localsContinuation =
            _oldMatchedWait != null &&
            _oldMatchedWait.Locals != null;
        if (localsContinuation)
        {
            _oldMatchedWait.Locals.Value = _workflowRunner;
            CurrentWaitEntity.Locals = _oldMatchedWait.Locals;
        }
        else if (RunnerHasValue())
        {
            CurrentWaitEntity.Locals = new PrivateData
            {
                Value = _workflowRunner,
                WorkflowInstanceId = _oldMatchedWait?.WorkflowInstanceId
            };
        }
    }

    private bool RunnerHasValue()
    {
        var json = Dependencies.JsonSerializer.Serialize(_workflowRunner);
        return json != "{}";
    }

    private void CreateRunnerIfNull(Type workflowRunnerType)
    {
        if (_workflowRunner != null) return;

        const string error = "Can't create a workflow runner.";
        if (workflowRunnerType == null)
            throw new Exception(error);

        var ctor = workflowRunnerType.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.CreateInstance,
            null,
            new[] { typeof(int) },
            null);

        if (ctor == null)
            throw new Exception(error);

        _workflowRunner = (IAsyncEnumerator<Wait>)ctor.Invoke(new object[] { -2 });


        if (_workflowRunner == null)
            throw new Exception(error);
    }

    private void SetRunnerCallerRfCalss(WorkflowContainer workflowClassInstance)
    {
        //set caller class for current workflow runner
        var thisField = _workflowRunner
            .GetType()
            .GetFields()
            .FirstOrDefault(x => x.Name.EndsWith(Constants.CompilerCallerSuffix) && x.FieldType == workflowClassInstance.GetType());
        thisField?.SetValue(_workflowRunner, workflowClassInstance);
    }

    private List<FieldInfo> GetClosureFields() => _workflowRunner.
            GetType().
            GetFields(BindingFlags.Instance | BindingFlags.NonPublic).
            Where(x => x.FieldType.Name.StartsWith(Constants.CompilerClosurePrefix)).
            ToList();
    private void ResumeLocals(PrivateData oldLocals, Type workflowRunnerType)
    {
        //use the old wait runner
        if (oldLocals?.Value.GetType() == workflowRunnerType)
        {
            _workflowRunner = (IAsyncEnumerator<Wait>)oldLocals.Value;
            return;
        }
        //if (oldLocals != null && oldLocals.Value is JObject jobject)
        //{
        //    _workflowRunner = (IAsyncEnumerator<Wait>)jobject.ToObject(workflowRunnerType);
        //}
    }

    private void ResumeClosure(PrivateData closureData)
    {
        var closureFields = GetClosureFields();
        if (closureFields is null || closureFields.Count == 0) return;

        var closure = closureData?.Value;
        if (closure is null)
        {
            closureFields.ForEach(
                field => field.SetValue(_workflowRunner, Activator.CreateInstance(field.FieldType)));
            return;
        }

        var activeField = closureFields.FirstOrDefault(x => x.FieldType.Name == closureData.TypeName);
        if (activeField is null) return;
        //if (closure is JObject jobject)
        //{
        //    var closureObject = jobject.ToObject(activeField.FieldType);
        //    activeField.SetValue(_workflowRunner, closureObject ?? Activator.CreateInstance(activeField.FieldType));
        //}
        //else if (closure.GetType() == activeField.FieldType)
        //    activeField.SetValue(_workflowRunner, closure);
    }

    public int State
    {
        get
        {
            if (_workflowRunner == null) return int.MinValue;
            var stateField = _workflowRunner?.GetType().GetField(Constants.CompilerStateFieldName);
            return stateField != null ? (int)stateField.GetValue(_workflowRunner) : int.MinValue;
        }
        set
        {
            if (_workflowRunner == null) return;
            var stateField = _workflowRunner?.GetType().GetField(Constants.CompilerStateFieldName);
            stateField?.SetValue(_workflowRunner, value);
        }
    }
}