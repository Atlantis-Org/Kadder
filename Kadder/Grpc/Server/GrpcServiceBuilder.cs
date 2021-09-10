using GenAssembly;
using GenAssembly.Descripters;
using Grpc.Core;
using Kadder.CodeGeneration;
using Kadder.Messaging;
using Kadder.Utilies;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Meta;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Kadder
{
    public class GrpcServiceBuilder
    {
        private List<string> _messages = new List<string>();
        private Dictionary<string, string> _oldVersionGrpcMethods = new Dictionary<string, string>();

        public List<ClassDescripter> GenerateGrpcProxy(GrpcServerOptions options, CodeBuilder codeBuilder = null)
        {
            if (codeBuilder == null)
            {
                codeBuilder = CodeBuilder.Default;
            }
            var implInterfaceTypes = options.GetKServicers();
            var classDescripterList = new List<ClassDescripter>();
            var protoMessageCode = new StringBuilder();
            var protoServiceCode = new StringBuilder();
            foreach (Type service in implInterfaceTypes)
            {
                var bindServicesCode = new StringBuilder("return ServerServiceDefinition.CreateBuilder()\n");
                protoServiceCode.AppendLine($"service {service.Name} {{");
                var @class = this.GenerateGrpcService(service);
                var interfaces = service.GetInterfaces();
                foreach (var method in service.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (method.CustomAttributes.FirstOrDefault(p => p.AttributeType == typeof(NotGrpcMethodAttribute)) != null)
                    {
                        continue;
                    }
                    var parameters = RpcParameterInfo.Convert(method.GetParameters());
                    if (parameters.Count == 0)
                    {
                        var emptyMessageType = typeof(EmptyMessage);
                        parameters.Add(new RpcParameterInfo()
                        {
                            Name = emptyMessageType.Name,
                            ParameterType = emptyMessageType,
                            IsEmpty = true
                        });
                    }

                    @class = GenerateGrpcMethod(@class, method, parameters[0], interfaces);
                    GenerateGrpcCallCode(method, parameters[0], options.NamespaceName, codeBuilder, ref bindServicesCode);
                    GenerateGrpcCallCodeForOldVersion(method, parameters[0], options.NamespaceName, options.ServiceName, codeBuilder, ref bindServicesCode);
                    if (options.IsGeneralProtoFile)
                    {
                        GenerateProtoCode(method, parameters[0], ref protoServiceCode, ref protoMessageCode);
                    }
                }
                bindServicesCode.AppendLine(".Build();");
                protoServiceCode.AppendLine();
                protoServiceCode.AppendLine("}");
                @class.CreateMember(new MethodDescripter("BindServices", false).SetCode(bindServicesCode.ToString()).SetReturn("ServerServiceDefinition").SetAccess(AccessType.Public));
                codeBuilder.AddAssemblyRefence(Assembly.GetExecutingAssembly())
                    .AddAssemblyRefence(typeof(ServerServiceDefinition).Assembly)
                    .AddAssemblyRefence(typeof(ServerServiceDefinition).Assembly)
                    .AddAssemblyRefence(typeof(ServiceProviderServiceExtensions).Assembly)
                    .AddAssemblyRefence(typeof(Console).Assembly)
                    .AddAssemblyRefence(service.Assembly);
                codeBuilder.CreateClass(@class);
                classDescripterList.Add(@class);
            }
            if (options.IsGeneralProtoFile)
            {
                if (string.IsNullOrWhiteSpace(options.PackageName))
                {
                    options.PackageName = options.NamespaceName;
                }

                var stringBuilder = new StringBuilder();
                stringBuilder.AppendLine("syntax = \"proto3\";");
                stringBuilder.AppendLine($@"option csharp_namespace = ""{options.NamespaceName}"";");
                stringBuilder.AppendLine($"package {options.PackageName};\n");
                stringBuilder.Append(protoServiceCode);
                stringBuilder.AppendLine();
                var messageCode = protoMessageCode.ToString();
                if (messageCode.Contains(".bcl."))
                {
                    protoMessageCode.Replace(".bcl.", "");
                    protoMessageCode.AppendLine(Bcl.Proto);
                }
                stringBuilder.Append(protoMessageCode);
                var path = $"{Environment.CurrentDirectory}/{options.NamespaceName}.proto";
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.WriteAllText(path, stringBuilder.ToString());
                _messages = null;
            }
            return classDescripterList;
        }

        private ClassDescripter GenerateGrpcService(Type service)
        {
            var code =
                @"_binarySerializer = GrpcServerBuilder.ServiceProvider.GetService<IBinarySerializer>();
                 _messageServicer = GrpcServerBuilder.ServiceProvider.GetService<GrpcMessageServicer>();";
            var name = $"{service.Name}GrpcService";
            return new ClassDescripter(name, "Kadder")
                .SetAccess(AccessType.Public)
                .SetBaseType(typeof(IGrpcServices).Name)
                .AddUsing(
                    "using Grpc.Core;",
                    "using System.Threading.Tasks;",
                    "using Kadder;",
                    "using Kadder.Middlewares;",
                    "using Kadder.Utilies;",
                    "using Microsoft.Extensions.DependencyInjection;",
                    "using Kadder.Messaging;")
                .CreateFiled(
                    new FieldDescripter("_binarySerializer")
                    .SetAccess(AccessType.PrivateReadonly)
                    .SetType(typeof(IBinarySerializer)),
                    new FieldDescripter("_messageServicer")
                    .SetAccess(AccessType.PrivateReadonly)
                    .SetType(typeof(GrpcMessageServicer)))
                .CreateConstructor(new ConstructorDescripter(name).SetCode(code).SetAccess(AccessType.Public));
        }

        private ClassDescripter GenerateGrpcMethod(ClassDescripter @class, MethodInfo method, RpcParameterInfo parameter, Type[] baseInterfaces)
        {
            var className = method.DeclaringType.Name;
            var messageName = parameter.ParameterType.Name;
            var messageResultType = GrpcServiceBuilder.GetMethodReturn(method);
            var messageResultName = messageResultType.Name;
            var requestCode = "messageEnvelope.Message";
            var responseCode = "var result = ";
            var setResponseCode = "result";
            if (parameter.IsEmpty)
            {
                requestCode = string.Empty;
            }
            if (messageResultType.IsEmpty)
            {
                responseCode = string.Empty;
                setResponseCode = "new EmptyMessageResult()";
            }
            var code = $@"
            var envelope = new MessageEnvelope<{messageName}>();
            envelope.Message = request;  
            var grpcContext = new GrpcContext(envelope, context);
            Func<IMessageEnvelope, IServiceScope, Task<IMessageResultEnvelope>> handler = async (imsgEnvelope, scope) => 
            {{
                var messageEnvelope = (MessageEnvelope<{messageName}>) imsgEnvelope;
                {responseCode}await scope.ServiceProvider.GetService<{className}>().{method.Name}({requestCode});
                return new MessageResultEnvelope<{messageResultName}>() {{ MessageResult = {setResponseCode} }};
            }};
            var resultEnvelope = await _messageServicer.ProcessAsync(grpcContext, handler);
            return ((MessageResultEnvelope<{messageResultName}>)resultEnvelope).MessageResult;";
            return @class.CreateMember(
                new MethodDescripter(method.Name.Replace("Async", "") ?? "", true)
                .SetAccess(AccessType.Public)
                .AppendCode(code).SetReturn($"Task<{GrpcServiceBuilder.GetMethodReturn(method).Name}>")
                .SetParams(
                    new ParameterDescripter(parameter.ParameterType.Name, "request"),
                    new ParameterDescripter("ServerCallContext", "context")))
                .AddUsing($"using {parameter.ParameterType.Namespace};")
                .AddUsing($"using {method.DeclaringType.Namespace};")
                .AddUsing($"using {GrpcServiceBuilder.GetMethodReturn(method).Namespace};");
        }

        private void GenerateGrpcCallCode(MethodInfo method, RpcParameterInfo parameter, string namespaceName,
                                          CodeBuilder codeBuilder, ref StringBuilder bindServicesCode)
        {
            bindServicesCode.AppendLine($@"
                .AddMethod(new Method<{parameter.ParameterType.Name}, {GrpcServiceBuilder.GetMethodReturn(method).Name}>(
                    MethodType.Unary,
                    ""{namespaceName}.{method.DeclaringType.Name}"",
                    ""{method.Name.Replace("Async", "")}"",
                    new Marshaller<{parameter.ParameterType.Name}>(
                        _binarySerializer.Serialize,
                        _binarySerializer.Deserialize<{parameter.ParameterType.Name}>
                    ),
                    new Marshaller<{GrpcServiceBuilder.GetMethodReturn(method).Name}>(
                        _binarySerializer.Serialize,
                        _binarySerializer.Deserialize<{GrpcServiceBuilder.GetMethodReturn(method).Name}>)
                    ),
                    {method.Name.Replace("Async", "")})");

            codeBuilder
                .AddAssemblyRefence(parameter.ParameterType.Assembly)
                .AddAssemblyRefence(GrpcServiceBuilder.GetMethodReturn(method).Assembly);
        }

        /// <summary>
        /// Support version 0.0.6 before
        /// </summary>
        /// <param name="method"></param>
        /// <param name="parameter"></param>
        /// <param name="namespaceName"></param>
        /// <param name="codeBuilder"></param>
        /// <param name="bindServicesCode"></param>
        private void GenerateGrpcCallCodeForOldVersion(MethodInfo method, RpcParameterInfo parameter, string namespaceName,
            string serviceName, CodeBuilder codeBuilder, ref StringBuilder bindServicesCode)
        {
            var serviceDefName = $"{namespaceName}.{serviceName}";
            var methodDefName = method.Name.Replace("Async", "");
            var key = $"{serviceDefName}.{methodDefName}";
            if (_oldVersionGrpcMethods.ContainsKey(key))
            {
                return;
            }
            bindServicesCode.AppendLine($@"
                .AddMethod(new Method<{parameter.ParameterType.Name}, {GrpcServiceBuilder.GetMethodReturn(method).Name}>(
                    MethodType.Unary,
                    ""{serviceDefName}"",
                    ""{methodDefName}"",
                    new Marshaller<{parameter.ParameterType.Name}>(
                        _binarySerializer.Serialize,
                        _binarySerializer.Deserialize<{parameter.ParameterType.Name}>
                    ),
                    new Marshaller<{GrpcServiceBuilder.GetMethodReturn(method).Name}>(
                        _binarySerializer.Serialize,
                        _binarySerializer.Deserialize<{GrpcServiceBuilder.GetMethodReturn(method).Name}>)
                    ),
                    {method.Name.Replace("Async", "")})");
            _oldVersionGrpcMethods.Add(key, key);
        }

        private void GenerateProtoCode(MethodInfo method, RpcParameterInfo parameter, ref StringBuilder protoServiceCode, ref StringBuilder protoMessageCode)
        {
            var str = $"rpc {method.Name.Replace("Async", "")}({parameter.ParameterType.Name}) returns({GrpcServiceBuilder.GetMethodReturn(method).Name});";
            protoServiceCode.AppendLine();
            protoServiceCode.AppendLine(str);
            if (!_messages.Contains(parameter.ParameterType.Name))
            {
                protoMessageCode.AppendLine(GenerateProtoMessageCode(parameter.ParameterType));
                _messages.Add(parameter.ParameterType.Name);
            }
            if (_messages.Contains(GrpcServiceBuilder.GetMethodReturn(method).Name))
            {
                return;
            }
            protoMessageCode.AppendLine(GenerateProtoMessageCode(GrpcServiceBuilder.GetMethodReturn(method).ReturnType));
            _messages.Add(GrpcServiceBuilder.GetMethodReturn(method).Name);
        }

        private string GenerateProtoMessageCode(Type messageType)
        {
            if (this._messages.Contains(messageType.Name))
            {
                return string.Empty;
            }
            var p = GetProto(messageType);
            _messages.AddRange(p.Types);
            return p.Proto;
        }

        private static RpcMethodReturnType GetMethodReturn(MethodInfo method)
        {
            var genericTypes = method.ReturnType.GetGenericArguments();
            if (genericTypes.Length != 0)
            {
                return new RpcMethodReturnType(genericTypes[0]);
            }

            if (method.ReturnType.FullName == typeof(Task).FullName || method.ReturnType.FullName == typeof(void).FullName)
            {
                return new RpcMethodReturnType(typeof(EmptyMessageResult), true);
            }

            return new RpcMethodReturnType(method.ReturnType);
        }

        private static string GetRequestName(MethodInfo method)
        {
            var name = $"{method.DeclaringType.FullName}.{method.Name}";
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                return name;
            }

            foreach (var param in parameters)
            {
                name += $"-{param.ParameterType.Name}";
            }
            return name;
        }

        private (string Proto, List<string> Types) GetProto(Type type)
        {
            var p = RuntimeTypeModel.Default.GetSchema(type, ProtoSyntax.Proto3).Replace("\r", "");
            var arr = p.Split('\n');
            var proto = new StringBuilder();
            var types = new List<string>();
            var currentType = string.Empty;
            var isEnum = false;
            var isContent = false;
            for (var i = 0; i < arr.Length; i++)
            {
                var item = arr[i];
                if (item.StartsWith("syntax ") || item.StartsWith("package ") || item.StartsWith("import "))
                {
                    continue;
                }
                if (item.StartsWith("message"))
                {
                    currentType = item.Replace("message", "").Replace("{", "").Replace(" ", "");
                    isContent = true;
                }
                if (item.StartsWith("enum"))
                {
                    currentType = item.Replace("enum", "").Replace("{", "").Replace(" ", "");
                    isEnum = true;
                    isContent = true;
                }
                if (isContent && _messages.Contains(currentType))
                {
                    continue;
                }
                if (item.EndsWith("}"))
                {
                    isContent = false;
                    isEnum = false;
                }
                if (isEnum && !item.Contains("{"))
                {
                    var key = item.Replace(" ", "").Split('=')[0];
                    item = item.Replace(key, $"{currentType}_{key}");
                }
                types.Add(currentType);
                proto.AppendLine(item);
            }
            return (proto.ToString(), types);
        }

    }
}
