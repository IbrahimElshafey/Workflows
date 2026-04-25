using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Xml;
using Workflows.Handler.Abstraction.Serialization;
using Workflows.Handler.Helpers;
using Workflows.Handler.InOuts.Entities.EntityBehaviour;
namespace Workflows.Handler.InOuts.Entities
{
    public class SignalEntity : IEntity<long>, IBeforeSaveEntity
    {
        public long Id { get; internal set; }
        /// <summary>
        /// MethodInfo of the method that was called and also some RF attribtes like
        ///  CanPublishFromExternal, IsLocalOnly, IsActive
        /// </summary>
        public MethodData MethodData { get; internal set; }
        /// <summary>
        /// Same as MethodData but in binary format
        /// </summary>
        //Todo: Replace with MethodDataId because it's large and is same for all calls form the same method
        public byte[] MethodDataValue { get; internal set; }
        public InputOutput Data { get; internal set; } = new InputOutput();
        public byte[] DataValue { get; internal set; }
        public int? ServiceId { get; internal set; }

        public DateTime Created { get; internal set; }
        public string MethodUrn { get; internal set; }


        internal string GetMandatoryPart(List<string> callMandatoryPartPaths)
        {
            if (callMandatoryPartPaths?.Any() != true) return null;

            var raw = Dependencies.BinarySerializer.Deserialize<ExpandoObject>(DataValue);

            var values = callMandatoryPartPaths.Select(path =>
            {
                return raw.Get(path)?.ToString();
            });

            if (Dependencies.JsonSerializer == null)
                throw new InvalidOperationException("JSON serializer not configured. Call SetJsonSerializer first.");

            return Dependencies.JsonSerializer.Serialize(values);
        }

        public void BeforeSave()
        {
            DataValue = Dependencies.BinarySerializer.Serialize(Data);
            MethodDataValue = Dependencies.BinarySerializer.Serialize(MethodData);
            MethodUrn = MethodData?.MethodUrn;
        }

        public void LoadUnmappedProps(MethodInfo methodInfo = null)
        {
            if (methodInfo == null)
                Data = Dependencies.BinarySerializer.Deserialize<InputOutput>(DataValue);
            else
            {
                var inputType = methodInfo.GetParameters()[0].ParameterType;
                var outputType = methodInfo.IsAsyncMethod() ?
                    methodInfo.ReturnType.GetGenericArguments()[0] :
                    methodInfo.ReturnType;
                Data = GetMethodData(inputType, outputType, DataValue);
            }
            MethodData = Dependencies.BinarySerializer.Deserialize<MethodData>(MethodDataValue);
        }

        private static InputOutput GetMethodData(Type inputType, Type outputType, byte[] dataBytes)
        {
            var genericInputOutPut = typeof(GInputOutput<,>).MakeGenericType(inputType, outputType);
            dynamic data = Dependencies.BinarySerializer.Deserialize(dataBytes, genericInputOutPut);
            return InputOutput.FromGeneric(data);
        }

        public override string ToString()
        {
            return Dependencies.JsonSerializer.Serialize(this);
        }
    }
}