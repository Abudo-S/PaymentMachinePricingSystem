using Grpc.Core;
using TimeIntervalService;
using MicroservicesProtos;
using GenericMessages;
using AutoMapper;
using Microsoft.Extensions.Caching.Distributed;
using LibHelpers;

namespace TimeIntervalService.Services
{
    public class TimeIntervalService : TimeInterval.TimeIntervalBase
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();
        private IDistributedCache cache;
        private IMapper mapper;

        public TimeIntervalService(IDistributedCache cache, IMapper mapper)
        {
            this.cache = cache;
            this.mapper = mapper;
        }

        public override Task<AsyncResult> UpsertTimeInterval(UpsertTimeIntervalRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked UpsertTimeInterval with RequestCamp.RequestId: {request.RequestCamp.RequestId}, TimeIntervalId: {request.TimeInterval.Id}");

                //essential for request handling through reflection
                string requestId = nameof(UpsertTimeIntervalRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => TimeIntervalManager.Instance.UpsertTimeInterval(requestId, mapper.Map<LibDTO.TimeInterval>(request.TimeInterval), request.RequestCamp.RequiredDelay));

                return Task.FromResult(new AsyncResult
                {
                    Awk = TimeIntervalManager.Instance.NotifyHandledRequest(requestId) &&
                          cache.SetRecordAsync<UpsertTimeIntervalRequest>(requestId, request).Result
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
                log.Info($"Invoked GetTimeInterval with RequestCamp.RequestId: {request.RequestCamp.RequestId}");

                //essential for request handling through reflection
                string requestId = nameof(GetTimeIntervalRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => TimeIntervalManager.Instance.GetTimeInterval(requestId, request.Id, request.RequestCamp.RequiredDelay));

                return Task.FromResult(new AsyncResult
                {
                    Awk = TimeIntervalManager.Instance.NotifyHandledRequest(requestId) &&
                          cache.SetRecordAsync<GetTimeIntervalRequest>(requestId, request).Result
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
                log.Info($"Invoked GetTimeIntervals with RequestCamp.RequestId: {request.RequestCamp.RequestId}");

                //essential for request handling through reflection
                string requestId = nameof(GetTimeIntervalsRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => TimeIntervalManager.Instance.GetTimeIntervals(requestId, request.RequestCamp.RequiredDelay));

                return Task.FromResult(new AsyncResult
                {
                    Awk = TimeIntervalManager.Instance.NotifyHandledRequest(requestId) &&
                          cache.SetRecordAsync<GetTimeIntervalsRequest>(requestId, request).Result
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
                log.Info($"Invoked DeleteTimeInterval with RequestCamp.RequestId: {request.RequestCamp.RequestId}, TimeIntervalId: {request.Id}");

                //essential for request handling through reflection
                string requestId = nameof(GetTimeIntervalRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => TimeIntervalManager.Instance.DeleteTimeInterval(requestId, request.Id, request.RequestCamp.RequiredDelay));

                return Task.FromResult(new AsyncResult
                {
                    Awk = TimeIntervalManager.Instance.NotifyHandledRequest(requestId) &&
                          cache.SetRecordAsync<DeleteTimeIntervalRequest>(requestId, request).Result
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
                log.Info($"Invoked CalculateTimeIntervalFee with RequestCamp.RequestId: {request.RequestCamp.RequestId}");

                //essential for request handling through reflection
                string requestId = nameof(CalculateTimeIntervalFeeRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => TimeIntervalManager.Instance.CalculateTimeIntervalFee(requestId,
                                TimeSpan.FromSeconds(request.Start),
                                TimeSpan.FromSeconds(request.End),
                                request.RequestCamp.RequiredDelay)
                );

                return Task.FromResult(new AsyncResult
                {
                    Awk = TimeIntervalManager.Instance.NotifyHandledRequest(requestId) &&
                          cache.SetRecordAsync<CalculateTimeIntervalFeeRequest>(requestId, request).Result
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

        public override Task<SyncResult> NotifyHandledRequest(NotifyHandledRequestMsg request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked NotifyHandledRequest with RequestId: {request.RequestId}");

                TimeIntervalManager.Instance.AppendNotifiedRequest(request.RequestId, request.Expiry);

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

                var notifiedRequestIds = TimeIntervalManager.Instance.notifiedHandledRequests.Select(kvp => kvp.Key).ToList();

                foreach (var notifiedRequestId in notifiedRequestIds)
                    response.Requests.Add(new NotifyHandledRequestMsg()
                    {
                        RequestId = notifiedRequestId,
                        Expiry = TimeIntervalManager.Instance.requestExpiryInMilliseconds
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
                    Result = TimeIntervalManager.Instance.CanIHandle(request.RequestId, request.Expiry)
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
                    Result = TimeIntervalManager.Instance.CheckIfNodeIdHigher(request.NodeId)
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

                var result = await TimeIntervalManager.Instance.StartCoordinatorActivity();

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
                    CoordinatorIp = TimeIntervalManager.Instance.GetCoordinatorIp()
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

                Task.Run(() => TimeIntervalManager.Instance.CaptureCoordinator(request.SenderIP));

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
                    Awk = TimeIntervalManager.Instance.AddClusterNode(request.ClusterNodeUri)
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
                    Awk = TimeIntervalManager.Instance.RemoveClusterNode(request.ClusterNodeUri)
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
