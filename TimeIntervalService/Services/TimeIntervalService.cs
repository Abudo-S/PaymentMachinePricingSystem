using Grpc.Core;
using TimeIntervalService;
using MicroservicesProtos;

namespace TimeIntervalService.Services
{
    public class TimeIntervalService : TimeInterval.TimeIntervalBase
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();
        public TimeIntervalService()
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
