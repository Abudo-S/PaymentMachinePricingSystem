using Grpc.Core;
using WeekPayModelService;

namespace WeekPayModelService.Services
{
    public class WeekPayModelService : Greeter.GreeterBase
    {
        private readonly ILogger<WeekPayModelService> _logger;
        public WeekPayModelService(ILogger<WeekPayModelService> logger)
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
