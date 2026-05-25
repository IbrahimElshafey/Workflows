using Nuqleon.Json.Serialization;
using System;
using System.Linq.Expressions;
using System.Linq.Expressions.Bonsai.Serialization;
using Json = Nuqleon.Json.Expressions;
namespace Workflows.Shared.Serialization
{

    internal sealed class ExpressionSerializer : BonsaiExpressionSerializer, Abstraction.Helpers.IExpressionSerializer
    {
        protected override Func<object, Json.Expression> GetConstantSerializer(Type type)
        {
            if (typeof(Type).IsAssignableFrom(type))
            {
                return o => Json.Expression.String(((Type)o).AssemblyQualifiedName);
            }

            // REVIEW: Nuqleon.Json has an odd asymmetry in Serialize and Deserialize signatures,
            //         due to the inability to overload by return type. However, it seems odd we
            //         have to go serialize string and subsequently parse into Expression.
            try
            {
                return o => Json.Expression.Parse(new JsonSerializer(type).Serialize(o), ensureTopLevelObjectOrArray: false);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return null;
            }
        }

        protected override Func<Json.Expression, object> GetConstantDeserializer(Type type)
        {
            if (typeof(Type).IsAssignableFrom(type))
            {
                return json => Type.GetType(((Json.ConstantExpression)json).Value.ToString());
            }

            return json => new JsonSerializer(type).Deserialize(json);
        }

        public object Serialize(LambdaExpression expression)
        {
            if (expression == null) return null;
            var slim = Lift(expression);
            return base.Serialize(slim);
        }

        public LambdaExpression Deserialize(object serializedExpression)
        {
            if (serializedExpression == null) return null;
            if (serializedExpression is string json)
            {
                var slim = base.Deserialize(json);
                return (LambdaExpression)Reduce(slim);
            }
            throw new ArgumentException("Serialized expression must be a string", nameof(serializedExpression));
        }
    }
}