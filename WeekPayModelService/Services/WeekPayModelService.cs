using AutoMapper;
using GenericMessages;
using Grpc.Core;
using LibHelpers;
using MicroservicesProtos;
using Microsoft.Extensions.Caching.Distributed;

namespace WeekPayModelService.Services
{
    public class WeekPayModelService : WeekPayModel.WeekPayModelBase
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();
        private IDistributedCache cache;
        private IMapper mapper;

        public WeekPayModelService(IDistributedCache cache, IMapper mapper)
        {
            this.cache = cache;
            this.mapper = mapper;
        }

        public override Task<AsyncResult> UpsertWeekPayModel(UpsertWeekPayModelRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked UpsertWeekPayModel with RequestCamp.RequestId: {request.RequestCamp.RequestId}, WeekPayModelId: {request.WeekPayModel.Id}");

                //essential for request handling through reflection
                string requestId = nameof(UpsertWeekPayModelRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => WeekPayModelManager.Instance.UpsertWeekPayModel(requestId, mapper.Map<LibDTO.WeekPayModel>(request.WeekPayModel), request.RequestCamp.RequiredDelay));

                return Task.FromResult(new AsyncResult
                {
                    Awk = WeekPayModelManager.Instance.NotifyHandledRequest(requestId) &&
                          cache.SetRecordAsync<UpsertWeekPayModelRequest>(requestId, request).Result
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
                log.Info($"Invoked GetWeekPayModel with RequestCamp.RequestId: {request.RequestCamp.RequestId}");

                //essential for request handling through reflection
                string requestId = nameof(GetWeekPayModelRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => WeekPayModelManager.Instance.GetWeekPayModel(requestId, request.Id, request.RequestCamp.RequiredDelay));

                return Task.FromResult(new AsyncResult
                {
                    Awk = WeekPayModelManager.Instance.NotifyHandledRequest(requestId) &&
                          cache.SetRecordAsync<GetWeekPayModelRequest>(requestId, request).Result
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
                log.Info($"Invoked GetWeekPayModels with RequestCamp.RequestId: {request.RequestCamp.RequestId}");

                //essential for request handling through reflection
                string requestId = nameof(GetWeekPayModelsRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => WeekPayModelManager.Instance.GetWeekPayModels(requestId, request.RequestCamp.RequiredDelay));

                return Task.FromResult(new AsyncResult
                {
                    Awk = WeekPayModelManager.Instance.NotifyHandledRequest(requestId) &&
                          cache.SetRecordAsync<GetWeekPayModelsRequest>(requestId, request).Result
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
                log.Info($"Invoked DeleteWeekPayModel with RequestCamp.RequestId: {request.RequestCamp.RequestId}, WeekPayModelId: {request.Id}");

                //essential for request handling through reflection
                string requestId = nameof(GetWeekPayModelRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => WeekPayModelManager.Instance.DeleteWeekPayModel(requestId, request.Id, request.RequestCamp.RequiredDelay));

                return Task.FromResult(new AsyncResult
                {
                    Awk = WeekPayModelManager.Instance.NotifyHandledRequest(requestId) &&
                          cache.SetRecordAsync<DeleteWeekPayModelRequest>(requestId, request).Result
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

        public override Task<AsyncResult> CalculateWeekFee(CalculateWeekFeeRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked CalculateWeekFee with RequestCamp.RequestId: {request.RequestCamp.RequestId}");

                //essential for request handling through reflection
                string requestId = nameof(CalculateDayFeeRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => WeekPayModelManager.Instance.CalculateWeekFee(requestId,
                                DateTime.FromOADate(request.Start),
                                DateTime.FromOADate(request.End),
                                request.RequestCamp.RequiredDelay)
                );

                return Task.FromResult(new AsyncResult
                {
                    Awk = WeekPayModelManager.Instance.NotifyHandledRequest(requestId) &&
                          cache.SetRecordAsync<CalculateWeekFeeRequest>(requestId, request).Result
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

        public override Task<SyncResult> NotifyHandledRequest(NotifyHandledRequestMsg request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked NotifyHandledRequest with RequestId: {request.RequestId}");

                WeekPayModelManager.Instance.AppendNotifiedRequest(request.RequestId, request.Expiry);

                return Task.FromResult(new SyncResult
                {
                    Result = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In NotifyHandledRequest()!");
            }

            return Task.FromResult(new SyncResult
            {
                Result = false
            });
        }

        public override Task<GetNotifiedRequestsResponse> GetNotifiedRequests(GetNotifiedRequestsRequest request, ServerCallContext context)
        {
            var response = new GetNotifiedRequestsResponse();

            try
            {
                log.Info($"Invoked GetNotifiedRequests with senderIP: {request.SenderIP}");

                var notifiedRequestIds = WeekPayModelManager.Instance.notifiedHandledRequests.Select(kvp => kvp.Key).ToList();

                foreach (var notifiedRequestId in notifiedRequestIds)
                    response.Requests.Add(new NotifyHandledRequestMsg()
                    {
                        RequestId = notifiedRequestId,
                        Expiry = WeekPayModelManager.Instance.requestExpiryInMilliseconds
                    });

                return Task.FromResult(response);
            }
            catch (Exception ex)
            {
                log.Error(ex, " In GetNotifiedRequests()!");
            }

            return Task.FromResult(response);
        }

        public override Task<SyncResult> CanIHandle(CanIHandleRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked CanIHandle with requestId: {request.RequestId}");

                return Task.FromResult(new SyncResult
                {
                    Result = WeekPayModelManager.Instance.CanIHandle(request.RequestId, request.Expiry)
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In CanIHandle()!");
            }

            return Task.FromResult(new SyncResult
            {
                Result = false
            });
        }

        /// <summary>
        /// [BullyElection] invoked when another cluster's node (with lower nodeId) notices coordinator inactivity
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override Task<SyncResult> CanICoordinate(CanICoordinateRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked CanICoordinate with NodeId: {request.NodeId}");

                return Task.FromResult(new SyncResult
                {
                    Result = WeekPayModelManager.Instance.CheckIfNodeIdHigher(request.NodeId)
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In CanICoordinate()!");
            }

            return Task.FromResult(new SyncResult
            {
                Result = false
            });
        }


        public override async Task<SyncResult> ConsiderNodeCoordinator(ConsiderNodeCoordinatorRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked ConsiderNodeCoordinator");

                var result = await WeekPayModelManager.Instance.StartCoordinatorActivity();

                return new SyncResult
                {
                    Result = result
                };
            }
            catch (Exception ex)
            {
                log.Error(ex, " In ConsiderNodeCoordinator()!");
            }

            return new SyncResult
            {
                Result = false
            };
        }

        public override Task<GetCoordinatorIpResponse> GetCoordinatorIp(GetCoordinatorIpRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked GetCoordinatorIp");

                return Task.FromResult(new GetCoordinatorIpResponse
                {
                    CoordinatorIp = WeekPayModelManager.Instance.GetCoordinatorIp()
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In GetCoordinatorIp()!");
            }

            return Task.FromResult(new GetCoordinatorIpResponse
            {
                CoordinatorIp = "ERROR"
            });
        }

        public override Task<SyncResult> IsAlive(IsAliveRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked IsAlive with SenderIP: {request.SenderIP}");

                Task.Run(() => WeekPayModelManager.Instance.CaptureCoordinator(request.SenderIP));

                return Task.FromResult(new SyncResult
                {
                    Result = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In IsAlive()!");
            }

            return Task.FromResult(new SyncResult
            {
                Result = false
            });
        }

        public override Task<AsyncResult> AddClusterNode(AddOrRemoveClusterNodeRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked AddClusterNode with clusterNodeUri: {request.ClusterNodeUri}");

                return Task.FromResult(new AsyncResult
                {
                    Awk = WeekPayModelManager.Instance.AddClusterNode(request.ClusterNodeUri)
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In RemoveClusterNode()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }

        public override Task<AsyncResult> RemoveClusterNode(AddOrRemoveClusterNodeRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked RemoveClusterNode with clusterNodeUri: {request.ClusterNodeUri}");


                return Task.FromResult(new AsyncResult
                {
                    Awk = WeekPayModelManager.Instance.RemoveClusterNode(request.ClusterNodeUri)
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In RemoveClusterNode()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }
    }
}
