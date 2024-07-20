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
using Polly.CircuitBreaker;
using Google.Api;
using System.Threading;
using MiddlewareProtos;
using LibDTO.Enum;

namespace LibDTO.Generic
{
    public abstract class GenericNodeManager
    {
        public static int AdditionalOperationDelay { get; set; } = 0;

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
        protected SemaphoreSlim currentClusterCoordinatorSem;

        protected int nThreads;

        protected object nThreadsLock;
        protected SemaphoreSlim bullyElectionLockSem;

        /// <summary>
        /// dedicated to synchronize add/remove cluster nodes
        /// </summary>
        protected SemaphoreSlim clusterNodesSem;

        /// <summary>
        /// used to create/recreate a grpc client in case of cluster node
        /// </summary>
        protected string middlewareEndpoint;

        /// <summary>
        /// a grpc client in case of cluster node
        /// </summary>
        protected RequestHandler.RequestHandlerClient middlewareGrpcClient;

        /// <summary>
        /// all other cluster's nodes
        /// </summary>
        protected volatile List<string> otherClusterNodes;

        /// <summary>
        /// nodes present in otherClusterNodes that have higher id
        /// </summary>
        //protected List<string> higherClusterNodes;

        protected object dbService;

        protected System.Timers.Timer coordinatorTraceTimer;
        protected System.Timers.Timer clusterNodesTimer;

        /// <summary>
        /// referenced in case of updating cluster nodes: currentLoadBalancer.Update()
        /// used to inform the coordinator about node removing or adding
        /// </summary>
        protected CustomLoadBalancerProxyProvider currentLoadBalancer;
        protected IMapper mapper;

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
                //higherClusterNodes = otherClusterNodes.Where(clusterNode => clusterNode.Replace("http://", "").GetHashCode() > machineIP.GetHashCode()).ToList();
                this.middlewareEndpoint = middlewareEndpoint;
                nThreads = maxThreads;
                this.requestExpiryInMilliseconds = requestExpiryInMilliseconds;

                ThreadPool.SetMaxThreads(nThreads, nThreads);

                //build othernodes' clients
                Task.Run(async () =>
                {
                    //grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                    //{
                        //init cluster nodes clients
                        GrpcClientInitializer.Instance.InitializeGrpcClients<T>(otherClusterNodes);

                        log.Info("Created otherClusterNodes grpc clients");

                        //ask all nodes for unique pending notified requests
                        await AskForPendingRequests();
                    //});
                });

                //build middleware client
                Task.Run(async () =>
                {
                    var result = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                                {
                                    return GrpcClientInitializer.Instance.BuildSingleGrpcClient<RequestHandler.RequestHandlerClient>(
                                            this.middlewareEndpoint
                                        );
                                });

                    log.Info("Created middleware client");

                    this.middlewareGrpcClient = (RequestHandler.RequestHandlerClient)result;
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


        /// <summary>
        /// the order of policy wrapping is important: 
        /// circuitBreakerPolicy.WrapAsync(retryPolicy).WrapAsync(timeoutCircuitRetryPolicy)
        /// </summary>
        private void InitPollyCircuitRetryPolicies()
        {
            //generic system timeout policy
            var retryPolicy = Policy.Handle<TimeoutException>()
            .WaitAndRetryAsync(30,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(3, retryAttempt)),
            (ex, time, context) => {
                try
                {
                    var methodMsg = context.Contains("methodMsg") ? context["methodMsg"] : "";
                    //log.Warn(ex, $"In genericCircuitRetryPolicy [{time}]");
                    log.Warn($"In GenericPollyRetryPolicy [{time}], methodMsg: {methodMsg}, msg: {ex.Message}");
                }
                catch (BrokenCircuitException) { }
            });

            var circuitBreakerPolicy = Policy.Handle<TimeoutException>().CircuitBreakerAsync(3, TimeSpan.FromSeconds(30));
            var timeoutCircuitRetryPolicy = circuitBreakerPolicy.WrapAsync(retryPolicy);

            //grpc policy
            retryPolicy = Policy.Handle<RpcException>(ex =>
                new StatusCode[] { StatusCode.Cancelled, StatusCode.Unavailable, StatusCode.DataLoss }.Contains(ex.StatusCode))
                .WaitAndRetryAsync(30,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(3, retryAttempt)),
                (ex, time, context) => {
                    try
                    {
                        var methodMsg = context.Contains("methodMsg") ? context["methodMsg"] : "";
                        //log.Warn(ex, $"In GrpcCircuitRetryPolicy [{time}]");
                        log.Warn($"In GrpcPollyRetryPolicy [{time}], methodMsg: {methodMsg}, msg: {ex.Message}");
                    }
                    catch (BrokenCircuitException) { }
                });

