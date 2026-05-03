using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Workflows.Abstraction.Helpers;

namespace Workflows.Runner.Helpers
{
    public class PrivateDataResolver : DefaultContractResolver
    {
        static Helpers.PrivateDataResolver contractResolver = new Helpers.PrivateDataResolver();
        internal static JsonSerializerSettings Settings { get; } =
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                ContractResolver = contractResolver
            };

        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            var props = type
               .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
               .Where(member =>
                        member is FieldInfo &&
                        member.MemberType.CanConvertToSimpleString() &&
                        !member.Name.StartsWith("<>") &&//not a clsoure or compiler generated field
                        //!member.Name.EndsWith("__BackingField") &&//Backing Field
                        !member.Name.StartsWith("<GroupMatchFuncName>")//
                        )
               .Select(parameter => base.CreateProperty(parameter, memberSerialization))
               .ToList();
            props.ForEach(p => { p.Writable = true; p.Readable = true; });
            return props;
        }
    }
}
