using DayRateService;
using Grpc.Core;
using MicroservicesProtos;

namespace DayRateService.Services
{
    public class DayRateService : DayRate.GreeterBase
    {
        private readonly ILogger<DayRateService> _logger;
        public DayRateService(ILogger<DayRateService> logger)
        {
            _logger = logger;
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
