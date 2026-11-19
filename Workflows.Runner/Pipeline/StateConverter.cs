using System;

namespace Workflows.Runner.Pipeline
{
    internal static class StateConverter
    {
        public static object ConvertState(object state, Type targetType)
        {
            if (state == null) return null;
            if (targetType == null) return state;
            
            var sourceType = state.GetType();
            if (targetType.IsAssignableFrom(sourceType)) return state;

            if (state is Newtonsoft.Json.Linq.JToken jToken)
            {
                return jToken.ToObject(targetType);
            }

            if (state is System.Collections.IDictionary dict && !typeof(System.Collections.IDictionary).IsAssignableFrom(targetType))
            {
                return Newtonsoft.Json.Linq.JObject.FromObject(dict).ToObject(targetType);
            }

            // Handle numeric conversions specifically
            if (IsNumericType(sourceType) && IsNumericType(targetType))
            {
                try
                {
                    return Convert.ChangeType(state, targetType);
                }
                catch
                {
                    // Fallback to original
                }
            }

            return state;
        }

        private static bool IsNumericType(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }
    }
}
