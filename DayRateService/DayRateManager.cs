using AutoMapper;
using DayRateService.DbServices;
using LibDTO;
using LibHelpers;
using MicroservicesProtos;
using System.Net;
using System.Net.Sockets;

namespace DayRateService
{
    public class DayRateManager
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();
        private int nThreads;
        private object nThreadsLock;
        private int requestExpiryInMilliseconds;
        private string middlewareEndpoint; //will be used to create a grpc client in case of cluster node
        private List<string> otherClusterNodes;

        private DayRateDbService dayRateDbService;

        /// <summary>
        /// referenced in case of updating cluster nodes: currentLoadBalancer.Update()
        /// used to inform the coordinator about node removing or adding
        /// </summary>
        private CustomLoadBalancerProxyProvider currentLoadBalancer; 

        #region Singleton
        private static readonly Lazy<DayRateManager> lazy =
            new Lazy<DayRateManager>(() => new DayRateManager());
        public static DayRateManager Instance { get { return lazy.Value; } }
        #endregion

        private CancellationTokenSource cts;

        /// <summary>
        /// all notified request ids from external nodes
        /// </summary>
        private Queue<KeyValuePair<string, KeyValuePair<DateTime, double>>> notifiedRequestsQueue;

        private DayRateManager() 
        {
            cts = new();
            notifiedRequestsQueue = new();
            nThreadsLock = new object();
        }

        /// <summary>
        /// when the manager of microservice is up it'll request a copy of ids of all pending requests and it will consider
        /// the default timeout to start handling these requests
        /// </summary>
        /// <param name="customLBPP"></param>
        internal void Init(CustomLoadBalancerProxyProvider customLBPP,
            DayRateDbService dayRateDbService,
            List<string> clusterNodes,
            string middlewareEndpoint,
            int maxThreads,
            int requestExpiryInMilliseconds)
        {
            try 
            {
                var machineIP = Dns.GetHostByName(Dns.GetHostName()).AddressList.First(address => address.AddressFamily == AddressFamily.InterNetwork).ToString();

                log.Info($"Invoked Init, machineIp: {machineIP}");
            
                this.currentLoadBalancer = customLBPP;
                this.dayRateDbService = dayRateDbService;
                otherClusterNodes = clusterNodes.Where(clusterNode => !clusterNode.Contains(machineIP)).ToList();
                this.middlewareEndpoint = middlewareEndpoint;
                nThreads = maxThreads;
                this.requestExpiryInMilliseconds = requestExpiryInMilliseconds;

                //init cluster nodes clients
                GrpcClientInitializer.Instance.InitializeClusterGrpcClients<MicroservicesProtos.DayRate.DayRateClient>(otherClusterNodes);

                QueuePolling();
            }
            catch (Exception ex)
            {
                log.Error(ex, "In AppendNotifiedRequest()!");
            }
        }

        /// <summary>
        /// take into account external request handling
        /// </summary>
        /// <param name="requestId"></param>
        /// <param name="requestExpiry">estimated expiry in seconds, declared by the node that invoked this method</param>
        /// <returns></returns>
        public bool AppendNotifiedRequest(string requestId, double requestExpiry)
        {
            try
            {

                this.notifiedRequestsQueue.Enqueue(KeyValuePair.Create(
                        requestId, 
                        KeyValuePair.Create(DateTime.UtcNow, requestExpiry))
                    );
                return true;
            }
            catch (Exception ex)
            {
                log.Error(ex, "In AppendNotifiedRequest()!");
            }

            return false;
        }

        /// <summary>
        /// notifies all cluster nodes of handled requests
        /// </summary>
        /// <returns></returns>
        public bool NotifyHandledRequest(string requestId)
        {
            try
            {
                //to be implemented
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
        public void QueuePolling()
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

                var dbDayRate = await dayRateDbService.GetAsync(dayRate.Id);

                if (dbDayRate == null) //create operation
                {
                    result = await dayRateDbService.CreateAsync(dayRate);
                }
                else //update
                {
                    result = await dayRateDbService.UpdateAsync(dayRate.Id, dayRate);
                }

                //build return/result elaborated message request to the middleware through grpc
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

                var dbDayRate = await dayRateDbService.GetAsync(id.ToString());

                //build return/result elaborated message request to the middleware through grpc
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

                var dbDayRates = await dayRateDbService.GetAllAsync();

                //build return elaborated message request to the middleware through grpc
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

                result = await dayRateDbService.RemoveAsync(id.ToString());

                //build return elaborated message request to the middleware through grpc
            }
            catch (Exception ex)
            {
                log.Error(ex, $"In DeleteDayRate(), result: {result}");
            }
        }

        #endregion

        #region fee calculation
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
    }
}
