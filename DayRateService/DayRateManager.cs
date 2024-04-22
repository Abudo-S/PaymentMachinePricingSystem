using AutoMapper;
using DayRateService.DbServices;
using GenericMessages;
using Grpc.Core;
using LibDTO;
using LibDTO.Generic;
using LibHelpers;
using MicroservicesProtos;
using Microsoft.AspNetCore.Authentication;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Timers;
using static Google.Rpc.Context.AttributeContext.Types;

namespace DayRateService
{
    public class DayRateManager : GenericNodeManager
    {
        /// <summary>
        /// essential to remap notified request ids from redis, also to invoke relative-request action in manager
        /// </summary>
        private Dictionary<string, KeyValuePair<Type, MethodInfo?>> supportedRedisRequestTypes = new() 
        {
            { nameof(UpsertDayRateRequest), KeyValuePair.Create(typeof(UpsertDayRateRequest), typeof(DayRateManager).GetMethod("Recover_UpsertDayRate")) },
            { nameof(GetDayRateRequest), KeyValuePair.Create(typeof(GetDayRateRequest), typeof(DayRateManager).GetMethod("Recover_GetDayRate")) },
            { nameof(GetDayRatesRequest), KeyValuePair.Create(typeof(GetDayRatesRequest), typeof(DayRateManager).GetMethod("Recover_GetDayRates")) },
            { nameof(DeleteDayRateRequest), KeyValuePair.Create(typeof(DeleteDayRateRequest), typeof(DayRateManager).GetMethod("Recover_DeleteDayRate")) },
            { nameof(CalculateDayFeeRequest), KeyValuePair.Create(typeof(CalculateDayFeeRequest), typeof(DayRateManager).GetMethod("Recover_CalculateDayFee")) },
        };
        
        #region Singleton
        private static readonly Lazy<DayRateManager> lazy =
            new Lazy<DayRateManager>(() => new DayRateManager());
        public static DayRateManager Instance { get { return lazy.Value; } }
        #endregion

        private DayRateManager()
        {
            cts = new();
            notifiedHandledRequests = new();
            nThreadsLock = new object();
            bullyElectionLock = new object();
            currentClusterCoordinatorLock = new();
        }

        public override void AskForPendingRequests()
        {
            try
            {
                Func<string, Task<KeyValuePair<string, List<NotifyHandledRequestMsg>>>> taskFactory = async (otherClusterNode) =>
                {
                    return grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                    {
                        var clusterNodeClient = GrpcClientInitializer.Instance.GetClusterNodeClient<MicroservicesProtos.DayRate.DayRateClient>(otherClusterNode);
                        return KeyValuePair.Create(
                            otherClusterNode,
                            ((MicroservicesProtos.DayRate.DayRateClient)clusterNodeClient).GetNotifiedRequests(new GenericMessages.GetNotifiedRequestsRequest()
                            {
                                SenderIP = machineIP
                            }).Requests.ToList());
                    }).Result;
                };

                var requests = RunTasksAndAggregate(otherClusterNodes, taskFactory).Result.SelectMany(kvp => kvp.Value).ToList();

                foreach (var request in requests) 
                {
                    AppendNotifiedRequest(request.RequestId, request.Expiry);
                }

            }
            catch (Exception ex)
            {
                log.Error(ex, "In AskForPendingRequests()!");
            }
        }

        public override bool NotifyHandledRequest(string requestId)
        {
            try
            {
                Func<string, Task<KeyValuePair<string, bool>>> taskFactory = async (otherClusterNode) =>
                {
                    return grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                    {
                        var clusterNodeClient = GrpcClientInitializer.Instance.GetClusterNodeClient<MicroservicesProtos.DayRate.DayRateClient>(otherClusterNode);
                        return KeyValuePair.Create(
                            otherClusterNode,
                            ((MicroservicesProtos.DayRate.DayRateClient)clusterNodeClient).NotifyHandledRequest(new GenericMessages.NotifyHandledRequestMsg()
                            {
                                RequestId = requestId,
                                Expiry = requestExpiryInMilliseconds
                            }).Result);
                    }).Result;
                };

                Task.Run(() => RunTasksAndAggregate(otherClusterNodes, taskFactory));

                return true;
            }
            catch (Exception ex)
            {
                log.Error(ex, "In NotifyHandledRequest()!");
            }

