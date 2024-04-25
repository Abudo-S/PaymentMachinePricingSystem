using Grpc.Core;
using LibDTO.Enum;
using LibHelpers;
using MicroservicesProtos;
using PaymentMachineProtos;
using Polly;
using Polly.CircuitBreaker;
using Polly.Wrap;
using static Google.Rpc.Context.AttributeContext.Types;

namespace RequestHandlerMiddleware
{
    public class RequestHandlerManager
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();
        private AsyncPolicyWrap grpcCircuitRetryPolicy;
        protected SemaphoreSlim clusterCoordinatorsSem;

        /// <summary>
        /// <ClusterType, clusterCoordinatorClient>
        /// </summary>
        internal Dictionary<string, ClientBase> clusterCoordinators;

        #region Singleton
        private static readonly Lazy<RequestHandlerManager> lazy =
            new Lazy<RequestHandlerManager>(() => new RequestHandlerManager());
        public static RequestHandlerManager Instance { get { return lazy.Value; } }
        #endregion

        /// <summary>
        /// the order of policy wrapping is important: 
        /// circuitBreakerPolicy.WrapAsync(retryPolicy).WrapAsync(timeoutCircuitRetryPolicy)
        /// </summary>
        private RequestHandlerManager()
        {
            clusterCoordinators = new ();
            clusterCoordinatorsSem = new SemaphoreSlim(1, 1);

            //for local request persistency
            ThreadPool.SetMaxThreads(5, 5);

            InitPollyCircuitRetryPolicy();
        }

        private void InitPollyCircuitRetryPolicy()
        {
            try
            {
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
            }
            catch (Exception ex)
            {
                log.Error(ex, "In InitPollyCircuitRetryPolicy()!");
            }
        }

        public void SendPaymentMachineResponse(string requestId, string requestType, string responseJson)
        {
            try
            {
                var kvp = GetSenderEPAndRequestId(requestId);
                //var paymentMachineClient = GrpcClientInitializer.Instance.GetNodeClient<PaymentMachine.PaymentMachineClient>(kvp.Key);
                
                //paymentMachineClient.ReceiveResponse();
            }
            catch (Exception ex)
            {
                log.Error(ex, "In PrepareMachineGrpcClient()!");
            }
        }

        public async Task SetClusterCoordinator(string clusterType, string coordinatorEndPoint)
        {
            try
            {

              await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
              {
                    await clusterCoordinatorsSem.WaitAsync();

                    switch (clusterType)
                    {
                        case ClusterType.DayRateCluster:
                            clusterCoordinators[clusterType] = GrpcClientInitializer.Instance.BuildSingleGrpcClient<DayRate.DayRateClient>(coordinatorEndPoint);
                            break;

                        case ClusterType.WeekPayModelCluster:
                            clusterCoordinators[clusterType] = GrpcClientInitializer.Instance.BuildSingleGrpcClient<WeekPayModel.WeekPayModelClient>(coordinatorEndPoint);
                            break;

                        case ClusterType.TimeIntervalCluster:
                            clusterCoordinators[clusterType] = GrpcClientInitializer.Instance.BuildSingleGrpcClient<TimeInterval.TimeIntervalClient>(coordinatorEndPoint);
                            break;

                        default:
                            clusterCoordinatorsSem.Release();
                            throw new KeyNotFoundException($"Can't find clusterType: {clusterType}");
                    }
               });
            }
            finally
            {
                clusterCoordinatorsSem.Release();
            }
        }

        public async Task HandleUpsertDayRate(UpsertDayRateRequest request)
        {
            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                await clusterCoordinatorsSem.WaitAsync();

                var res = ((DayRate.DayRateClient)clusterCoordinators[ClusterType.DayRateCluster]).UpsertDayRate(request);

                //always release the semaphore
                clusterCoordinatorsSem.Release();

                return res;
            });

            //persist in case of unacknowledgement
            if (!response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleUpsertDayRate(request));
            }
        }
        public async Task HandleGetDayRate(GetDayRateRequest request)
        {
            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                await clusterCoordinatorsSem.WaitAsync();

                var res = ((DayRate.DayRateClient)clusterCoordinators[ClusterType.DayRateCluster]).GetDayRate(request);

                //always release the semaphore
                clusterCoordinatorsSem.Release();

                return res;
            });

            //persist in case of unacknowledgement
            if (!response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleGetDayRate(request));
            }
        }

        public async Task HandleGetDayRates(GetDayRatesRequest request)
        {

            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                await clusterCoordinatorsSem.WaitAsync();

                var res = ((DayRate.DayRateClient)clusterCoordinators[ClusterType.DayRateCluster]).GetDayRates(request);

                //always release the semaphore
                clusterCoordinatorsSem.Release();

                return res;
            });

            //persist in case of unacknowledgement
            if (!response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleGetDayRates(request));
            }
        }

        public async Task HandleDeleteDayRate(DeleteDayRateRequest request)
        {

            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                await clusterCoordinatorsSem.WaitAsync();

                var res = ((DayRate.DayRateClient)clusterCoordinators[ClusterType.DayRateCluster]).DeleteDayRate(request);

                //always release the semaphore
                clusterCoordinatorsSem.Release();

                return res;
            });

            //persist in case of unacknowledgement
            if (!response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleDeleteDayRate(request));
            }
        }

        #region Helpers
        internal static string GetRequestId(string senderEP, string requestId)
        {
            return senderEP + "-" + requestId;
        }

        internal static KeyValuePair<string, string> GetSenderEPAndRequestId(string requestId)
        {
            var res = requestId.Split("-");
            return KeyValuePair.Create(res[0], res[1]);
        }
        #endregion
    }
}
