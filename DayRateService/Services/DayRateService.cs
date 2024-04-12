using DayRateService;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Google.Protobuf;
using MicroservicesProtos;
using GenericMessages;

namespace DayRateService.Services
{
    public class DayRateService : DayRate.DayRateBase
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

        public DayRateService()
        {
            
        }

        private bool WriteAndAssignRequest()
        {
            //write request in redis cache
            //assign request id to an available microservice instance

            return false;
        }

        public override Task<AsyncResult> UpsertDayRate(UpsertDayRateRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked UpsertDayRate with RequestId: {request.RequestId}, DayRateId: {request.DayRate.Id}");

                //WriteAndAssignRequest()

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In UpsertDayRate()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }

        public override Task<AsyncResult> GetDayRate(GetDayRateRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked GetDayRate with RequestId: {request.RequestId}");

                //WriteAndAssignRequest()

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In GetDayRate()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }

        public override Task<AsyncResult> GetDayRates(GetDayRatesRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked GetDayRates with RequestId: {request.RequestId}");

                //WriteAndAssignRequest()

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In GetDayRates()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }

        public override Task<AsyncResult> DeleteDayRate(DeleteDayRateRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked DeleteDayRate with RequestId: {request.RequestId}, DayRateId: {request.Id}");

                //WriteAndAssignRequest()

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In DeleteDayRate()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }

        public override Task<AsyncResult> CalculateDayFee(CalculateDayFeeRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked CalculateDayFee with RequestId: {request.RequestId}");

                //WriteAndAssignRequest()

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In CalculateDayFee()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }
    }
}