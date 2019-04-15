using System;
using System.IO;
using Atlantis.Grpc.Logging;
using Newtonsoft.Json;
using ProtoBuf;

namespace Atlantis.Grpc.Utilies
{
    public class ProtobufBinarySerializer : IBinarySerializer
    {

        public ProtobufBinarySerializer()
        {
        }

        public ILogger Log => ObjectContainer.Resolve<ILoggerFactory>().Create<ProtobufBinarySerializer>();

        public T Deserialize<T>(byte[] data)
        {
            try
            {
                using (var memoryStream = new MemoryStream(data))
                {
                    return Serializer.Deserialize<T>(memoryStream);
                }
            }
            catch(Exception ex)
            {
                Log.Error($"Serialize failed! MsgName[{typeof(T).FullName}] Data[{JsonConvert.SerializeObject(data)}]",ex);
                throw ex;
            }
}

        public byte[] Serialize<T>(T obj)
        {
            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    Serializer.Serialize(memoryStream, obj);
                    return memoryStream.ToArray();
                }
            }
            catch(Exception ex)
            {
                Log.Error($"Serialize failed! MsgName[{typeof(T).FullName}] Data[{JsonConvert.SerializeObject(obj)}]",ex);
                throw ex;
            }
        }
    }

}
