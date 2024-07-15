using AutoMapper;
using DayRateService.DbServices;
using GenericMessages;
using Grpc.Core;
using LibDTO;
using LibDTO.Generic;
using LibHelpers;
using MicroservicesProtos;
using Microsoft.AspNetCore.Authentication;
using StackExchange.Redis;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
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
            notifiedHandledRequests = new();
            nThreadsLock = new object();
            bullyElectionLockSem = new SemaphoreSlim(1, 1);
            currentClusterCoordinatorSem = new SemaphoreSlim(1, 1);
            clusterNodesSem = new SemaphoreSlim(1, 1);
        }

        public override async Task AskForPendingRequests()
        {
            try
            {
                log.Info("Invoked AskForPendingRequests");

                Func<string, Task<KeyValuePair<string, List<NotifyHandledRequestMsg>>>> taskFactory = async (otherClusterNode) =>
                {
                    return grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                    {
                        var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.DayRate.DayRateClient>(otherClusterNode);
                        return KeyValuePair.Create(
                            otherClusterNode,
                            ((MicroservicesProtos.DayRate.DayRateClient)clusterNodeClient).GetNotifiedRequests(new GenericMessages.GetNotifiedRequestsRequest()
                            {
                                SenderIP = machineIP
                            }).Requests.ToList());
                    }).Result;
                };

                //ask for notified requests from all cluster nodes
                var results = await RunTasksAndAggregate(otherClusterNodes, taskFactory);
                var requests = results.SelectMany(kvp => kvp.Value).ToList();

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
                        var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.DayRate.DayRateClient>(otherClusterNode);
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
                await Task.Delay(requestExpiry);

                while (string.IsNullOrEmpty(currentClusterCoordinatorIp))
                    await Task.Delay(requestExpiry);

                var requestTypeAction = supportedRedisRequestTypes[requestId.Split("@")[0]];
                var request = await redisCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    var x = typeof(DistributedCacheHelper).GetMethods();
                    var method = typeof(DistributedCacheHelper).GetMethod("GetRecordAsync",
                                 BindingFlags.Public | BindingFlags.Static);

                    method = method.MakeGenericMethod(requestTypeAction.Key);
                    var task = (Task) method.Invoke(null, new object[] { cache, requestId });
                    await task.ConfigureAwait(false);

                    var resultProperty = task.GetType().GetProperty("Result");
                    return resultProperty.GetValue(task);
                });

                if (request != null) 
                {
                    var result = grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                                 {
                                     var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.DayRate.DayRateClient>(currentClusterCoordinatorIp);
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
                    else //since the request isn't deleted from cache, so extend message expiry with the same requestExpiry
                    {
                        AppendNotifiedRequest(requestId, requestExpiry);
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
                await Task.Delay(delayInMilliseconds);

                var res = await mongoCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    var dbDayRate = await ((DayRateDbService)dbService).GetAsync(dayRate.Id);

                    if (dbDayRate == null) //create
                    {
                        result = await ((DayRateDbService)dbService).CreateAsync(dayRate);
                    }
                    else //update
                    {
                        result = await ((DayRateDbService)dbService).UpdateAsync(dayRate.Id, dayRate);
                    }

                    return result;
                    
                });

                var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //build return/result elaborated message request to the middleware through grpc
                    return middlewareGrpcClient.NotifyProcessedRequest(new MiddlewareProtos.NotifyProcessedRequestMessage()
                    {
                        RequestId = GetRequestId(requestId),
                        ResponseType = nameof(UpsertDayRateResponse),
                        ResponseJson = JsonSerializer.Serialize(new UpsertDayRateResponse()
                        {
                            Result = new OperationResult()
                            {
                                RequestId = GetRequestId(requestId),
                                Elaborated = res
                            }
                        })

                    });
                });

                //if Result = true, delete request Id from cache
                if (response.Result)
                {
                    await redisCircuitRetryPolicy.ExecuteAsync(async () =>
                    {
                        _ = cache.RemoveAsync(requestId);
                    });

                    log.Info($"Processed requestId: {requestId}");
                }
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
                await Task.Delay(delayInMilliseconds);

                var dbDayRate = await mongoCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    return await ((DayRateDbService)dbService).GetAsync(id.ToString());
                });

                var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //build return/result elaborated message request to the middleware through grpc
                    return middlewareGrpcClient.NotifyProcessedRequest(new MiddlewareProtos.NotifyProcessedRequestMessage()
                    {
                        RequestId = GetRequestId(requestId),
                        ResponseType = nameof(GetDayRateResponse),
                        ResponseJson = JsonSerializer.Serialize(new GetDayRateResponse()
                        {
                            Result = new OperationResult()
                            {
                                RequestId = GetRequestId(requestId),
                                Elaborated = (dbDayRate != null)
                            },
                            DayRate = (dbDayRate != null)? mapper.Map<DayRateType>(dbDayRate) : null
                        })

                    });
                });

                //if Result = true, delete request Id from cache
                if (response.Result)
                {
                    await redisCircuitRetryPolicy.ExecuteAsync(async () =>
                    {
                        _ = cache.RemoveAsync(requestId);
                    });
                }

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
                await Task.Delay(delayInMilliseconds);

                var dayRates = await mongoCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    return await ((DayRateDbService)dbService).GetAllAsync();
                });

                var getDayRatesResponse = new GetDayRatesResponse()
                {
                    Result = new OperationResult()
                    {
                        RequestId = GetRequestId(requestId),
                        Elaborated = (dayRates != null)
                    }
                };

                if(dayRates != null) 
                    dayRates.ForEach(dbDayRate => mapper.Map<DayRateType>(dbDayRate));

                var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //build return/result elaborated message request to the middleware through grpc
                    return middlewareGrpcClient.NotifyProcessedRequest(new MiddlewareProtos.NotifyProcessedRequestMessage()
                    {
                        RequestId = GetRequestId(requestId),
                        ResponseType = nameof(GetDayRatesResponse),
                        ResponseJson = JsonSerializer.Serialize(getDayRatesResponse)

                    });
                });

                //if Result = true, delete request Id from cache
                if (response.Result)
                {
                    await redisCircuitRetryPolicy.ExecuteAsync(async () =>
                    {
                        _ = cache.RemoveAsync(requestId);
                    });
                }
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
                await Task.Delay(delayInMilliseconds);

                var res = await mongoCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    return await ((DayRateDbService)dbService).RemoveAsync(id.ToString());
                });

                var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //build return/result elaborated message request to the middleware through grpc
                    return middlewareGrpcClient.NotifyProcessedRequest(new MiddlewareProtos.NotifyProcessedRequestMessage()
                    {
                        RequestId = GetRequestId(requestId),
                        ResponseType = nameof(DeleteDayRateResponse),
                        ResponseJson = JsonSerializer.Serialize(new DeleteDayRateResponse()
                        {
                            Result = new OperationResult()
                            {
                                RequestId = GetRequestId(requestId),
                                Elaborated = res
                            }
                        })

                    });
                });

                //if Result = true, delete request Id from cache
                if (response.Result)
                {
                    await redisCircuitRetryPolicy.ExecuteAsync(async () =>
                    {
                        _ = cache.RemoveAsync(requestId);
                    });
                }
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
                await Task.Delay(delayInMilliseconds);

                //to be implemented

                var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //build return/result elaborated message request to the middleware through grpc
                    return middlewareGrpcClient.NotifyProcessedRequest(new MiddlewareProtos.NotifyProcessedRequestMessage()
                    {
                        RequestId = GetRequestId(requestId),
                        ResponseType = nameof(DeleteWeekPayModelResponse),
                        ResponseJson = JsonSerializer.Serialize(new CalculateDayFeeResponse()
                        {
                            Result = new OperationResult()
                            {
                                RequestId = GetRequestId(requestId),
                                Elaborated = true
                            },
                            Fee = 1
                        })

                    });
                });

                //if Result = true, delete request Id from cache
                if (response.Result)
                {
                    await redisCircuitRetryPolicy.ExecuteAsync(async () =>
                    {
                        _ = cache.RemoveAsync(requestId);
                    });
                }
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
                var nodeId = machineIP.GetHashCode();

                log.Info($"Invoked CanBeCoordinator_Bully currentNodeId {nodeId}");

                //wait and acquire semaphore
                await bullyElectionLockSem.WaitAsync();

                Func<string, Task<KeyValuePair<string, bool>>> taskFactory = async (higherClusterNode) =>
                {
                    try
                    {
                        //should exclude RpcException with StatusCode.DeadlineExceeded
                        return await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                        {
                            var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.DayRate.DayRateClient>(higherClusterNode);
                            return KeyValuePair.Create(
                                higherClusterNode,
                                ((MicroservicesProtos.DayRate.DayRateClient)clusterNodeClient).CanICoordinate(new CanICoordinateRequest()
                                {
                                    NodeId = nodeId
                                }, deadline: DateTime.UtcNow.AddSeconds(8)).Result
                            );
                        });
                    }
                    catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded) //if a cluster node doesn't respond in time, then consider an acceptance
                    {
                        return KeyValuePair.Create(higherClusterNode, true);
                    }
                };

                Regex ipRegex = new Regex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
                var higherClusterNodes = otherClusterNodes.Where(clusterNode => ipRegex.Matches(clusterNode)[0].GetHashCode() > machineIP.GetHashCode()).ToList();

                //send CanICoordinate to all higher nodes
                var results = await RunTasksAndAggregate(higherClusterNodes, taskFactory);

                log.Info("Coordinator election results: " + string.Join(", ", higherClusterNodes.Zip(results).Select(item => item.Second).ToList()));

                var result = !results.Any(kvp => kvp.Value == false);

                if (result) //election won
                {
                    await StartCoordinatorActivity();
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "In CanBeCoordinator_Bully()!");
            }
            finally //always release semaphore
            {
                bullyElectionLockSem.Release();
            }
        }

        public override async void PingClusterNodes(Object source, ElapsedEventArgs e)
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
                            var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.DayRate.DayRateClient>(otherClusterNode);
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
                var responses = await RunTasksAndAggregate(otherClusterNodes, taskFactory);
                var offlineNodes = responses.Where(kvp => kvp.Value == false)
                                    .Select(kvp => kvp.Key)
                                    .ToList();

                if(offlineNodes.Count > 0)
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
                //wait and acquire semaphore
                await currentClusterCoordinatorSem.WaitAsync();

                if (isCoordinator) //conflict -coordinator shouldn't receive this message [ask cluster nodes who's the coordinator]
                {
                    Func<string, Task<KeyValuePair<string, string>>> taskFactory = async (otherClusterNode) =>
                    {
                        return grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                        {
                            var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.DayRate.DayRateClient>(otherClusterNode);
                            return KeyValuePair.Create(
                                otherClusterNode,
                                ((MicroservicesProtos.DayRate.DayRateClient)clusterNodeClient).GetCoordinatorIp(new GenericMessages.GetCoordinatorIpRequest
                                {
                                       
                                }).CoordinatorIp);
                        }).Result;
                    };
                    var results = await RunTasksAndAggregate(otherClusterNodes, taskFactory);
                    var majorCoordinator = results.Where(kvp => !string.IsNullOrEmpty(kvp.Value))
                                            .Select(kvp => kvp.Value).GroupBy(v => v)
                                            .OrderByDescending(grp => grp.Count())
                                            .FirstOrDefault()?.Key ?? "";

                    //in case of received CaptureCoordinator, which means that this node isn't a coordinator anymore
                    if (majorCoordinator != machineIP)
                    {
                        log.Info($"Detected majorCoordinator: {majorCoordinator}, while considered coordinator with machineIP: {machineIP}");
                        StopCoordinatorActivity(coordinatorIp);
                    }
                }
                else
                {
                    currentClusterCoordinatorIp = coordinatorIp;
                    coordinatorTraceTimer.Stop();
                    coordinatorTraceTimer.Start();
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "In CaptureCoordinator()!");
            }
            finally //always release semaphore
            {
                currentClusterCoordinatorSem.Release();
            }
        }

        public override async void CaptureInactiveCoordinator(object source, ElapsedEventArgs e)
        {
            try
            {
                log.Info($"Invoked CaptureInactiveCoordinator currentCoordinatorIp: {currentClusterCoordinatorIp}");

                if (bullyElectionLockSem.CurrentCount > 0)
                {
                    await CanBeCoordinator_Bully();
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "In CaptureCoordinator()!");
            }
        }

        public async override Task<bool> AddClusterNode(string clusterNodeUri)
        {
            bool result = false;

            try
            {
                log.Info($"Invoked AddClusterNode clusterNodeuri: {clusterNodeUri}");

                if (!clusterNodeUri.Contains(machineIP))
                {
                    await clusterNodesSem.WaitAsync();
                    otherClusterNodes.Add(clusterNodeUri);

                    currentLoadBalancer.Update(otherClusterNodes);
                    clusterNodesSem.Release();

                    if (isCoordinator) 
                    {
                        Func<string, Task<KeyValuePair<string, bool>>> taskFactory = async (otherClusterNode) =>
                        {
                            return grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                            {
                                var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.DayRate.DayRateClient>(otherClusterNode);
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

                    //notify the new node of the current node presence
                    var res = grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                    {
                        var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.DayRate.DayRateClient>(clusterNodeUri);
                        return ((MicroservicesProtos.DayRate.DayRateClient)clusterNodeClient).NotifyNodePresence(new GenericMessages.NotifyNodePresenceRequest()
                        {
                            NodeUri = this.machineIP + ":" + 80 //default port!
                        }).Awk;
                    });

                    result = true;
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "In CaptureCoordinator()!");
            }

            return result;
        }

        public async override Task<bool> RemoveClusterNode(string clusterNodeUri)
        {
            bool result = false;
            try
            {
                log.Info($"Invoked CaptureInactiveCoordinator clusterNodeuri: {clusterNodeUri}");

                if (!clusterNodeUri.Contains(machineIP))
                {
                    await clusterNodesSem.WaitAsync();
                    otherClusterNodes.Remove(clusterNodeUri);

                    currentLoadBalancer.Update(otherClusterNodes);
                    clusterNodesSem.Release();

                    if (isCoordinator)
                    {
                        Func<string, Task<KeyValuePair<string, bool>>> taskFactory = async (otherClusterNode) =>
                        {
                            return grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                            {
                                var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.DayRate.DayRateClient>(otherClusterNode);
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

        public async Task<bool> AppendPresentClusterNode(string clusterNodeUri)
        {
            bool result = false;

            try
            {
                log.Info($"Invoked AppendPresentClusterNode clusterNodeUri: {clusterNodeUri}");

                await clusterNodesSem.WaitAsync();
                otherClusterNodes.Add(clusterNodeUri);

                currentLoadBalancer.Update(otherClusterNodes);
                clusterNodesSem.Release();

                result = true;
            }
            catch (Exception ex)
            {
                log.Error(ex, "In AppendPresentClusterNode()!");
            }

            return result;
        }
        #endregion
    }
}
