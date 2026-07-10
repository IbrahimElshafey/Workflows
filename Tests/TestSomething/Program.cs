using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Workflows.Abstraction.DTOs.Waits;
using Workflows.Abstraction.Enums;
using Workflows.Primitives;

class Program
{
    private static readonly JsonSerializerSettings PolymorphicSerializerSettings = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.All,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented,
        ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        ContractResolver = new PrivateSetterContractResolver(),
        PreserveReferencesHandling = PreserveReferencesHandling.Objects,
        Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() }
    };

    private class PrivateSetterContractResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
    {
        protected override Newtonsoft.Json.Serialization.JsonProperty CreateProperty(
            System.Reflection.MemberInfo member, 
            Newtonsoft.Json.MemberSerialization memberSerialization)
        {
            var prop = base.CreateProperty(member, memberSerialization);
            if (!prop.Writable)
            {
                var property = member as System.Reflection.PropertyInfo;
                if (property != null)
                {
                    var hasPrivateSetter = property.GetSetMethod(true) != null;
                    prop.Writable = hasPrivateSetter;
                }
            }
            return prop;
        }
    }

    static void Main(string[] args)
    {
        var signal = new SignalWaitDto
        {
            Id = Guid.NewGuid().ToString(),
            SignalIdentifier = "TestSignal",
            WaitName = "Child Signal Wait",
            Status = WaitStatus.Waiting,
            WaitType = WaitType.SignalWait
        };

        var group = new GroupWaitDto
        {
            Id = Guid.NewGuid().ToString(),
            WaitName = "Parent Group Wait",
            Status = WaitStatus.Waiting,
            WaitType = WaitType.GroupWaitAll,
            ChildWaits = new List<WaitInfrastructureDto> { signal }
        };

        signal.ParentWaitId = group.Id;

        var list = new List<WaitInfrastructureDto> { group };

        var serialized = JsonConvert.SerializeObject(list, PolymorphicSerializerSettings);
        Console.WriteLine("--- Serialized JSON ---");
        Console.WriteLine(serialized);

        var deserialized = JsonConvert.DeserializeObject<List<WaitInfrastructureDto>>(serialized, PolymorphicSerializerSettings);
        Console.WriteLine("\n--- Deserialized tree check ---");
        if (deserialized == null)
        {
            Console.WriteLine("Deserialized list is null!");
            return;
        }

        Console.WriteLine($"Root count: {deserialized.Count}");
        var deserializedGroup = deserialized[0] as GroupWaitDto;
        if (deserializedGroup != null)
        {
            Console.WriteLine($"Group Wait Name: {deserializedGroup.WaitName}");
            Console.WriteLine($"Group ChildWaits count: {deserializedGroup.ChildWaits?.Count}");
            if (deserializedGroup.ChildWaits != null && deserializedGroup.ChildWaits.Count > 0)
            {
                var child = deserializedGroup.ChildWaits[0];
                Console.WriteLine($"Child Wait Name: {child.WaitName}");
                Console.WriteLine($"Child ParentWaitId: {child.ParentWaitId}");
            }
        }
        else
        {
            Console.WriteLine("First item is not a GroupWaitDto!");
        }
    }
}
