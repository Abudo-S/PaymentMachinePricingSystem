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
using Polly;
using StackExchange.Redis;
using MongoDB.Driver;
using System.Runtime.CompilerServices;
using MongoDB.Driver.Linq;
using AutoMapper;

namespace LibDTO.Generic
{
    public abstract class GenericNodeManager
    {
        protected static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();
        protected AsyncPolicy grpcCircuitRetryPolicy;
        protected AsyncPolicy redisCircuitRetryPolicy;
        protected AsyncPolicy mongoCircuitRetryPolicy;

        protected bool isCoordinator;

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

        /// <summary>
        /// will be used to create a grpc client in case of cluster node
        /// </summary>
        protected string middlewareEndpoint;

        /// <summary>
        /// all other cluster's nodes
        /// </summary>
        protected List<string> otherClusterNodes;

        ///// <summary>
        ///// nodes present in otherClusterNodes that have heigher id
        ///// </summary>
        //protected List<string> heigherClusterNodes;

        protected object dbService;

        protected System.Timers.Timer coordinatorTraceTimer;
        protected System.Timers.Timer clusterNodesTimer;

        /// <summary>
        /// referenced in case of updating cluster nodes: currentLoadBalancer.Update()
        /// used to inform the coordinator about node removing or adding
        /// </summary>
        protected CustomLoadBalancerProxyProvider currentLoadBalancer;
        protected IMapper mapper;

        protected CancellationTokenSource cts;

        protected IDistributedCache cache;

        public int requestExpiryInMilliseconds { get; protected set; }

        /// <summary>
        /// all notified request ids from external nodes
        /// in case of normal cluster node, it'll be traced
        /// <requestId, <lastCommunicatedExpiry, expiry>>
        /// </summary>
        public Dictionary<string, KeyValuePair<DateTime, int>> notifiedHandledRequests { get; protected set; }

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
            string machineIP,
            IDistributedCache cache,
            List<string> otherClusterNodes,
            IMapper mapper,
            string middlewareEndpoint,
            int maxThreads,
            int requestExpiryInMilliseconds) where T : ClientBase
        {
            try
            {
                log.Info($"Invoked Init, machineIp: {machineIP}, nodeId: {machineIP.GetHashCode()}, maxThreads: {maxThreads}, middlewareEndpoint: {middlewareEndpoint}");

                InitPollyCircuitRetryPolicies();

                this.currentLoadBalancer = currentLoadBalancer;
                this.mapper = mapper;
                this.machineIP = machineIP;
                this.cache = cache;
                this.dbService = dbService;
                this.otherClusterNodes = otherClusterNodes;
                //heigherClusterNodes = otherClusterNodes.Where(clusterNode => clusterNode.Replace("http://", "").GetHashCode() > machineIP.GetHashCode()).ToList();
                this.middlewareEndpoint = middlewareEndpoint;
                nThreads = maxThreads;
                this.requestExpiryInMilliseconds = requestExpiryInMilliseconds;

                ThreadPool.SetMaxThreads(nThreads, nThreads);

                Task.Run(() =>
                {
                    //init cluster nodes clients
                    GrpcClientInitializer.Instance.InitializeClusterGrpcClients<T>(otherClusterNodes);

                    //ask all nodes for unique pending notified requests
                    AskForPendingRequests(); 
                });

                //prepare timers
                InitCoordinatorInactivityTimer(CaptureInactiveCoordinator);
                InitPingingTimer(PingClusterNodes);
            }
            catch (Exception ex)
            {
                log.Error(ex, "In Init()!");
            }
        }

        private void InitPollyCircuitRetryPolicies()
        {
            //generic system timeout policy
            var retryPolicy = Policy.Handle<TimeoutException>()
            .WaitAndRetryAsync(3,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(3, retryAttempt)),
            (ex, time) => {
                //log.Warn(ex, $"In timeoutCircuitRetryPolicy [{time}]");
                log.Warn($"In GenericPollyRetryPolicy [{time}], msg: {ex.Message}");
            });
            var circuitBreakerPolicy = Policy.Handle<RedisException>().CircuitBreakerAsync(3, TimeSpan.FromSeconds(30));
            var timeoutCircuitRetryPolicy = retryPolicy.WrapAsync(circuitBreakerPolicy);

