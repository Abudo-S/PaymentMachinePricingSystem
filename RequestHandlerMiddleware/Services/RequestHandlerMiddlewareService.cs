using Grpc.Core;
using RequestHandlerMiddleware;

namespace RequestHandlerMiddleware.Services
{
    public class RequestHandlerMiddlewareService : Greeter.GreeterBase
    {
        private readonly ILogger<RequestHandlerMiddlewareService> _logger;
        public RequestHandlerMiddlewareService(ILogger<RequestHandlerMiddlewareService> logger)
        {
            _logger = logger;
        }

        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply
            {
                Message = "Hello " + request.Name
            });
        }
    }
}
