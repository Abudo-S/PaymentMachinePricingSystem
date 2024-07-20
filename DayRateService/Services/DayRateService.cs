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
using static Google.Rpc.Context.AttributeContext.Types;

namespace DayRateService.Services
{
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
                
                //essential for request handling through reflection
                string requestId = nameof(UpsertDayRateRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => DayRateManager.Instance.UpsertDayRate(requestId, mapper.Map<LibDTO.DayRate>(request.DayRate), request.RequestCamp.RequiredDelay));

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

                //essential for request handling through reflection
                string requestId = nameof(GetDayRateRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => DayRateManager.Instance.GetDayRate(requestId, request.Id, request.RequestCamp.RequiredDelay));

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

                //essential for request handling through reflection
                string requestId = nameof(GetDayRatesRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => DayRateManager.Instance.GetDayRates(requestId, request.RequestCamp.RequiredDelay));

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

                //essential for request handling through reflection
                string requestId = nameof(GetDayRateRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => DayRateManager.Instance.DeleteDayRate(requestId, request.Id, request.RequestCamp.RequiredDelay));

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

                //essential for request handling through reflection
                string requestId = nameof(CalculateDayFeeRequest) + "@" + request.RequestCamp.RequestId;

                //async without waiting
                Task.Run(() => DayRateManager.Instance.CalculateDayFee(requestId,
                                TimeSpan.FromSeconds(request.Start),
                                TimeSpan.FromSeconds(request.End),
                                request.RequestCamp.RequiredDelay)
                );

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


        public override async Task<SyncResult> ConsiderNodeCoordinator(ConsiderNodeCoordinatorRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked ConsiderNodeCoordinator");

                var result = await DayRateManager.Instance.StartCoordinatorActivity();

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
                log.Info($"Invoked IsAlive with SenderIP: {request.SenderIP}");

                Task.Run(() => DayRateManager.Instance.CaptureCoordinator(request.SenderIP));

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

                _ = DayRateManager.Instance.AddClusterNode(request.ClusterNodeUri);

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
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

                _ = DayRateManager.Instance.RemoveClusterNode(request.ClusterNodeUri);

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
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

        public override Task<AsyncResult> NotifyNodePresence(NotifyNodePresenceRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked NotifyNodePresence with NodeUri: {request.NodeUri}");

                _ = DayRateManager.Instance.AppendPresentClusterNode(request.NodeUri);

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In NotifyNodePresence()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }

        public override Task<AsyncResult> SetDefaultOperationDelay(SetDefaultOperationDelayRequest request, ServerCallContext context)
        {
            try
            {
                log.Info($"Invoked SetDefaultOperationDelay with DelayInMilliseconds: {request.DelayInMilliseconds}");

                DayRateManager.AdditionalOperationDelay = request.DelayInMilliseconds;

                return Task.FromResult(new AsyncResult
                {
                    Awk = true
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, " In SetDefaultOperationDelay()!");
            }

            return Task.FromResult(new AsyncResult
            {
                Awk = false
            });
        }
    }
}
