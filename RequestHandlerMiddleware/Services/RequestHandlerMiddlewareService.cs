using Grpc.Core;
using MiddlewareProtos;

namespace RequestHandlerMiddleware.Services
{
    public class RequestHandlerMiddlewareService : RequestHandler.RequestHandlerBase
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();
        public RequestHandlerMiddlewareService()
        {
        }

        //public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        //{
        //    return Task.FromResult(new HelloReply
        //    {
        //        Message = "Hello " + request.Name
        //    });
        //}
    }
}
