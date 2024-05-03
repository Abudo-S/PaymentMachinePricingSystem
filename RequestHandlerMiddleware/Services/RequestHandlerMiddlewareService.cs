using Amazon.Runtime.Internal.Util;
using AutoMapper;
using GenericMessages;
using Grpc.Core;
using LibDTO.Enum;
using LibHelpers;
using MiddlewareProtos;
using static Google.Rpc.Context.AttributeContext.Types;

namespace RequestHandlerMiddleware.Services
{
    public class RequestHandlerMiddlewareService : RequestHandler.RequestHandlerBase
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();
        private int RequiredDelayInMilliseconds;

        public RequestHandlerMiddlewareService(IConfiguration configuration)
        {
            RequiredDelayInMilliseconds = (int)(configuration.GetValue(typeof(int), "RequiredDelayInMilliseconds") ?? 5000);
        }

        public override async Task<SyncResult> NotifyClusterCoordinator(NotifyClusterCoordinatorRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked NotifyClusterCoordinator with ClusterType: {request.ClusterType}, CoordinatorEndpoint: {request.CoordinatorEndpoint}");

                await RequestHandlerManager.Instance.SetClusterCoordinator(request.ClusterType, request.CoordinatorEndpoint, "/LoadBalancer");

                return new SyncResult
                {
                    Result = true
                };
            }
            catch (Exception ex)
            {
                log.Error(ex, " In NotifyClusterCoordinator()!");
            }

