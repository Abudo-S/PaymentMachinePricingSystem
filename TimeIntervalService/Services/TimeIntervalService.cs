using Grpc.Core;
using TimeIntervalService;

namespace TimeIntervalService.Services
{
    public class TimeIntervalService : Greeter.GreeterBase
    {
        private readonly ILogger<TimeIntervalService> _logger;
        public TimeIntervalService(ILogger<TimeIntervalService> logger)
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
