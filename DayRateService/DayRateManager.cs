using DayRateService.DbServices;
using LibDTO;
using LibHelpers;
using MicroservicesProtos;

namespace DayRateService
{
    public class DayRateManager
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();
        private int nThreads;
        private object nThreadsLock;

        private DayRateDbService dayRateDbService;

        /// <summary>
        /// referenced in case of updating cluster nodes: currentLoadBalancer.Update()
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
        internal void Init(CustomLoadBalancerProxyProvider customLBPP, DayRateDbService dayRateDbService, int maxThreads)
        {
            log.Info("Invoked Init");
            this.currentLoadBalancer = customLBPP;
            this.dayRateDbService = dayRateDbService;
            nThreads = maxThreads;
            QueuePolling();
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
        public bool NotifyHandledRequest()
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
        /// uses notifiedRequestsQueue
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


        #endregion

    }
}