            //grpc policy
            retryPolicy = Policy.Handle<RpcException>(ex =>
                new StatusCode[] { StatusCode.Cancelled, StatusCode.Unavailable, StatusCode.DataLoss }.Contains(ex.StatusCode))
                .WaitAndRetryAsync(3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(3, retryAttempt)),
                (ex, time) => {
                    //log.Warn(ex. $"In GrpcPollyRetryPolicy [{time}]");
                    log.Warn($"In GrpcPollyRetryPolicy [{time}], msg: {ex.Message}");
                });

            circuitBreakerPolicy = Policy.Handle<RpcException>(ex =>
                new StatusCode[] { StatusCode.Cancelled, StatusCode.Unavailable, StatusCode.DataLoss }.Contains(ex.StatusCode))
                .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30));
            grpcCircuitRetryPolicy = retryPolicy.WrapAsync(circuitBreakerPolicy).WrapAsync(timeoutCircuitRetryPolicy);

            //redis policy
            retryPolicy = Policy.Handle<RedisException>()
            .WaitAndRetryAsync(3,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(3, retryAttempt)),
            (ex, time) => {
                //log.Warn(ex, $"In RedisPollyRetryPolicy [{time}]");
                log.Warn($"In RediscPollyRetryPolicy [{time}], msg: {ex.Message}");
            });
            circuitBreakerPolicy = Policy.Handle<RedisException>().CircuitBreakerAsync(3, TimeSpan.FromSeconds(30));
            redisCircuitRetryPolicy = retryPolicy.WrapAsync(circuitBreakerPolicy).WrapAsync(timeoutCircuitRetryPolicy);

            //mongo policy
            retryPolicy = Policy.Handle<MongoException>(ex => 
            ex is MongoExecutionTimeoutException || ex is MongoClientException || ex is MongoServerException ||
            ex is MongoConnectionException || ex is MongoWaitQueueFullException)
                .WaitAndRetryAsync(3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(3, retryAttempt)),
                (ex, time) => {
                    //log.Warn(ex, $"In MongoPollyRetryPolicy [{time}]");
                    log.Warn($"In MongoPollyRetryPolicy [{time}], msg: {ex.Message}");
                });
            circuitBreakerPolicy = Policy.Handle<MongoException>(ex =>
                ex is MongoExecutionTimeoutException || ex is MongoClientException || ex is MongoServerException ||
                ex is MongoConnectionException || ex is MongoWaitQueueFullException)
                .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30));
            mongoCircuitRetryPolicy = retryPolicy.WrapAsync(circuitBreakerPolicy).WrapAsync(timeoutCircuitRetryPolicy);
        }

        /// <summary>
        /// take into account external request handling
        /// </summary>
        /// <param name="requestId"></param>
        /// <param name="requestExpiry">estimated expiry in seconds, declared by the node that invoked this method</param>
        /// <returns></returns>
        public bool AppendNotifiedRequest(string requestId, int requestExpiry)
        {
            try
            {
                this.notifiedHandledRequests.Add(requestId, KeyValuePair.Create(DateTime.UtcNow, requestExpiry));

                return (!isCoordinator)? ThreadPool.QueueUserWorkItem((object? state) => TraceNotifiedRequest(state, requestId, requestExpiry)): true;
            }
            catch (Exception ex)
            {
                log.Error(ex, "In AppendNotifiedRequest()!");
            }

            return false;
        }

        /// <summary>
        /// Trace remote handled requests in the same cluster - only normal nodes can trace
        /// </summary>
        public abstract Task TraceNotifiedRequest(object? state, string requestId, int requestExpiry);

        /// <summary>
        /// It's recommanded to run this method into a seperated Task, because it interfers with several cluster nodes
        /// </summary>
        public abstract void AskForPendingRequests();

        public async Task<List<TResult>> RunTasksAndAggregate<TInput, TResult>(IEnumerable<TInput> inputs, Func<TInput, Task<TResult>> taskFactory)
        {
            var tasks = inputs.Select(taskFactory).ToList();
            var results = await Task.WhenAll(tasks);

            return results.ToList();
        }

        #region Election

        /// <summary>
        /// should be assured that no another election is in progress
        /// </summary>
        /// <param name="anotherNodeId"></param>
        /// <returns></returns>
        public bool CheckIfNodeIdHigher(int anotherNodeId)
        {
            try
            {
                log.Info($"Invoked CheckIfNodeIdHigher anotherNodeId: {anotherNodeId}, currentNodeId {machineIP.GetHashCode()}");

                var result = Monitor.TryEnter(bullyElectionLock) && anotherNodeId < machineIP.GetHashCode();

                if (result)
                {
                    Task.Run(() =>
                    {
                        coordinatorTraceTimer.Stop();
                        _ = CanBeCoordinator_Bully();

                        if (!isCoordinator)
                            coordinatorTraceTimer.Start();
                    });                 
                }

                return !result;
            }
            catch (Exception ex)
            {
                log.Error(ex, "In CheckIfNodeIdHigher()!");
            }

            return false;
        }

        /// <summary>
        /// checks if no other heigher nodes are active; if so, then declare this node as coordinator.
        /// It's recommanded to run this method into a seperated Task, because it interfers with several cluster nodes
        /// </summary>
        public abstract Task CanBeCoordinator_Bully();
        #endregion

        #region Pinging
        public void InitCoordinatorInactivityTimer(ElapsedEventHandler onElapsedTimer)
        {
            coordinatorTraceTimer = new System.Timers.Timer(90000); //1.5 min 
            coordinatorTraceTimer.Elapsed += onElapsedTimer;
            coordinatorTraceTimer.AutoReset = true;

            //coordinatorTraceTimer.Start();
        }

        public void InitPingingTimer(ElapsedEventHandler onElapsedTimer)
        {
            clusterNodesTimer = new System.Timers.Timer(10000); //10 sec 
            clusterNodesTimer.Elapsed += onElapsedTimer;
            clusterNodesTimer.AutoReset = true;

            //clusterNodesTimer.Start();
        }

        /// <summary>
        /// asyncronous timer
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        public abstract void PingClusterNodes(Object source, ElapsedEventArgs e);

        /// <summary>
        /// received from coordinator node.
        /// if a coordinator received this message, it means that a new coordinato might be elected per a detected inactivity; 
        /// in such a case, current node will request all nodes for majorCoordinator determination.
        /// it's recommanded to run this method into a seperated Task, because it might interfer with several cluster nodes
        /// </summary>
        /// <param name="coordinatorIp"></param>
        /// <returns></returns>
        public abstract Task CaptureCoordinator(string coordinatorIp);

        public abstract void CaptureInactiveCoordinator(object source, ElapsedEventArgs e);
        #endregion

        #region Coordination
        public bool StartCoordinatorActivity()
        {
            try
            {
                log.Info("Invoked StartCoordinatorActivity");

                lock (currentClusterCoordinatorLock)
                {
                    isCoordinator = true;
                    currentClusterCoordinatorIp = null;

                    //stop coordinator tracer timer
                    coordinatorTraceTimer.Stop();
                    clusterNodesTimer.Start();
                }

                return true;
            }
            catch (Exception ex)
            {
                log.Error(ex, "In StartCoordinatorActivity()!");
            }

            return false;
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

                    //stop cluster node pinging
                    clusterNodesTimer.Stop();

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
        /// <summary>
        /// the idea is that if a cluster node wants to handle a request and the coordinator notices that the current request handling is expired,
        /// then it responds true and update its notifiedHandledRequests
        /// </summary>
        /// <param name="requestId"></param>
        /// <param name="requestExpiry">new request expiry</param>
        /// <returns></returns>
        public bool CanIHandle(string requestId, int requestExpiry)
        {
            try
            {
                lock(this)
                {
                    var notifiedHandledRequest = notifiedHandledRequests.FirstOrDefault(kvp => kvp.Key == requestId);

                    if (notifiedHandledRequest.Equals(default(KeyValuePair<string, KeyValuePair<DateTime, int>>)))
                    {
                        throw new KeyNotFoundException("Can't find requestId, unsyncronized coordinator!");
                    }

                    if (notifiedHandledRequest.Value.Key.AddMilliseconds(notifiedHandledRequest.Value.Value) < DateTime.UtcNow)
                    {
                        //update notifiedHandledRequests
                        notifiedHandledRequest = KeyValuePair.Create(requestId, KeyValuePair.Create(DateTime.UtcNow, requestExpiry));
                        notifiedHandledRequests[notifiedHandledRequest.Key] = notifiedHandledRequest.Value;

                        return true;
                    }
                }
            } 
            catch (Exception ex)
            {
                log.Error(ex, $" In CanIHandle with requestId: {requestId}");
            }

            return false;
        }

        /// <summary>
        /// notifies all cluster nodes of handled requests
        /// </summary>
        /// <returns></returns>
        public abstract bool NotifyHandledRequest(string requestId);

        public abstract bool AddClusterNode(string clusterNodeUri);

        public abstract bool RemoveClusterNode(string clusterNodeuri);

        #endregion

        public string GetCoordinatorIp()
        {
            if (isCoordinator)
                return this.machineIP;
            else
                return currentClusterCoordinatorIp;
        }
    }
}
