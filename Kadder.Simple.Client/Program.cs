﻿using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Kadder;
using Kadder.Simple.Client;
using Kadder.Simple.Server;
using Kadder.Utilies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProtoBuf;
using Kadder.Grpc.Client;
using Kadder.Streaming;

namespace Atlantis.Grpc.Simple.Client
{
    class Program
    {
        static void Main(string[] args)
        {
            IServiceCollection servicers = new ServiceCollection();
            servicers.UseGrpcClient(builder =>
            {
                builder.GrpcServerOptions = new GrpcServerOptions() { PackageName = typeof(IAnimalMessageServicer).Namespace };
                builder.Assemblies.Add(typeof(IAnimalMessageServicer).Assembly);
            });

            // var options = new GrpcClientOptions()
            // {
            //     Host = "127.0.0.1",
            //     Port = 13002,
            //     NamespaceName = "Atlantis.Simple",
            //     ServiceName = "AtlantisService",
            //     ScanAssemblies = new string[]
            //     {
            //         typeof(IPersonMessageServicer).Assembly.FullName
            //     }
            // };

            // IServiceCollection services = new ServiceCollection();
            // services.AddLogging(b => b.AddConsole());
            // services.AddTransient(typeof(ILogger<>), typeof(NullLogger<>));
            // services.AddKadderGrpcClient(builder =>
            // {
            //     builder.RegClient(options);
            //     //builder.RegShareInterceptor<Kadder.Simple.Client.LoggerInterceptor>();
            // });

            // var provider = services.BuildServiceProvider();
            // provider.ApplyKadderGrpcClient();
            // var log = provider.GetService<ILogger<GrpcClient>>();
            // log.LogInformation("dd");

            // var animalServicer=new Kadder.Simple.Client.AnimalMessageServicer();
            // var request=StreamMessage.CreateRequest<HelloMessage>();
            // Console.WriteLine(((AsyncRequestStream<HelloMessage>)request).Name);
            // change(request);
            // Console.WriteLine(((AsyncRequestStream<HelloMessage>)request).Name);
            // var res=animalServicer.ClientAsync(request);


            // var s=animalServicer.ClientStreamAsync<HelloMessage,HelloMessageResult>(animalServicer.ClientAsync);

            // TestInterceptor(provider);

            // TestNumber(provider);

            // Console.ReadLine();

            // var channel = new Channel("127.0.0.1", 3002, ChannelCredentials.Insecure);

            // channel.ConnectAsync().Wait();

            // AtlantisServiceClient client=new AtlantisServiceClient(channel);
            // 
            // var result= client.Hello(message);

            // // var serailizer=new ProtobufBinarySerializer();
            // // var s=serailizer.Serialize(message);

            // // foreach(var b in s)
            // // {
            // //     Console.Write($" {b}");
            // }
        }

        public static IAsyncRequestStream<TRequest> change<TRequest>(IAsyncRequestStream<TRequest> request) where TRequest : class
        {
            var grpc = new AsyncRequestStream<TRequest>();
            grpc.Name = "bbbb";
            request = grpc;
            return request;
        }

        // static void TestAI(ServiceProvider provider)
        // {
        //     var service = provider.GetService<IOcrService>();
        //     var request = new ImageUrlOcrRequest()
        //     {
        //         Url = "https://resapi.neobai.com/previews/1195351096063823872.jpg"
        //     };
        //     var response = service.GetImageTagsByUrlAsync(request);
        // }

        static void TestNumber(ServiceProvider provider)
        {
            var i = 0;
            var servicer = provider.GetService<INumberMessageServicer>();
            while (true)
            {
                servicer.PrintAsync(new NumberMessage { Number = i++ }).Wait();
            }
        }

        static void TestParallel(ServiceProvider provider)
        {
            try
            {
                var servicer = provider.GetService<IPersonMessageServicer>();
                var message = new HelloMessage() { Name = "DotNet" };
                var stopwatch = new Stopwatch();
                var resuslt = servicer.HelloAsync();
                stopwatch.Start();
                System.Threading.Tasks.Parallel.For(0, 10000, i =>
                {
                    var result = servicer.HelloAsync();
                    Console.WriteLine(result);
                });
                stopwatch.Stop();
                Console.WriteLine(stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        static void TestInterceptor(ServiceProvider provider)
        {
            var servicer = provider.GetService<IPersonMessageServicer>();
            var message = new HelloMessage() { Name = "test interceptor" };

            for (var i = 0; i < 10; i++)
            {
                Task.Run(() =>
                {
                    while (true)
                    {
                        try
                        {
                            var resuslt = servicer.HelloAsync().Result;
                            Console.WriteLine(resuslt.Result);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
                        }

                        // Console.WriteLine(resuslt.Result);
                        System.Threading.Thread.Sleep(1000);
                    }

                });
            }
        }

    }
}