            circuitBreakerPolicy = Policy.Handle<RpcException>(ex =>
                new StatusCode[] { StatusCode.Cancelled, StatusCode.Unavailable, StatusCode.DataLoss }.Contains(ex.StatusCode))
                .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30));
            grpcCircuitRetryPolicy = circuitBreakerPolicy.WrapAsync(retryPolicy).WrapAsync(timeoutCircuitRetryPolicy);

            //redis policy
            retryPolicy = Policy.Handle<RedisException>()
            .WaitAndRetryAsync(30,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(3, retryAttempt)),
            (ex, time, context) => {
                try
                {
                    var methodMsg = context.Contains("methodMsg") ? context["methodMsg"] : "";
                    //log.Warn(ex, $"In RedisCircuitRetryPolicy [{time}]");
                    log.Warn($"In RedisPollyRetryPolicy [{time}], methodMsg: {methodMsg}, msg: {ex.Message}");
                }
                catch (BrokenCircuitException) { }
            });
            circuitBreakerPolicy = Policy.Handle<RedisException>().CircuitBreakerAsync(3, TimeSpan.FromSeconds(30));
            redisCircuitRetryPolicy = circuitBreakerPolicy.WrapAsync(retryPolicy).WrapAsync(timeoutCircuitRetryPolicy);

            //mongo policy
            retryPolicy = Policy.Handle<MongoException>(ex => 
            ex is MongoExecutionTimeoutException || ex is MongoClientException || ex is MongoServerException ||
            ex is MongoConnectionException || ex is MongoWaitQueueFullException)
                .WaitAndRetryAsync(30,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(3, retryAttempt)),
                (ex, time, context) => {
                    try
                    {
                        var methodMsg = context.Contains("methodMsg") ? context["methodMsg"] : "";
                        //log.Warn(ex, $"In MongoCircuitRetryPolicy [{time}]");
                        log.Warn($"In MongoPollyRetryPolicy [{time}], methodMsg: {methodMsg}, msg: {ex.Message}");
                    }
                    catch (BrokenCircuitException) { }
                });
            circuitBreakerPolicy = Policy.Handle<MongoException>(ex =>
                ex is MongoExecutionTimeoutException || ex is MongoClientException || ex is MongoServerException ||
                ex is MongoConnectionException || ex is MongoWaitQueueFullException)
                .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30));
            mongoCircuitRetryPolicy = circuitBreakerPolicy.WrapAsync(retryPolicy).WrapAsync(timeoutCircuitRetryPolicy);
        }

        /// <summary>
        /// takes into account external request handling
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
        public abstract Task AskForPendingRequests();

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

                
                var result = anotherNodeId < machineIP.GetHashCode();

                //if the current id is higher then start own election
                //block concurrent elections
                if (result && bullyElectionLockSem.CurrentCount > 0 && !isCoordinator)
                {

                    Task.Run(async() =>
                    {
                        coordinatorTraceTimer.Stop();
                        await CanBeCoordinator_Bully();

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
        /// checks if no other higher nodes are active; if so, then declare this node as coordinator.
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
            clusterNodesTimer = new System.Timers.Timer(30000); //30 sec 
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
        public async Task<bool> StartCoordinatorActivity()
        {
            bool result = false;

            try
            {
                log.Info("Invoked StartCoordinatorActivity");

                //wait and acquire semaphore
                await currentClusterCoordinatorSem.WaitAsync();

                isCoordinator = true;
                currentClusterCoordinatorIp = null;

                //stop coordinator tracer timer
                coordinatorTraceTimer.Stop();
                clusterNodesTimer.Start();

                //inform the middleware of the new coordinator
                _ = Task.Run(async () =>
                {
                    while (middlewareGrpcClient == null)
                        await Task.Delay(5000);

                    await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                    {
                        middlewareGrpcClient.NotifyClusterCoordinator(new NotifyClusterCoordinatorRequest()
                        {
                            ClusterType = ClusterType.DayRateCluster,
                            CoordinatorEndpoint = $"http://{machineIP}:80" //supposing that the port is static!
                        });
                    });
                });

                result = true;
            }
            catch (Exception ex)
            {
                log.Error(ex, "In StartCoordinatorActivity()!");
            }
            finally //always release semaphore
            {
                currentClusterCoordinatorSem.Release();
            }

            return result;
        }

        /// <summary>
        /// in case of new coordinator elected and this node is considered as a time-out coordinator
        /// </summary>
        /// <param name="currentCoordinator"></param>
        /// <returns></returns>
        public async Task<bool> StopCoordinatorActivity(string currentCoordinator)
        {
            bool result = false;

            try
            {
                log.Info("Invoked StopCoordinatorActivity");

                //wait and acquire semaphore
                await currentClusterCoordinatorSem.WaitAsync();

                isCoordinator = false;
                currentClusterCoordinatorIp = currentCoordinator;

                //stop cluster node pinging
                clusterNodesTimer.Stop();

                //start coordinator tracer timer
                coordinatorTraceTimer.Start();
            }
            catch (Exception ex)
            {
                log.Error(ex, "In StopCoordinatorActivity()!");
            }
            finally //always release semaphore
            {
                currentClusterCoordinatorSem.Release();
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

        public abstract Task<bool> AddClusterNode(string clusterNodeUri);

        public abstract Task<bool> RemoveClusterNode(string clusterNodeuri);

        #endregion

        public string GetCoordinatorIp()
        {
            if (isCoordinator)
                return this.machineIP;
            else
                return currentClusterCoordinatorIp;
        }

        public string GetRequestId(string requestId)
        {
            return requestId.Contains("@")? requestId.Split("@")[1] : requestId;
        }
    }
}
