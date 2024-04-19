using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using LibHelpers;
using Microsoft.Extensions.Caching.Distributed;
using System.Timers;

namespace LibDTO.Generic
{
    public abstract class GenericNodeManager
    {
        protected static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// node id is obtained through machineIP.GetHashCode();
        /// </summary>
        protected string machineIP;

        /// <summary>
        /// it changes due to coordinator inactivity
        /// </summary>
        protected string currentClusterCoordinatorIp;
        protected object currentClusterCoordinatorLock;

        protected int nThreads;

        protected object nThreadsLock;
        protected object bullyElectionLock;

        protected int requestExpiryInMilliseconds;

        /// <summary>
        /// will be used to create a grpc client in case of cluster node
        /// </summary>
        protected string middlewareEndpoint;

        /// <summary>
        /// all other cluster's nodes
        /// </summary>
        protected List<string> otherClusterNodes;

        /// <summary>
        /// nodes present in otherClusterNodes that have heigher id
        /// </summary>
        protected List<string> heigherClusterNodes;

        protected object dbService;

        protected System.Timers.Timer coordinatorTraceTimer;

        /// <summary>
        /// referenced in case of updating cluster nodes: currentLoadBalancer.Update()
        /// used to inform the coordinator about node removing or adding
        /// </summary>
        protected CustomLoadBalancerProxyProvider currentLoadBalancer;

        protected CancellationTokenSource cts;

        /// <summary>
        /// all notified request ids from external nodes
        /// </summary>
        protected Queue<KeyValuePair<string, KeyValuePair<DateTime, double>>> notifiedRequestsQueue;

        protected IDistributedCache cache;

        /// <summary>
        /// when the manager of microservice is up it'll request a copy of ids of all pending requests and it will consider
        /// the default timeout to start handling these requests
        /// </summary>
        /// <param name="currentLoadBalancer"></param>
        /// <param name="dbService"></param>
        /// <param name="clusterNodes"></param>
        /// <param name="middlewareEndpoint"></param>
        /// <param name="maxThreads"></param>
        /// <param name="requestExpiryInMilliseconds"></param>
        public void Init<T>(CustomLoadBalancerProxyProvider currentLoadBalancer,
            object dbService, 
            IDistributedCache cache,
            List<string> clusterNodes,
            string middlewareEndpoint,
            int maxThreads,
            int requestExpiryInMilliseconds) where T : ClientBase
        {
            try
            {
                this.machineIP = Dns.GetHostByName(Dns.GetHostName()).AddressList.First(address => address.AddressFamily == AddressFamily.InterNetwork).ToString();

                log.Info($"Invoked Init, machineIp: {machineIP}");

                this.currentLoadBalancer = currentLoadBalancer;
                this.cache = cache;
                this.dbService = dbService;
                otherClusterNodes = clusterNodes.Where(clusterNode => !clusterNode.Contains(machineIP)).ToList();
                heigherClusterNodes = otherClusterNodes.Where(clusterNode => clusterNode.Replace("http://", "").GetHashCode() > machineIP.GetHashCode()).ToList();
                this.middlewareEndpoint = middlewareEndpoint;
                nThreads = maxThreads;
                this.requestExpiryInMilliseconds = requestExpiryInMilliseconds;

                //init cluster nodes clients
                GrpcClientInitializer.Instance.InitializeClusterGrpcClients<T>(otherClusterNodes);

                QueuePolling();
            }
            catch (Exception ex)
            {
                log.Error(ex, "In Init()!");
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
        /// uses notifiedRequestsQueue to trace remote handled requests in the same cluster
        /// </summary>
        public abstract void QueuePolling();

        public async Task<List<TResult>> RunTasksAndAggregate<TInput, TResult>(IEnumerable<TInput> inputs, Func<TInput, Task<TResult>> taskFactory)
        {
            var tasks = inputs.Select(taskFactory).ToList();
            var results = await Task.WhenAll(tasks);

            return results.ToList();
        }

        public void InitCoordinatorInactivityTimer()
        {
            coordinatorTraceTimer = new System.Timers.Timer(90000); //1.5 min 
            coordinatorTraceTimer.Elapsed += CaptureInactiveCoordinator;
            coordinatorTraceTimer.AutoReset = true;

            coordinatorTraceTimer.Start();
        }

        public abstract void CaptureInactiveCoordinator(Object source, ElapsedEventArgs e);
    }
}
