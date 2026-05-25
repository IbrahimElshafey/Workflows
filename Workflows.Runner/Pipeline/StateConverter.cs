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