            return new SyncResult
            {
                Result = false
            };
        }

        public override Task<SyncResult> NotifyProcessedRequest(NotifyProcessedRequestMessage request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked NotifyProcessedRequest with RequestId: {request.RequestId}, ResponseType: {request.ResponseType}");

                ThreadPool.QueueUserWorkItem(async (object? state) =>
                    await RequestHandlerManager.Instance.SendPaymentMachineResponse(request.RequestId,
                        request.ResponseType,
                        request.ResponseJson
                    )
                );

                return Task.FromResult(new SyncResult
                {
                    Result = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In NotifyProcessedRequest()!");
            }

            return Task.FromResult(new SyncResult
            {
                Result = false
            });
        }

        public override Task<CalculateFeeResponse> CalculateFee(CalculateFeeRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked CalculateFee with RequestId: {request.RequestId}, SenderEndpoint: {request.SenderEndpoint}");

                //analyze request period before redirecting to a specific cluster

                ThreadPool.QueueUserWorkItem((object? state) => { });

                return Task.FromResult(new CalculateFeeResponse
                {
                    Fee = 0
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In CalculateFee()!");
            }

            return Task.FromResult(new CalculateFeeResponse
            {
                Fee = 0
            });
        }

        #region WeekPayModel
        public override Task<AsyncResult> UpsertWeekPayModel(UpsertWeekPayModelRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked UpsertWeekPayModel with RequestId: {request.RequestId}, SenderEndpoint: {request.SenderEndpoint}");

                if (!RequestHandlerManager.Instance.clusterCoordinators.ContainsKey(ClusterType.WeekPayModelCluster))
                {
                    throw new KeyNotFoundException($"Can't find cluster coordinator with ClusterType: {ClusterType.WeekPayModelCluster}");
                }

                ThreadPool.QueueUserWorkItem(async (object? state) =>
                    await RequestHandlerManager.Instance.HandleUpsertWeekPayModel(new MicroservicesProtos.UpsertWeekPayModelRequest()
                    {
                        WeekPayModel = request.WeekPayModel,
                        RequestCamp = new GenericRequestData()
                        {
                            RequestId = RequestHandlerManager.GetRequestId(request.SenderEndpoint, request.RequestId),
                            RequiredDelay = RequiredDelayInMilliseconds
                        }
                    })
                );

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
                log.Info($"Invoked GetWeekPayModel with RequestId: {request.RequestId}, SenderEndpoint: {request.SenderEndpoint}");
                
                if (!RequestHandlerManager.Instance.clusterCoordinators.ContainsKey(ClusterType.WeekPayModelCluster))
                {
                    throw new KeyNotFoundException($"Can't find cluster coordinator with ClusterType: {ClusterType.WeekPayModelCluster}");
                }

                ThreadPool.QueueUserWorkItem(async (object? state) =>
                    await RequestHandlerManager.Instance.HandleGetWeekPayModel(new MicroservicesProtos.GetWeekPayModelRequest()
                    {
                        Id = request.Id,
                        RequestCamp = new GenericRequestData()
                        {
                            RequestId = RequestHandlerManager.GetRequestId(request.SenderEndpoint, request.RequestId),
                            RequiredDelay = RequiredDelayInMilliseconds
                        }
                    })
                );

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
                log.Info($"Invoked GetWeekPayModels with RequestId: {request.RequestId}, SenderEndpoint: {request.SenderEndpoint}");

                if (!RequestHandlerManager.Instance.clusterCoordinators.ContainsKey(ClusterType.WeekPayModelCluster))
                {
                    throw new KeyNotFoundException($"Can't find cluster coordinator with ClusterType: {ClusterType.WeekPayModelCluster}");
                }

                ThreadPool.QueueUserWorkItem(async (object? state) =>
                    await RequestHandlerManager.Instance.HandleGetWeekPayModels(new MicroservicesProtos.GetWeekPayModelsRequest()
                    {
                        RequestCamp = new GenericRequestData()
                        {
                            RequestId = RequestHandlerManager.GetRequestId(request.SenderEndpoint, request.RequestId),
                            RequiredDelay = RequiredDelayInMilliseconds
                        }
                    })
                );

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
                log.Info($"Invoked DeleteWeekPayModel with RequestId: {request.RequestId}, SenderEndpoint: {request.SenderEndpoint}");

                if (!RequestHandlerManager.Instance.clusterCoordinators.ContainsKey(ClusterType.WeekPayModelCluster))
                {
                    throw new KeyNotFoundException($"Can't find cluster coordinator with ClusterType: {ClusterType.WeekPayModelCluster}");
                }

                ThreadPool.QueueUserWorkItem(async (object? state) =>
                    await RequestHandlerManager.Instance.HandleDeleteWeekPayModel(new MicroservicesProtos.DeleteWeekPayModelRequest()
                    {
                        Id = request.Id,
                        RequestCamp = new GenericRequestData()
                        {
                            RequestId = RequestHandlerManager.GetRequestId(request.SenderEndpoint, request.RequestId),
                            RequiredDelay = RequiredDelayInMilliseconds
                        }
                    })
                );

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
        #endregion

        #region DayRate
        public override Task<AsyncResult> UpsertDayRate(UpsertDayRateRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked UpsertDayRate with RequestId: {request.RequestId}, SenderEndpoint: {request.SenderEndpoint}");

                if (!RequestHandlerManager.Instance.clusterCoordinators.ContainsKey(ClusterType.DayRateCluster))
                {
                    throw new KeyNotFoundException($"Can't find cluster coordinator with ClusterType: {ClusterType.DayRateCluster}");
                }

                ThreadPool.QueueUserWorkItem(async (object? state) =>
                    await RequestHandlerManager.Instance.HandleUpsertDayRate(new MicroservicesProtos.UpsertDayRateRequest()
                    {
                        DayRate = request.DayRate,
                        RequestCamp = new GenericRequestData()
                        {
                            RequestId = RequestHandlerManager.GetRequestId(request.SenderEndpoint, request.RequestId),
                            RequiredDelay = RequiredDelayInMilliseconds
                        }
                    })
                );

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
                log.Info($"Invoked GetDayRate with RequestId: {request.RequestId}, SenderEndpoint: {request.SenderEndpoint}");
                
                if (!RequestHandlerManager.Instance.clusterCoordinators.ContainsKey(ClusterType.DayRateCluster))
                {
                    throw new KeyNotFoundException($"Can't find cluster coordinator with ClusterType: {ClusterType.DayRateCluster}");
                }

                ThreadPool.QueueUserWorkItem(async (object? state) =>
                    await RequestHandlerManager.Instance.HandleGetDayRate(new MicroservicesProtos.GetDayRateRequest()
                    {
                        Id = request.Id,
                        RequestCamp = new GenericRequestData()
                        {
                            RequestId = RequestHandlerManager.GetRequestId(request.SenderEndpoint, request.RequestId),
                            RequiredDelay = RequiredDelayInMilliseconds
                        }
                    })
                );

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
                log.Info($"Invoked GetDayRates with RequestId: {request.RequestId}, SenderEndpoint: {request.SenderEndpoint}");

                if (!RequestHandlerManager.Instance.clusterCoordinators.ContainsKey(ClusterType.DayRateCluster))
                {
                    throw new KeyNotFoundException($"Can't find cluster coordinator with ClusterType: {ClusterType.DayRateCluster}");
                }

                ThreadPool.QueueUserWorkItem(async (object? state) =>
                    await RequestHandlerManager.Instance.HandleGetDayRates(new MicroservicesProtos.GetDayRatesRequest()
                    {
                        RequestCamp = new GenericRequestData()
                        {
                            RequestId = RequestHandlerManager.GetRequestId(request.SenderEndpoint, request.RequestId),
                            RequiredDelay = RequiredDelayInMilliseconds
                        }
                    })
                );

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
                log.Info($"Invoked DeleteDayRate with RequestId: {request.RequestId}, SenderEndpoint: {request.SenderEndpoint}");

                if (!RequestHandlerManager.Instance.clusterCoordinators.ContainsKey(ClusterType.DayRateCluster))
                {
                    throw new KeyNotFoundException($"Can't find cluster coordinator with ClusterType: {ClusterType.DayRateCluster}");
                }

                ThreadPool.QueueUserWorkItem(async (object? state) =>
                    await RequestHandlerManager.Instance.HandleDeleteDayRate(new MicroservicesProtos.DeleteDayRateRequest()
                    {
                        Id = request.Id,
                        RequestCamp = new GenericRequestData()
                        {
                            RequestId = RequestHandlerManager.GetRequestId(request.SenderEndpoint, request.RequestId),
                            RequiredDelay = RequiredDelayInMilliseconds
                        }
                    })
                );

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
        #endregion

        #region TimeInterval
        public override Task<AsyncResult> UpsertTimeInterval(UpsertTimeIntervalRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked UpsertTimeInterval with RequestId: {request.RequestId}, SenderEndpoint: {request.SenderEndpoint}");

                if (!RequestHandlerManager.Instance.clusterCoordinators.ContainsKey(ClusterType.TimeIntervalCluster))
                {
                    throw new KeyNotFoundException($"Can't find cluster coordinator with ClusterType: {ClusterType.TimeIntervalCluster}");
                }

                ThreadPool.QueueUserWorkItem(async (object? state) =>
                    await RequestHandlerManager.Instance.HandleUpsertTimeInterval(new MicroservicesProtos.UpsertTimeIntervalRequest()
                    {
                        TimeInterval = request.TimeInterval,
                        RequestCamp = new GenericRequestData()
                        {
                            RequestId = RequestHandlerManager.GetRequestId(request.SenderEndpoint, request.RequestId),
                            RequiredDelay = RequiredDelayInMilliseconds
                        }
                    })
                );

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
                log.Info($"Invoked GetTimeInterval with RequestId: {request.RequestId}, SenderEndpoint: {request.SenderEndpoint}");

                if (!RequestHandlerManager.Instance.clusterCoordinators.ContainsKey(ClusterType.TimeIntervalCluster))
                {
                    throw new KeyNotFoundException($"Can't find cluster coordinator with ClusterType: {ClusterType.TimeIntervalCluster}");
                }

                ThreadPool.QueueUserWorkItem(async (object? state) =>
                    await RequestHandlerManager.Instance.HandleGetTimeInterval(new MicroservicesProtos.GetTimeIntervalRequest()
                    {
                        Id = request.Id,
                        RequestCamp = new GenericRequestData()
                        {
                            RequestId = RequestHandlerManager.GetRequestId(request.SenderEndpoint, request.RequestId),
                            RequiredDelay = RequiredDelayInMilliseconds
                        }
                    })
                );

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
                log.Info($"Invoked GetTimeIntervals with RequestId: {request.RequestId}, SenderEndpoint: {request.SenderEndpoint}");

                if (!RequestHandlerManager.Instance.clusterCoordinators.ContainsKey(ClusterType.TimeIntervalCluster))
                {
                    throw new KeyNotFoundException($"Can't find cluster coordinator with ClusterType: {ClusterType.TimeIntervalCluster}");
                }

                ThreadPool.QueueUserWorkItem(async (object? state) =>
                    await RequestHandlerManager.Instance.HandleGetTimeIntervals(new MicroservicesProtos.GetTimeIntervalsRequest()
                    {
                        RequestCamp = new GenericRequestData()
                        {
                            RequestId = RequestHandlerManager.GetRequestId(request.SenderEndpoint, request.RequestId),
                            RequiredDelay = RequiredDelayInMilliseconds
                        }
                    })
                );

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
                log.Info($"Invoked DeleteTimeInterval with RequestId: {request.RequestId}, SenderEndpoint: {request.SenderEndpoint}");

                if (!RequestHandlerManager.Instance.clusterCoordinators.ContainsKey(ClusterType.TimeIntervalCluster))
                {
                    throw new KeyNotFoundException($"Can't find cluster coordinator with ClusterType: {ClusterType.TimeIntervalCluster}");
                }

                ThreadPool.QueueUserWorkItem(async (object? state) =>
                    await RequestHandlerManager.Instance.HandleDeleteTimeInterval(new MicroservicesProtos.DeleteTimeIntervalRequest()
                    {
                        Id = request.Id,
                        RequestCamp = new GenericRequestData()
                        {
                            RequestId = RequestHandlerManager.GetRequestId(request.SenderEndpoint, request.RequestId),
                            RequiredDelay = RequiredDelayInMilliseconds
                        }
                    })
                );

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
        #endregion
    }
}