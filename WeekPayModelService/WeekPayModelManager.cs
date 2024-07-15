using AutoMapper;
using WeekPayModelService.DbServices;
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
using System.Text.Json;
using System.Timers;
using static Google.Rpc.Context.AttributeContext.Types;

namespace WeekPayModelService
{
    public class WeekPayModelManager : GenericNodeManager
    {
        /// <summary>
        /// essential to remap notified request ids from redis, also to invoke relative-request action in manager
        /// </summary>
        private Dictionary<string, KeyValuePair<Type, MethodInfo?>> supportedRedisRequestTypes = new() 
        {
            { nameof(UpsertWeekPayModelRequest), KeyValuePair.Create(typeof(UpsertWeekPayModelRequest), typeof(WeekPayModelManager).GetMethod("Recover_UpsertWeekPayModel")) },
            { nameof(GetWeekPayModelRequest), KeyValuePair.Create(typeof(GetWeekPayModelRequest), typeof(WeekPayModelManager).GetMethod("Recover_GetWeekPayModel")) },
            { nameof(GetWeekPayModelsRequest), KeyValuePair.Create(typeof(GetWeekPayModelsRequest), typeof(WeekPayModelManager).GetMethod("Recover_GetWeekPayModels")) },
            { nameof(DeleteWeekPayModelRequest), KeyValuePair.Create(typeof(DeleteWeekPayModelRequest), typeof(WeekPayModelManager).GetMethod("Recover_DeleteWeekPayModel")) },
            { nameof(CalculateWeekFeeRequest), KeyValuePair.Create(typeof(CalculateWeekFeeRequest), typeof(WeekPayModelManager).GetMethod("Recover_CalculateWeekFee")) },
        };
        
        #region Singleton
        private static readonly Lazy<WeekPayModelManager> lazy =
            new Lazy<WeekPayModelManager>(() => new WeekPayModelManager());
        public static WeekPayModelManager Instance { get { return lazy.Value; } }
        #endregion

