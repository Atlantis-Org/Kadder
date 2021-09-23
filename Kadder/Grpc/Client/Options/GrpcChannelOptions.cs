using Grpc.Core;

namespace Kadder.Grpc.Client.Options
{
    public class GrpcChannelOptions
    {
        public string Address { get; set; }

        public ChannelCredentials Credentials { get; set; }
    }
}
