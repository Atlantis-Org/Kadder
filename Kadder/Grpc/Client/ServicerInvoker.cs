using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Grpc.Core;
using Kadder.Streaming;
using Kadder.Utils;

namespace Kadder.Grpc.Client
{
    public class ServicerInvoker
    {
        private readonly IBinarySerializer _serializer;
        private readonly ConcurrentDictionary<string, IMethod> _methods;

        public ServicerInvoker(IBinarySerializer serializer)
        {
            _serializer = serializer;
            _methods = new ConcurrentDictionary<string, IMethod>();
        }

        public async Task<TResponse> RpcAsync<TRequest, TResponse>(TRequest request, string service, string methodName) where TRequest : class where TResponse : class
        {
            var client = getClient(service);
            var channelInfo = client.GetChannel();
            var invoker = channelInfo.Channel.CreateCallInvoker();
            var method = GetMethod<TRequest, TResponse>(service, methodName, MethodType.Unary);

            var result = invoker.AsyncUnaryCall(method, channelInfo.Options.Address, new CallOptions(), request);
            return await result.ResponseAsync;
        }

        public Task<TResponse> ClientStreamAsync<TRequest, TResponse>(IAsyncRequestStream<TRequest> request, string service, string methodName) where TRequest : class where TResponse : class
        {
            var client = getClient(service);
            var channelInfo = client.GetChannel();
            var invoker = channelInfo.Channel.CreateCallInvoker();
            var method = GetMethod<TRequest, TResponse>(service, methodName, MethodType.ClientStreaming);

            var result = invoker.AsyncClientStreamingCall(method, channelInfo.Options.Address, new CallOptions());
            var requestStream = (AsyncRequestStream<TRequest>)request;
            requestStream.StreamWriter = result.RequestStream;
            return result.ResponseAsync;
        }

        public Task ServerStreamAsync<TRequest, TResponse>(TRequest request, IAsyncResponseStream<TResponse> response, string service, string methodName) where TRequest : class where TResponse : class
        {
            var client = getClient(service);
            var channelInfo = client.GetChannel();
            var invoker = channelInfo.Channel.CreateCallInvoker();
            var method = GetMethod<TRequest, TResponse>(service, methodName, MethodType.ClientStreaming);

            var result = invoker.AsyncServerStreamingCall(method, channelInfo.Options.Address, new CallOptions(), request);
            var responseStream = (AsyncResponseStream<TResponse>)response;
            responseStream.StreamReader = result.ResponseStream;
            return result.ResponseHeadersAsync;
        }

        public Task DuplexStreamAsync<TRequest, TResponse>(IAsyncRequestStream<TRequest> request, IAsyncResponseStream<TResponse> response, string service, string methodName) where TRequest : class where TResponse : class
        {
            var client = getClient(service);
            var channelInfo = client.GetChannel();
            var invoker = channelInfo.Channel.CreateCallInvoker();
            var method = GetMethod<TRequest, TResponse>(service, methodName, MethodType.ClientStreaming);

            var result = invoker.AsyncDuplexStreamingCall(method, channelInfo.Options.Address, new CallOptions());
            var requestStream = (AsyncRequestStream<TRequest>)request;
            var responseStream = (AsyncResponseStream<TResponse>)response;
            requestStream.StreamWriter = result.RequestStream;
            responseStream.StreamReader = result.ResponseStream;
            return result.ResponseHeadersAsync;
        }

        private GrpcClient getClient(string service)
        {
            if (!GrpcClient.ClientDict.TryGetValue(service, out GrpcClient client))
                throw new KeyNotFoundException($"Cannot found client! Servicer({service})");
            return client;
        }

        private Method<TRequest, TResponse> GetMethod<TRequest, TResponse>(string service, string methodName, MethodType methodType)
        {
            var key = $"{service}{methodName}";
            if (_methods.TryGetValue(key, out IMethod method))
                return (Method<TRequest, TResponse>)method;

            var requestMarshaller = new Marshaller<TRequest>(_serializer.Serialize<TRequest>, _serializer.Deserialize<TRequest>);
            var responseMarshaller = new Marshaller<TResponse>(_serializer.Serialize<TResponse>, _serializer.Deserialize<TResponse>);
            var newMethod = new Method<TRequest, TResponse>(methodType, service, methodName, requestMarshaller, responseMarshaller);
            _methods.TryAdd(key, newMethod);
            return newMethod;
        }
    }
}