            return false;
        }

        public override async Task TraceNotifiedRequest(object? state, string requestId, int requestExpiry)
        {
            try
            {
                var requestTypeAction = supportedRedisRequestTypes[requestId.Split("@")[0]];

                await Task.Delay(requestExpiry);

                while (string.IsNullOrEmpty(currentClusterCoordinatorIp))
                    await Task.Delay(requestExpiry);

                var request = await redisCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    var method = cache.GetType().GetMethod("GetRecordAsync",
                                BindingFlags.Public | BindingFlags.Static);

                    method.MakeGenericMethod(requestTypeAction.Key);
                    return method.Invoke(null, null);
                });

                if (request != null) 
                {
                   var result = grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                                {
                                    var clusterNodeClient = GrpcClientInitializer.Instance.GetClusterNodeClient<MicroservicesProtos.DayRate.DayRateClient>(currentClusterCoordinatorIp);
                                    return ((MicroservicesProtos.DayRate.DayRateClient)clusterNodeClient).CanIHandle(new GenericMessages.CanIHandleRequest()
                                           {
                                               RequestId = requestId,
                                               Expiry = requestExpiryInMilliseconds
                                           }).Result;
                                }).Result;

                    if (result) //handle request type
                    {
                        requestTypeAction.Value.Invoke(DayRateManager.Instance, new object[] { request });
                    }
                    else //since the request isn't deleted from cache, so extend message expiry with local requestExpiryInMilliseconds
                    {
                        AppendNotifiedRequest(requestId, requestExpiryInMilliseconds);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "In TraceNotifiedRequest()!");
            }
        }

        #region CRUD
        public async Task UpsertDayRate(string requestId, LibDTO.DayRate dayRate, int delayInMilliseconds = 0)
        {
            var result = false;

            try
            {
                log.Info($"Invoked UpsertDayRate with id {dayRate.Id}");

                //apply delay
                await Task.Delay(delayInMilliseconds * 1000);

                await mongoCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    var dbDayRate = await ((DayRateDbService)dbService).GetAsync(dayRate.Id);

                    if (dbDayRate == null) //create operation
                    {
                        result = await ((DayRateDbService)dbService).CreateAsync(dayRate);
                    }
                    else //update
                    {
                        result = await ((DayRateDbService)dbService).UpdateAsync(dayRate.Id, dayRate);
                    }
                    
                });

                await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //build return/result elaborated message request to the middleware through grpc
                });

                await redisCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //if awk = true, delete request Id from cache
                    _ = cache.RemoveAsync(requestId);
                });

            }
            catch(Exception ex)
            {
                log.Error(ex, $"In UpsertDayRate(), result: {result}");
            }
        }

        public async Task GetDayRate(string requestId, int id, int delayInMilliseconds = 0)
        {
            var result = false;

            try
            {
                log.Info($"Invoked GetDayRate with id: {id}");

                //apply delay
                await Task.Delay(delayInMilliseconds * 1000);

                await mongoCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    var dbDayRate = await ((DayRateDbService)dbService).GetAsync(id.ToString());

                });

                await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //build return/result elaborated message request to the middleware through grpc
                });

                await redisCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //if awk = true, delete request Id from cache
                    _ = cache.RemoveAsync(requestId);
                });

            }
            catch (Exception ex)
            {
                log.Error(ex, $"In GetDayRate(), result: {result}");
            }
        }

        public async Task GetDayRates(string requestId, int delayInMilliseconds = 0)
        {
            var result = false;

            try
            {
                //apply delay
                await Task.Delay(delayInMilliseconds * 1000);

                await mongoCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    var dbDayRates = await ((DayRateDbService)dbService).GetAllAsync();
                });

                await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //build return/result elaborated message request to the middleware through grpc
                });

                await redisCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //if awk = true, delete request Id from cache
                    _ = cache.RemoveAsync(requestId);
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, $"In GetDayRates(), result: {result}");
            }
        }

        public async Task DeleteDayRate(string requestId, int id, int delayInMilliseconds = 0)
        {
            var result = false;

            try
            {
                //apply delay
                await Task.Delay(delayInMilliseconds * 1000);

                await mongoCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    result = await ((DayRateDbService)dbService).RemoveAsync(id.ToString());
                });

                await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //build return/result elaborated message request to the middleware through grpc
                });

                await redisCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //if awk = true, delete request Id from cache
                    _ = cache.RemoveAsync(requestId);
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, $"In DeleteDayRate(), result: {result}");
            }
        }
        #endregion

        #region fee calculation

        /// <summary>
        /// since the aim of the project is to demonstrate the coordination, fault tolerance and load balancing in a distributed system, 
        /// so the fee calculation algorithm per entity {interval, day, week} is simplified
        /// </summary>
        /// <param name="requestId"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="delayInMilliseconds"></param>
        /// <returns></returns>
        public async Task CalculateDayFee(string requestId, TimeSpan start, TimeSpan end, int delayInMilliseconds = 0)
        {
            try
            {
                //apply delay
                await Task.Delay(delayInMilliseconds * 1000);

                //to be implemented

                //build return elaborated message request to the middleware through grpc

                //if awk = true, delete request Id from cache
                cache.RemoveAsync(requestId);
            }
            catch (Exception ex)
            {
                log.Error(ex, "In CalculateDayFee()!");
            }
        }
        #endregion

        #region RecoverNotifiedRequest
        public async Task Recover_UpsertDayRate(UpsertDayRateRequest request)
        {
            await this.UpsertDayRate(request.RequestCamp.RequestId, mapper.Map<LibDTO.DayRate>(request.DayRate), request.RequestCamp.RequiredDelay);
        }

        public async Task Recover_GetDayRate(GetDayRateRequest request)
        {
            await this.GetDayRate(request.RequestCamp.RequestId, request.Id, request.RequestCamp.RequiredDelay);
        }

        public async Task Recover_GetDayRates(GetDayRatesRequest request)
        {
            await this.GetDayRates(request.RequestCamp.RequestId, request.RequestCamp.RequiredDelay);
        }

        public async Task Recover_DeleteDayRate(DeleteDayRateRequest request)
        {
            await this.DeleteDayRate(request.RequestCamp.RequestId, request.Id, request.RequestCamp.RequiredDelay);
        }

        public async Task Recover_CalculateDayFee(CalculateDayFeeRequest request)
        {
            await this.CalculateDayFee(request.RequestCamp.RequestId,
                        TimeSpan.FromSeconds(request.Start),
                        TimeSpan.FromSeconds(request.End),
                        request.RequestCamp.RequiredDelay);
        }
        #endregion

        #region Coordination
        public override async Task CanBeCoordinator_Bully()
        {
            try
            {
                log.Info($"Invoked CanBeCoordinator_Bully currentNodeId {machineIP.GetHashCode()}");

                var nodeId = machineIP.GetHashCode();

                lock (bullyElectionLock)
                {
                    Func<string, Task<KeyValuePair<string, bool>>> taskFactory = async (heigherClusterNode) =>
                    {
                        try
                        {
                            //should exclude RpcException with StatusCode.DeadlineExceeded
                            return await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                            {
                                var clusterNodeClient = GrpcClientInitializer.Instance.GetClusterNodeClient<MicroservicesProtos.DayRate.DayRateClient>(heigherClusterNode);
                                return KeyValuePair.Create(
                                    heigherClusterNode,
                                    ((MicroservicesProtos.DayRate.DayRateClient)clusterNodeClient).CanICoordinate(new GenericMessages.CanICoordinateRequest()
                                    {
                                        NodeId = nodeId
                                    }, deadline: DateTime.UtcNow.AddSeconds(8)).Result
                                );
                            });
                        }
                        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded) //if a cluster node doesn't respond in time, then consider an acceptance
                        {
                            return KeyValuePair.Create(heigherClusterNode, true);
                        }
                    };

                    var heigherClusterNodes = otherClusterNodes.Where(clusterNode => clusterNode.Replace("http://", "").GetHashCode() > machineIP.GetHashCode()).ToList();

                    //send CanICoordinate to all heigher nodes
                    var result = !RunTasksAndAggregate(heigherClusterNodes, taskFactory).Result.Any(kvp => kvp.Value == false);

                    if(result) //election won
                    {
                        StartCoordinatorActivity();
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "In CanBeCoordinator_Bully()!");
            }
        }

        public override void PingClusterNodes(Object source, ElapsedEventArgs e)
        {
            try
            {
                Func<string, Task<KeyValuePair<string, bool>>> taskFactory = async (otherClusterNode) =>
                {
                    try
                    {
                        //should exclude RpcException with StatusCode.DeadlineExceeded
                        return await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                        {
                            var clusterNodeClient = GrpcClientInitializer.Instance.GetClusterNodeClient<MicroservicesProtos.DayRate.DayRateClient>(otherClusterNode);
                            return KeyValuePair.Create(
                                otherClusterNode,
                                ((MicroservicesProtos.DayRate.DayRateClient)clusterNodeClient).IsAlive(new GenericMessages.IsAliveRequest
                                {
                                    SenderIP = machineIP
                                }, deadline: DateTime.UtcNow.AddSeconds(8)).Result
                            );
                        });
                    }
                    catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded) //if a cluster node doesn't respond in time, then consider an acceptance
                    {
                        return KeyValuePair.Create(otherClusterNode, false);
                    }
                };

                //send IsAlive to all other nodes
                var offlineNodes = RunTasksAndAggregate(otherClusterNodes, taskFactory).Result
                                    .Where(kvp => kvp.Value == false)
                                    .Select(kvp => kvp.Key)
                                    .ToList();

                log.Debug($"Detected offlineNodes [can be caused by timeout]: {string.Join(",", offlineNodes)}");
            }
            catch (Exception ex)
            {
                log.Error(ex, "In PingClusterNodes()!");
            }
        }

        public override async Task CaptureCoordinator(string coordinatorIp)
        {
            try
            {
                lock (currentClusterCoordinatorLock)
                {
                    if (isCoordinator) //conflict -coordinator shouldn't receive this message [ask cluster nodes who's the coordinator]
                    {
                        Func<string, Task<KeyValuePair<string, string>>> taskFactory = async (otherClusterNode) =>
                        {
                            return grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                            {
                                var clusterNodeClient = GrpcClientInitializer.Instance.GetClusterNodeClient<MicroservicesProtos.DayRate.DayRateClient>(otherClusterNode);
                                return KeyValuePair.Create(
                                    otherClusterNode,
                                    ((MicroservicesProtos.DayRate.DayRateClient)clusterNodeClient).GetCoordinatorIp(new GenericMessages.GetCoordinatorIpRequest
                                    {
                                       
                                    }).CoordinatorIp);
                            }).Result;
                        };

                        var majorCoordinator = RunTasksAndAggregate(otherClusterNodes, taskFactory).Result
                                                .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
                                                .Select(kvp => kvp.Value).GroupBy(v => v)
                                                .OrderByDescending(grp => grp.Count())
                                                .FirstOrDefault()?.Key ?? "";

                        //in case of received CaptureCoordinator, which means that this node isn't a coordinator anymore
                        if (majorCoordinator != machineIP)
                        {
                            StopCoordinatorActivity(coordinatorIp);
                        }
                    }
                    else if (currentClusterCoordinatorIp != null) //in case of an existing coordinator
                    {
                        currentClusterCoordinatorIp = coordinatorIp;
                        coordinatorTraceTimer.Stop();
                        coordinatorTraceTimer.Start();
                    }
                    else //first time to coordinator inactivity, so initialize its timer
                    {
                        coordinatorTraceTimer.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "In CaptureCoordinator()!");
            }
        }

        public override void CaptureInactiveCoordinator(object source, ElapsedEventArgs e)
        {
            try
            {
                log.Info($"Invoked CaptureInactiveCoordinator currentCoordinatorIp: {currentClusterCoordinatorIp}");

                if (Monitor.TryEnter(bullyElectionLock))
                {
                    CanBeCoordinator_Bully();
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "In CaptureCoordinator()!");
            }
        }

        public override bool AddClusterNode(string clusterNodeUri)
        {
            bool result = false;

            try
            {
                log.Info($"Invoked AddClusterNode clusterNodeuri: {clusterNodeUri}");

                if (!clusterNodeUri.Contains(machineIP))
                {
                    otherClusterNodes.Add(clusterNodeUri);

                    currentLoadBalancer.Update(otherClusterNodes);

                    if (isCoordinator) 
                    {
                        Func<string, Task<KeyValuePair<string, bool>>> taskFactory = async (otherClusterNode) =>
                        {
                            return grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                            {
                                var clusterNodeClient = GrpcClientInitializer.Instance.GetClusterNodeClient<MicroservicesProtos.DayRate.DayRateClient>(otherClusterNode);
                                return KeyValuePair.Create(
                                    otherClusterNode,
                                    ((MicroservicesProtos.DayRate.DayRateClient)clusterNodeClient).AddClusterNode(new GenericMessages.AddOrRemoveClusterNodeRequest()
                                    {
                                        ClusterNodeUri = clusterNodeUri
                                    }).Awk);
                            }).Result;
                        };

                        Task.Run(() => RunTasksAndAggregate(otherClusterNodes, taskFactory));
                    }

                    result = true;
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "In CaptureCoordinator()!");
            }

            return result;
        }

        public override bool RemoveClusterNode(string clusterNodeUri)
        {
            bool result = false;
            try
            {
                log.Info($"Invoked CaptureInactiveCoordinator clusterNodeuri: {clusterNodeUri}");

                if (!clusterNodeUri.Contains(machineIP))
                {
                    otherClusterNodes.Remove(clusterNodeUri);

                    currentLoadBalancer.Update(otherClusterNodes);

                    if (isCoordinator)
                    {
                        Func<string, Task<KeyValuePair<string, bool>>> taskFactory = async (otherClusterNode) =>
                        {
                            return grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                            {
                                var clusterNodeClient = GrpcClientInitializer.Instance.GetClusterNodeClient<MicroservicesProtos.DayRate.DayRateClient>(otherClusterNode);
                                return KeyValuePair.Create(
                                    otherClusterNode,
                                    ((MicroservicesProtos.DayRate.DayRateClient)clusterNodeClient).RemoveClusterNode(new GenericMessages.AddOrRemoveClusterNodeRequest()
                                    {
                                        ClusterNodeUri = clusterNodeUri
                                    }).Awk);
                            }).Result;
                        };

                        Task.Run(() => RunTasksAndAggregate(otherClusterNodes, taskFactory));
                    }
                }

                result = true;
            }
            catch (Exception ex)
            {
                log.Error(ex, "In CaptureCoordinator()!");
            }

            return false;
        }
        #endregion
    }
}
