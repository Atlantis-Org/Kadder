using System;
using Grpc.Core;
using System.Threading.Tasks;
using Atlantis.Grpc.Logging;
using Atlantis.Grpc.Middlewares;
using Atlantis.Grpc.Utilies;

namespace Atlantis.Grpc
{
    public class GrpcMessageServicer
    {
        private readonly GrpcHandlerBuilder _builder;
        private readonly ILogger _logger;

        public GrpcMessageServicer()
        {
            _builder = ObjectContainer.Resolve<GrpcHandlerBuilder>();
            _logger = ObjectContainer.Resolve<ILoggerFactory>()
                .Create<GrpcMessageServicer>();
        }

        public async Task<TMessageResult> ProcessAsync<TMessage, TMessageResult>(
            TMessage message, ServerCallContext callContext)
            where TMessage : BaseMessage
            where TMessageResult : MessageResult, new()
        {
            try
            {
                var context = new GrpcContext(message, callContext);
                await _builder.DelegateProxyAsync(context);
                if (context.Result is TMessageResult)
                {
                    return (TMessageResult)context.Result;
                } 
                else
                {
                    return new TMessageResult()
                    {
                        Code = context.Result.Code,
                        Message = context.Result.Message
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.Error("消息处理失败，原因：{@Message}，Msg：{@message}", ex, new object[] { message, ex.Message });
                return new TMessageResult() { Code = ResultCode.Exception, Message = "服务处理出错，请重试！" };
            }

        }
    }
}
