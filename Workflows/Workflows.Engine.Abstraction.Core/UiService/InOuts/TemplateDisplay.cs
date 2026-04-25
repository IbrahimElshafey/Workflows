using System.Collections.Generic;
using Workflows.Handler.InOuts.Entities;
using System;
using System.Linq;
using Workflows.Engine.Abstraction.Core.Abstraction.Serialization;
namespace Workflows.Handler.UiService.InOuts
{
    public class TemplateDisplay
    {
        private static ExpressionSerializer _serializerInstance;

        /// <summary>
        /// Sets the ExpressionSerializer implementation to use
        /// </summary>
        public static void SetExpressionSerializer(ExpressionSerializer serializer)
        {
            _serializerInstance = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public string MatchExpression { get; }
        public string MandatoryPartExpression { get; }

        public TemplateDisplay(WaitTemplate waitTemplate) :
            this(waitTemplate.MatchExpressionValue, waitTemplate.InstanceMandatoryPartExpressionValue)
        {
        }

        public TemplateDisplay(string matchExpressionValue, List<string> callMandatoryPartPaths)
        {
            if (_serializerInstance == null)
                throw new InvalidOperationException("ExpressionSerializer not configured. Call SetExpressionSerializer first.");

            MatchExpression = GetMatch(matchExpressionValue);
            //MandatoryPartExpression = List<string> callMandatoryPartPaths joined as string
            if (callMandatoryPartPaths?.Any() == true)
            {
                MandatoryPartExpression = string.Join(',', callMandatoryPartPaths);
            }
        }

        public TemplateDisplay(string matchExpressionValue, string instanceMandatoryPartExpressionValue)
        {
            if (_serializerInstance == null)
                throw new InvalidOperationException("ExpressionSerializer not configured. Call SetExpressionSerializer first.");

            MatchExpression = GetMatch(matchExpressionValue);
            MandatoryPartExpression = GetMandatoryParts(instanceMandatoryPartExpressionValue);
        }

        string GetMatch(string matchExpressionValue)
        {
            if (matchExpressionValue == null) return string.Empty;
            var result = _serializerInstance.Deserialize(matchExpressionValue).ToString();
            result = result.Split("=>")[1];
            return result;
        }

        string GetMandatoryParts(string instanceMandatoryPartExpressionValue)
        {
            if (instanceMandatoryPartExpressionValue == null) return string.Empty;
            var result = _serializerInstance.Deserialize(instanceMandatoryPartExpressionValue).ToString();
            result = result.Replace("new object[]", "");
            result = result.Split("=>")[1];
            result = result.Replace("(object)", "");
            return result;
        }
    }
}
