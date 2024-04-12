using GenericMessages;
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

        public override Task<AsyncResult> UpsertWeekPayModel(UpsertWeekPayModelRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked UpsertWeekPayModel with RequestId: {request.RequestId}, WeekPayModelId: {request.WeekPayModel.Id}");

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In UpsertWeekPayModel()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }

        public override Task<AsyncResult> GetWeekPayModel(GetWeekPayModelRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked GetWeekPayModel with RequestId: {request.RequestId}");

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In GetWeekPayModel()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }

        public override Task<AsyncResult> GetWeekPayModels(GetWeekPayModelsRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked GetWeekPayModels with RequestId: {request.RequestId}");

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In GetWeekPayModels()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }

        public override Task<AsyncResult> DeleteWeekPayModel(DeleteWeekPayModelRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked DeleteWeekPayModel with RequestId: {request.RequestId}, WeekPayModelId: {request.Id}");

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In DeleteWeekPayModel()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }

        public override Task<AsyncResult> CalculateWeekPayModelFee(CalculateWeekPayModelFeeRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked CalculateWeekPayModelFee with RequestId: {request.RequestId}");

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In CalculateWeekPayModelFee()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }
    }
}
