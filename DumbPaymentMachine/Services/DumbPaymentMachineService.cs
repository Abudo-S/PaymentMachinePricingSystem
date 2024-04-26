using DumbPaymentMachine;
using GenericMessages;
using Grpc.Core;
using PaymentMachineProtos;

namespace DumbPaymentMachine.Services
{
    public class DumbPaymentMachineService : PaymentMachineProtos.DumbPaymentMachine.DumbPaymentMachineBase
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

        public DumbPaymentMachineService()
        {
        }
        
        public override Task<SyncResult> ReceiveResponse(ReceiveResponseMsg request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked ReceiveResponse with RequestId: {request.RequestId}, ResultType: {request.ResultType}, ResultJson: {request.ResultJson}");

                //do nothing

                return Task.FromResult(new SyncResult
                {
                    Result = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In ReceiveResponse()!");
            }

            return Task.FromResult(new SyncResult
            {
                Result = false
            });
        }
    }
}
