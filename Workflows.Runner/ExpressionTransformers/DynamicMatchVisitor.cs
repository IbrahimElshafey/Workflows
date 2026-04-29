using System;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using Workflows.Abstraction.Helpers;
using static System.Linq.Expressions.Expression;

namespace Workflows.Runner.ExpressionTransformers
{
    internal class DynamicMatchVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _signalData;
        private readonly ParameterExpression _workflowInstance;
        private bool _stop;
        public Expression<Func<ExpandoObject, ExpandoObject, bool>> Result { get; internal set; }

        public DynamicMatchVisitor(LambdaExpression matchExpression)
        {
            _signalData = Parameter(typeof(ExpandoObject), "signalData");
            _workflowInstance = Parameter(typeof(ExpandoObject), "workflowInstance");
            var result = Visit(matchExpression.Body);
            if (!_stop && result != null)
                Result = Lambda<Func<ExpandoObject, ExpandoObject, bool>>(result, _signalData, _workflowInstance);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            var parameter = GetParamter(node.ToString());
            if (CanConvertToString(node.Type))
            {
                var getValueMi = typeof(ExpandoExtensions).GetMethods().First(x => x.Name == "Get" && x.IsGenericMethod).MakeGenericMethod(node.Type);
                return Call(
                    getValueMi,
                    parameter.ParameterExpression,
                    parameter.Path
                );
            }

            _stop = true;
            return base.VisitMember(node);
        }

        private (ParameterExpression ParameterExpression, ConstantExpression Path) GetParamter(string path)
        {
            if (path.StartsWith("signalData."))
                return (_signalData, Constant(path));
            if (path.StartsWith("workflowInstance."))
                return (_workflowInstance, Constant(path.Substring(17)));
            throw new Exception($"Can't access to [{path}]");
        }

        public override Expression Visit(Expression node)
        {
            if (_stop) return node;
            return base.Visit(node);
        }

        private bool CanConvertToString(Type type)
        {
            return type.IsConstantType() || type == typeof(DateTime) || type == typeof(Guid) || type.IsEnum;
        }
    }
}