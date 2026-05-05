using Nuqleon.Json.Serialization;
using System;
using System.Linq.Expressions;
using System.Linq.Expressions.Bonsai.Serialization;
using System.Threading.Tasks;
using Json = Nuqleon.Json.Expressions;
namespace Workflows.Runner.ExpressionTransformers
{

    internal sealed class ExpressionSerializer : BonsaiExpressionSerializer, Workflows.Common.Abstraction.IExpressionSerializer
    {
        protected override Func<object, Json.Expression> GetConstantSerializer(Type type)
        {
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
            return json => new JsonSerializer(type).Deserialize(json);
        }

        public string Serialize(LambdaExpression expression)
        {
            throw new NotImplementedException();
        }

        public LambdaExpression Deserialize(string serializedExpression)
        {
            throw new NotImplementedException();
        }
    }
}