        private WeekPayModelManager()
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
                        var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.WeekPayModel.WeekPayModelClient>(otherClusterNode);
                        return KeyValuePair.Create(
                            otherClusterNode,
                            ((MicroservicesProtos.WeekPayModel.WeekPayModelClient)clusterNodeClient).GetNotifiedRequests(new GenericMessages.GetNotifiedRequestsRequest()
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
                        var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.WeekPayModel.WeekPayModelClient>(otherClusterNode);
                        return KeyValuePair.Create(
                            otherClusterNode,
                            ((MicroservicesProtos.WeekPayModel.WeekPayModelClient)clusterNodeClient).NotifyHandledRequest(new GenericMessages.NotifyHandledRequestMsg()
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
                                     var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.WeekPayModel.WeekPayModelClient>(currentClusterCoordinatorIp);
                                     return ((MicroservicesProtos.WeekPayModel.WeekPayModelClient)clusterNodeClient).CanIHandle(new GenericMessages.CanIHandleRequest()
                                     {
                                         RequestId = requestId,
                                         Expiry = requestExpiryInMilliseconds
                                     }).Result;
                                 }).Result;

                    if (result) //handle request type
                    {
                        requestTypeAction.Value.Invoke(WeekPayModelManager.Instance, new object[] { request });
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
        public async Task UpsertWeekPayModel(string requestId, LibDTO.WeekPayModel dayRate, int delayInMilliseconds = 0)
        {
            var result = false;

            try
            {
                log.Info($"Invoked UpsertWeekPayModel with id {dayRate.Id}");

                //apply delay
                await Task.Delay(delayInMilliseconds);

                var res = await mongoCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    var dbWeekPayModel = await ((WeekPayModelDbService)dbService).GetAsync(dayRate.Id);

                    if (dbWeekPayModel == null) //create
                    {
                        result = await ((WeekPayModelDbService)dbService).CreateAsync(dayRate);
                    }
                    else //update
                    {
                        result = await ((WeekPayModelDbService)dbService).UpdateAsync(dayRate.Id, dayRate);
                    }

                    return result;
                    
                });

                var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //build return/result elaborated message request to the middleware through grpc
                    return middlewareGrpcClient.NotifyProcessedRequest(new MiddlewareProtos.NotifyProcessedRequestMessage()
                    {
                        RequestId = GetRequestId(requestId),
                        ResponseType = nameof(UpsertWeekPayModelResponse),
                        ResponseJson = JsonSerializer.Serialize(new UpsertWeekPayModelResponse()
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
                log.Error(ex, $"In UpsertWeekPayModel(), result: {result}");
            }
        }

        public async Task GetWeekPayModel(string requestId, int id, int delayInMilliseconds = 0)
        {
            var result = false;

            try
            {
                log.Info($"Invoked GetWeekPayModel with id: {id}");

                //apply delay
                await Task.Delay(delayInMilliseconds);

                var dbWeekPayModel = await mongoCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    return await ((WeekPayModelDbService)dbService).GetAsync(id.ToString());
                });

                var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //build return/result elaborated message request to the middleware through grpc
                    return middlewareGrpcClient.NotifyProcessedRequest(new MiddlewareProtos.NotifyProcessedRequestMessage()
                    {
                        RequestId = GetRequestId(requestId),
                        ResponseType = nameof(GetWeekPayModelResponse),
                        ResponseJson = JsonSerializer.Serialize(new GetWeekPayModelResponse()
                        {
                            Result = new OperationResult()
                            {
                                RequestId = GetRequestId(requestId),
                                Elaborated = (dbWeekPayModel != null)
                            },
                            WeekPayModel = (dbWeekPayModel != null)? mapper.Map<WeekPayModelType>(dbWeekPayModel) : null
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
                log.Error(ex, $"In GetWeekPayModel(), result: {result}");
            }
        }

        public async Task GetWeekPayModels(string requestId, int delayInMilliseconds = 0)
        {
            var result = false;

            try
            {
                //apply delay
                await Task.Delay(delayInMilliseconds);

                var dayRates = await mongoCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    return await ((WeekPayModelDbService)dbService).GetAllAsync();
                });

                var getWeekPayModelsResponse = new GetWeekPayModelsResponse()
                {
                    Result = new OperationResult()
                    {
                        RequestId = GetRequestId(requestId),
                        Elaborated = (dayRates != null)
                    }
                };

                if(dayRates != null) 
                    dayRates.ForEach(dbWeekPayModel => mapper.Map<WeekPayModelType>(dbWeekPayModel));

                var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //build return/result elaborated message request to the middleware through grpc
                    return middlewareGrpcClient.NotifyProcessedRequest(new MiddlewareProtos.NotifyProcessedRequestMessage()
                    {
                        RequestId = GetRequestId(requestId),
                        ResponseType = nameof(GetWeekPayModelsResponse),
                        ResponseJson = JsonSerializer.Serialize(getWeekPayModelsResponse)

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
                log.Error(ex, $"In GetWeekPayModels(), result: {result}");
            }
        }

        public async Task DeleteWeekPayModel(string requestId, int id, int delayInMilliseconds = 0)
        {
            var result = false;

            try
            {
                //apply delay
                await Task.Delay(delayInMilliseconds);

                var res = await mongoCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    return await ((WeekPayModelDbService)dbService).RemoveAsync(id.ToString());
                });

                var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    //build return/result elaborated message request to the middleware through grpc
                    return middlewareGrpcClient.NotifyProcessedRequest(new MiddlewareProtos.NotifyProcessedRequestMessage()
                    {
                        RequestId = GetRequestId(requestId),
                        ResponseType = nameof(DeleteWeekPayModelResponse),
                        ResponseJson = JsonSerializer.Serialize(new DeleteWeekPayModelResponse()
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
                log.Error(ex, $"In DeleteWeekPayModel(), result: {result}");
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
        public async Task CalculateWeekFee(string requestId, DateTime start, DateTime end, int delayInMilliseconds = 0)
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
                        ResponseJson = JsonSerializer.Serialize(new CalculateWeekFeeResponse()
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
        public async Task Recover_UpsertWeekPayModel(UpsertWeekPayModelRequest request)
        {
            await this.UpsertWeekPayModel(request.RequestCamp.RequestId, mapper.Map<LibDTO.WeekPayModel>(request.WeekPayModel), request.RequestCamp.RequiredDelay);
        }

        public async Task Recover_GetWeekPayModel(GetWeekPayModelRequest request)
        {
            await this.GetWeekPayModel(request.RequestCamp.RequestId, request.Id, request.RequestCamp.RequiredDelay);
        }

        public async Task Recover_GetWeekPayModels(GetWeekPayModelsRequest request)
        {
            await this.GetWeekPayModels(request.RequestCamp.RequestId, request.RequestCamp.RequiredDelay);
        }

        public async Task Recover_DeleteWeekPayModel(DeleteWeekPayModelRequest request)
        {
            await this.DeleteWeekPayModel(request.RequestCamp.RequestId, request.Id, request.RequestCamp.RequiredDelay);
        }

        public async Task Recover_CalculateWeekFee(CalculateDayFeeRequest request)
        {
            await this.CalculateWeekFee(request.RequestCamp.RequestId,
                        DateTime.FromOADate(request.Start),
                        DateTime.FromOADate(request.End),
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
                            var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.WeekPayModel.WeekPayModelClient>(higherClusterNode);
                            return KeyValuePair.Create(
                                higherClusterNode,
                                ((MicroservicesProtos.WeekPayModel.WeekPayModelClient)clusterNodeClient).CanICoordinate(new CanICoordinateRequest()
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

                var higherClusterNodes = otherClusterNodes.Where(clusterNode => clusterNode.Replace("http://", "").GetHashCode() > machineIP.GetHashCode()).ToList();

                //send CanICoordinate to all higher nodes
                var results = await RunTasksAndAggregate(higherClusterNodes, taskFactory);
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
                            var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.WeekPayModel.WeekPayModelClient>(otherClusterNode);
                            return KeyValuePair.Create(
                                otherClusterNode,
                                ((MicroservicesProtos.WeekPayModel.WeekPayModelClient)clusterNodeClient).IsAlive(new GenericMessages.IsAliveRequest
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
                            var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.WeekPayModel.WeekPayModelClient>(otherClusterNode);
                            return KeyValuePair.Create(
                                otherClusterNode,
                                ((MicroservicesProtos.WeekPayModel.WeekPayModelClient)clusterNodeClient).GetCoordinatorIp(new GenericMessages.GetCoordinatorIpRequest
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
                                var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.WeekPayModel.WeekPayModelClient>(otherClusterNode);
                                return KeyValuePair.Create(
                                    otherClusterNode,
                                    ((MicroservicesProtos.WeekPayModel.WeekPayModelClient)clusterNodeClient).AddClusterNode(new GenericMessages.AddOrRemoveClusterNodeRequest()
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
                        var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.WeekPayModel.WeekPayModelClient>(clusterNodeUri);
                        return ((MicroservicesProtos.WeekPayModel.WeekPayModelClient)clusterNodeClient).NotifyNodePresence(new GenericMessages.NotifyNodePresenceRequest()
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
                                var clusterNodeClient = GrpcClientInitializer.Instance.GetNodeClient<MicroservicesProtos.WeekPayModel.WeekPayModelClient>(otherClusterNode);
                                return KeyValuePair.Create(
                                    otherClusterNode,
                                    ((MicroservicesProtos.WeekPayModel.WeekPayModelClient)clusterNodeClient).RemoveClusterNode(new GenericMessages.AddOrRemoveClusterNodeRequest()
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
