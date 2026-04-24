using MessagePack;
using MessagePack.Resolvers;
using System.Dynamic;

namespace Workflows.Handler.Helpers
{
    internal static class ExpandoExtensions
    {
        internal static T ToObject<T>(this ExpandoObject _this)
        {
            var blob = MessagePackSerializer.Serialize(_this, ContractlessStandardResolver.Options);
            return MessagePackSerializer.Deserialize<T>(blob, ContractlessStandardResolver.Options);
        }

        internal static object ToObject(this ExpandoObject _this, Type type)
        {
            var blob = MessagePackSerializer.Serialize(_this, ContractlessStandardResolver.Options);
            return MessagePackSerializer.Deserialize(type, blob, ContractlessStandardResolver.Options);
        }

        internal static ExpandoObject ToExpando(this object _this)
        {
            var blob = MessagePackSerializer.Serialize(_this, ContractlessStandardResolver.Options);
            return MessagePackSerializer.Deserialize<ExpandoObject>(blob, ContractlessStandardResolver.Options);
        }
    }
}