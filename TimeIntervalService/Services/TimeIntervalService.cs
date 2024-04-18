using Grpc.Core;
using TimeIntervalService;
using MicroservicesProtos;
using GenericMessages;

namespace TimeIntervalService.Services
{
    public class TimeIntervalService : TimeInterval.TimeIntervalBase
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();
        public TimeIntervalService()
        {
            
        }
        public override Task<AsyncResult> UpsertTimeInterval(UpsertTimeIntervalRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked UpsertTimeInterval with RequestId: {request.RequestCamp.RequestId}, TimeIntervalId: {request.TimeInterval.Id}");

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In UpsertTimeInterval()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }

        public override Task<AsyncResult> GetTimeInterval(GetTimeIntervalRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked GetTimeInterval with RequestId: {request.RequestCamp.RequestId}");

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In GetTimeInterval()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }

        public override Task<AsyncResult> GetTimeIntervals(GetTimeIntervalsRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked GetTimeIntervals with RequestId: {request.RequestCamp.RequestId}");

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In GetTimeIntervals()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }

        public override Task<AsyncResult> DeleteTimeInterval(DeleteTimeIntervalRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked DeleteTimeInterval with RequestId: {request.RequestCamp.RequestId}, TimeIntervalId: {request.Id}");

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In DeleteTimeInterval()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }

        public override Task<AsyncResult> CalculateTimeIntervalFee(CalculateTimeIntervalFeeRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked CalculateTimeIntervalFee with RequestId: {request.RequestCamp.RequestId}");

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In CalculateTimeIntervalFee()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }
    }
}
