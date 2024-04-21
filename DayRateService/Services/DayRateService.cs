using DayRateService;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Google.Protobuf;
using MicroservicesProtos;
using GenericMessages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Caching.Distributed;
using LibHelpers;
using AutoMapper;

namespace DayRateService.Services
{
    [ActionInterceptor]
    public class DayRateService : DayRate.DayRateBase
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();
        private IDistributedCache cache;
        private IMapper mapper;

        public DayRateService(IDistributedCache cache, IMapper mapper)
        {
            this.cache = cache;
            this.mapper = mapper;
        }

        public override Task<AsyncResult> UpsertDayRate(UpsertDayRateRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked UpsertDayRate with RequestCamp.RequestId: {request.RequestCamp.RequestId}, DayRateId: {request.DayRate.Id}");

                //async without waiting
                _ = DayRateManager.Instance.UpsertDayRate(request.RequestCamp.RequestId, mapper.Map<LibDTO.DayRate>(request.DayRate), request.RequestCamp.RequiredDelay);

                //essential for request handling through reflection
                string requestId = nameof(UpsertDayRateRequest) + "@" + request.RequestCamp.RequestId;

                return Task.FromResult(new AsyncResult
                {
                    Awk = DayRateManager.Instance.NotifyHandledRequest(requestId) &&
                          cache.SetRecordAsync<UpsertDayRateRequest>(requestId, request).Result
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
                log.Info($"Invoked GetDayRate with RequestCamp.RequestId: {request.RequestCamp.RequestId}");

                //async without waiting
                _ = DayRateManager.Instance.GetDayRate(request.RequestCamp.RequestId, request.Id, request.RequestCamp.RequiredDelay);
                
                //essential for request handling through reflection
                string requestId = nameof(GetDayRateRequest) + "@" + request.RequestCamp.RequestId;

                return Task.FromResult(new AsyncResult
                {
                    Awk = DayRateManager.Instance.NotifyHandledRequest(requestId) &&
                          cache.SetRecordAsync<GetDayRateRequest>(requestId, request).Result
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
                log.Info($"Invoked GetDayRates with RequestCamp.RequestId: {request.RequestCamp.RequestId}");

                //async without waiting
                _ = DayRateManager.Instance.GetDayRates(request.RequestCamp.RequestId, request.RequestCamp.RequiredDelay);

                //essential for request handling through reflection
                string requestId = nameof(GetDayRatesRequest) + "@" + request.RequestCamp.RequestId;

                return Task.FromResult(new AsyncResult
                {
                    Awk = DayRateManager.Instance.NotifyHandledRequest(requestId) && 
                          cache.SetRecordAsync<GetDayRatesRequest>(requestId, request).Result
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
                log.Info($"Invoked DeleteDayRate with RequestCamp.RequestId: {request.RequestCamp.RequestId}, DayRateId: {request.Id}");

                //async without waiting
                _ = DayRateManager.Instance.DeleteDayRate(request.RequestCamp.RequestId, request.Id, request.RequestCamp.RequiredDelay);

                //essential for request handling through reflection
                string requestId = nameof(DeleteDayRateRequest) + "@" + request.RequestCamp.RequestId;

                return Task.FromResult(new AsyncResult
                {
                    Awk = DayRateManager.Instance.NotifyHandledRequest(requestId) && 
                          cache.SetRecordAsync<DeleteDayRateRequest>(requestId, request).Result
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
                log.Info($"Invoked CalculateDayFee with RequestCamp.RequestId: {request.RequestCamp.RequestId}");
                
                //async without waiting
                _ = DayRateManager.Instance.CalculateDayFee(request.RequestCamp.RequestId,
                        TimeSpan.FromSeconds(request.Start),
                        TimeSpan.FromSeconds(request.End),
                        request.RequestCamp.RequiredDelay);

                //essential for request handling through reflection
                string requestId = nameof(CalculateDayFeeRequest) + "@" + request.RequestCamp.RequestId;

                return Task.FromResult(new AsyncResult
                {
                    Awk = DayRateManager.Instance.NotifyHandledRequest(requestId) && 
                          cache.SetRecordAsync<CalculateDayFeeRequest>(requestId, request).Result
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

                DayRateManager.Instance.AppendNotifiedRequest(request.RequestId, request.Expiry);

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

                var notifiedRequestIds = DayRateManager.Instance.notifiedHandledRequests.Select(kvp => kvp.Key).ToList();

                foreach (var notifiedRequestId in notifiedRequestIds)
                    response.Requests.Add(new NotifyHandledRequestMsg()
                    {
                        RequestId = notifiedRequestId,
                        Expiry = DayRateManager.Instance.requestExpiryInMilliseconds
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
                    Result = DayRateManager.Instance.CanIHandle(request.RequestId, request.Expiry)
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
                    Result = DayRateManager.Instance.CheckIfNodeIdHigher(request.NodeId)
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


        public override Task<SyncResult> ConsiderNodeCoordinator(ConsiderNodeCoordinatorRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked ConsiderNodeCoordinator");

                return Task.FromResult(new SyncResult
                {
                    Result = DayRateManager.Instance.StartCoordinatorActivity()
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In ConsiderNodeCoordinator()!");
            }

            return Task.FromResult(new SyncResult
            {
                Result = false
            });
        }

        public override Task<GetCoordinatorIpResponse> GetCoordinatorIp(GetCoordinatorIpRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked GetCoordinatorIp");

                return Task.FromResult(new GetCoordinatorIpResponse
                {
                    CoordinatorIp = DayRateManager.Instance.GetCoordinatorIp()
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
                log.Info($"Invoked IsAlive");

                DayRateManager.Instance.CaptureCoordinator(request.SenderIP);

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
                    Awk = DayRateManager.Instance.AddClusterNode(request.ClusterNodeUri)
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
                    Awk = DayRateManager.Instance.RemoveClusterNode(request.ClusterNodeUri)
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
