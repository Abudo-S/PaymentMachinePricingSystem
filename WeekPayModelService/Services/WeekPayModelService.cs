using Grpc.Core;
using MicroservicesProtos;

namespace WeekPayModelService.Services
{
    public class WeekPayModelService : WeekPayModel.WeekPayModelBase
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();
        public WeekPayModelService()
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
