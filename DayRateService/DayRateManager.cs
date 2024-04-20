using AutoMapper;
using DayRateService.DbServices;
using Grpc.Core;
using LibDTO;
using LibDTO.Generic;
using LibHelpers;
using MicroservicesProtos;
using Microsoft.AspNetCore.Authentication;
using System.Net;
using System.Net.Sockets;
using System.Timers;
using static Google.Rpc.Context.AttributeContext.Types;

namespace DayRateService
{
    public class DayRateManager : GenericNodeManager
    {
        #region Singleton
        private static readonly Lazy<DayRateManager> lazy =
            new Lazy<DayRateManager>(() => new DayRateManager());
        public static DayRateManager Instance { get { return lazy.Value; } }
        #endregion

        private DayRateManager()
        {
            cts = new();
            notifiedRequestsQueue = new();
            nThreadsLock = new object();
            bullyElectionLock = new object();
        }

        /// <summary>
        /// notifies all cluster nodes of handled requests
        /// </summary>
        /// <returns></returns>
        public bool NotifyHandledRequest(string requestId)
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

                _ = RunTasksAndAggregate(otherClusterNodes, taskFactory);

                return true;
            }
            catch (Exception ex)
            {
                log.Error(ex, "In NotifyHandledRequest()!");
            }

            return false;
        }

        /// <summary>
        /// uses notifiedRequestsQueue to trace remote handled requests in the same cluster
        /// </summary>
        public override void QueuePolling()
        {
            try
            {
                ThreadPool.QueueUserWorkItem()
            }
            catch (Exception ex)
            {
                log.Error(ex, "In QueuePolling()!");
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

                await redisCircuitRetryPolicy.ExecuteAsync(async () =>
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

                    await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                    {
                        //build return/result elaborated message request to the middleware through grpc
                    });

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

                await redisCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    var dbDayRate = await ((DayRateDbService)dbService).GetAsync(id.ToString());

                    await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                    {
                        //build return/result elaborated message request to the middleware through grpc
                    });

                    //if awk = true, delete request Id from cache
                    cache.RemoveAsync(requestId);
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

                var dbDayRates = await ((DayRateDbService)dbService).GetAllAsync();

                await redisCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                    {
                        //build return/result elaborated message request to the middleware through grpc
                    });

                    //if awk = true, delete request Id from cache
                    cache.RemoveAsync(requestId);
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

                result = await ((DayRateDbService)dbService).RemoveAsync(id.ToString());

                //build return elaborated message request to the middleware through grpc

                //if awk = true, delete request Id from cache
                cache.RemoveAsync(requestId);
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

        #region Coordination
        /// <summary>
        /// should be assured that no another election is in progress
        /// </summary>
        /// <param name="anotherNodeId"></param>
        /// <returns></returns>
        public bool CheckIfNodeIdHigher(int anotherNodeId)
        {
            try
            {
                log.Info($"Invoked CanBeCoordinator_Bully anotherNodeId: {anotherNodeId}, currentNodeId {machineIP.GetHashCode()}");

                var result = Monitor.TryEnter(bullyElectionLock) && anotherNodeId < machineIP.GetHashCode();

                if (result)
                    _ = CanBeCoordinator_Bully();

                return !result;
            }
            catch (Exception ex)
            {
                log.Error(ex, "In CheckIfNodeIdHigher()!");
            }

            return false;
        }

        /// <summary>
        /// checks if no other heigher nodes are active; if so, then declare this node as coordinator
        /// </summary>
        public async Task CanBeCoordinator_Bully()
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

        public bool StartCoordinatorActivity()
        {
            bool result = false;

            try
            {
                log.Info("Invoked StartCoordinatorActivity");

                lock (currentClusterCoordinatorLock)
                {
                    isCoordinator = true;
                    currentClusterCoordinatorIp = null;

                    //stop coordinator tracer timer
                    coordinatorTraceTimer.Stop();
                }

                InitPingingTimer(PingClusterNodes);
            }
            catch (Exception ex)
            {
                log.Error(ex, "In StartCoordinatorActivity()!");
            }

            return result;
        }

        /// <summary>
        /// in case of new coordinator elected and this node is considered as a time-out coordinator
        /// </summary>
        /// <param name="currentCoordinator"></param>
        /// <returns></returns>
        public bool StopCoordinatorActivity(string currentCoordinator)
        {
            bool result = false;

            try
            {
                log.Info("Invoked StopCoordinatorActivity");

                lock (currentClusterCoordinatorLock)
                {
                    isCoordinator = false;
                    currentClusterCoordinatorIp = currentCoordinator;

                    //start coordinator tracer timer
                    coordinatorTraceTimer.Start();
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "In StopCoordinatorActivity()!");
            }

            return result;
        }

        private void PingClusterNodes(Object source, ElapsedEventArgs e)
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

        public async Task CaptureCoordinator(string coordinatorIp)
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
                            currentClusterCoordinatorIp = majorCoordinator;
                            clusterNodesTimer.Stop();
                            InitCoordinatorInactivityTimer(CaptureInactiveCoordinator);
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
                        InitCoordinatorInactivityTimer(CaptureInactiveCoordinator);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "In CaptureCoordinator()!");
            }
        }

        public void CaptureInactiveCoordinator(object source, ElapsedEventArgs e)
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

                        _ = RunTasksAndAggregate(otherClusterNodes, taskFactory);
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

                otherClusterNodes.Remove(clusterNodeUri);

                currentLoadBalancer.Update(otherClusterNodes);

                if (!clusterNodeUri.Contains(machineIP))
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

                    _ = RunTasksAndAggregate(otherClusterNodes, taskFactory);
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
