using AutoMapper;
using DayRateService.DbServices;
using Grpc.Core;
using LibDTO;
using LibDTO.Generic;
using LibHelpers;
using MicroservicesProtos;
using System.Net;
using System.Net.Sockets;
using System.Timers;

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
        public async Task NotifyHandledRequest(string requestId)
        {
            try
            {
                foreach (var clusterNode in otherClusterNodes)
                {
                    var clusterNodeClient = GrpcClientInitializer.Instance.GetClusterNodeClient<MicroservicesProtos.DayRate.DayRateClient>(clusterNode);
                    ((MicroservicesProtos.DayRate.DayRateClient)clusterNodeClient).NotifyHandledRequest(new GenericMessages.NotifyHandledRequestMsg()
                    {
                        RequestId = requestId,
                        Expiry = requestExpiryInMilliseconds
                    });
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "In NotifyHandledRequest()!");
            }
        }

        /// <summary>
        /// uses notifiedRequestsQueue to trace remote handled requests in the same cluster
        /// </summary>
        public override void QueuePolling()
        {
            try
            {
                //to be implemented
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
                Task.Delay(delayInMilliseconds * 1000);

                var dbDayRate = await ((DayRateDbService)dbService).GetAsync(dayRate.Id);

                if (dbDayRate == null) //create operation
                {
                    result = await ((DayRateDbService)dbService).CreateAsync(dayRate);
                }
                else //update
                {
                    result = await ((DayRateDbService)dbService).UpdateAsync(dayRate.Id, dayRate);
                }

                //build return/result elaborated message request to the middleware through grpc


                //if awk = true, delete request Id from cache
                cache.RemoveAsync(requestId);
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
                Task.Delay(delayInMilliseconds * 1000);

                var dbDayRate = await ((DayRateDbService)dbService).GetAsync(id.ToString());

                //build return/result elaborated message request to the middleware through grpc

                //if awk = true, delete request Id from cache
                cache.RemoveAsync(requestId);
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
                Task.Delay(delayInMilliseconds * 1000);

                var dbDayRates = await ((DayRateDbService)dbService).GetAllAsync();

                //build return elaborated message request to the middleware through grpc

                //if awk = true, delete request Id from cache
                cache.RemoveAsync(requestId);
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
                Task.Delay(delayInMilliseconds * 1000);

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
        public async Task CalculateDayFee(string requestId, TimeSpan start, TimeSpan end, double delayInMilliseconds = 0)
        {
            try
            {
                //to be implemented
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
                    CanBeCoordinator_Bully();

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
                    Func<string, Task<bool>> taskFactory = async (heigherClusterNode) =>
                    {
                        try
                        {
                            var clusterNodeClient = GrpcClientInitializer.Instance.GetClusterNodeClient<MicroservicesProtos.DayRate.DayRateClient>(heigherClusterNode);
                            return ((MicroservicesProtos.DayRate.DayRateClient)clusterNodeClient).CanICoordinate(new GenericMessages.CanICoordinateRequest()
                            {
                                NodeId = nodeId
                            }, deadline: DateTime.UtcNow.AddSeconds(8)).Result;
                        }
                        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded) //if a cluster node doesn't respond in time, then consider an acceptance
                        {
                            return true;
                        }
                    };

                    //send CanICoordinate to all heigher nodes
                    var result = !RunTasksAndAggregate(heigherClusterNodes, taskFactory).Result.Contains(false);

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
                StartClusterNodesPinging();

                lock (currentClusterCoordinatorLock)
                {
                    currentClusterCoordinatorIp = null;

                    //stop coordinator tracer timer
                    coordinatorTraceTimer.Stop();
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "In StartCoordinatorActivity()!");
            }

            return result;
        }

        private async Task StartClusterNodesPinging()
        {
            try
            {
                //to be implemented
            }
            catch (Exception ex)
            {
                log.Error(ex, "In StartClusterNodesPinging()!");
            }
        }

        public async Task CaptureCoordinator(string coordinatorIp)
        {
            try
            {
                lock (currentClusterCoordinatorLock)
                {
                    if (currentClusterCoordinatorIp != null)
                    {
                        currentClusterCoordinatorIp = coordinatorIp;
                        coordinatorTraceTimer.Stop();
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
        #endregion
    }
}
