using Workflows.Handler.Helpers;
using System.Linq.Expressions;
using static System.Linq.Expressions.Expression;

namespace Workflows.Handler.Expressions;

public class SetDataExpressionWriter : ExpressionVisitor
{
    private readonly ParameterExpression _workflowInstanceArg;
    private readonly List<Expression> _setValuesExpressions = new();
    public LambdaExpression SetDataExpression { get; protected set; }

    public SetDataExpressionWriter(LambdaExpression setDataExpression, Type funcType)
    {
        if (setDataExpression == null)
            return;
        if (setDataExpression?.Parameters.Count == 3)
        {
            SetDataExpression = setDataExpression;
            return;
        }

        _workflowInstanceArg = Parameter(funcType, "workflowInstance");

        var updatedBoy = (LambdaExpression)Visit(setDataExpression);
        for (var i = 0; i < _setValuesExpressions.Count; i++)
        {
            var setValue = _setValuesExpressions[i];
            _setValuesExpressions[i] = SwapAssignParts(setValue);
        }

        var workflowType = typeof(Action<,,>)
            .MakeGenericType(
                updatedBoy.Parameters[0].Type,
                updatedBoy.Parameters[1].Type,
                funcType);
        _setValuesExpressions.Add(Empty());
        var block = Block(_setValuesExpressions);
        SetDataExpression = Lambda(
            workflowType,
            block,
            updatedBoy.Parameters[0],
            updatedBoy.Parameters[1],
            _workflowInstanceArg);
    }


    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType == ExpressionType.Equal)
        {
            var assignVariable = Assign(node.Left, node.Right);
            _setValuesExpressions.Add(assignVariable);
        }

        return base.VisitBinary(node);
    }

    protected Expression SwapAssignParts(Expression node)
    {
        if (node is BinaryExpression binaryExpression)
        {
            if (binaryExpression.Left is MemberExpression dataMemeber)
            {
                var dataAccess = dataMemeber.GetDataParameterAccess(_workflowInstanceArg);
                if (dataAccess.IsWorkflowData)
                    return Assign(dataAccess.NewExpression, binaryExpression.Right);
            }

            if (binaryExpression.Right is MemberExpression rightDataMemeber)
            {
                var dataAccess = rightDataMemeber.GetDataParameterAccess(_workflowInstanceArg);
                if (dataAccess.IsWorkflowData)
                    return Assign(dataAccess.NewExpression, binaryExpression.Left);
            }
        }

        return node;
    }
}