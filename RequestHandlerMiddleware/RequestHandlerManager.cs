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
            
            //for local request persistency (cluster side or payment machine side)
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

        /// <summary>
        /// client's endpoint should be encoded in requestId
        /// persist on sending client response
        /// </summary>
        /// <param name="requestId"></param>
        /// <param name="requestType"></param>
        /// <param name="responseJson"></param>
        public async Task SendPaymentMachineResponse(string requestId, string requestType, string responseJson)
        {
            try
            {
                var kvp = GetSenderEPAndRequestId(requestId);
                var paymentMachineClient = (DumbPaymentMachine.DumbPaymentMachineClient)GrpcClientInitializer.Instance.GetNodeClient<DumbPaymentMachine.DumbPaymentMachineClient>(kvp.Key);

                var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
                {
                    var res = paymentMachineClient.ReceiveResponse(new ReceiveResponseMsg()
                    {
                        RequestId = requestId,
                        ResultType = requestType,
                        ResultJson = responseJson
                    });

                    return res;
                });

                //persist in case of unacknowledgement
                if (response == null || !response.Result)
                {
                    ThreadPool.QueueUserWorkItem(async (object? state) => await SendPaymentMachineResponse(requestId, requestType, responseJson));
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "In SendPaymentMachineResponse()!");
            }
        }

        public async Task SetClusterCoordinator(string clusterType, string coordinatorEndPoint)
        {
              await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
              {
                  await clusterCoordinatorsSem.WaitAsync();
                  try
                  {
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
                  }
                  catch (RpcException e)
                  {
                      clusterCoordinatorsSem.Release();
                      throw new RpcException(e.Status); //for grpcCircuitRetryPolicy
                  }
                  finally
                  {
                      clusterCoordinatorsSem.Release();
                  }
              });
        }

        #region WeekPayModel Handling
        public async Task HandleUpsertWeekPayModel(UpsertWeekPayModelRequest request)
        {
            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                //wait if a cluster coordinator is being modified
                while (clusterCoordinatorsSem.CurrentCount == 0) 
                {
                    await Task.Delay(1000);
                }

                var res = ((WeekPayModel.WeekPayModelClient)clusterCoordinators[ClusterType.WeekPayModelCluster]).UpsertWeekPayModel(request);

                return res;
            });

            //persist in case of unacknowledgement
            if (response == null || !response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleUpsertWeekPayModel(request));
            }
        }
        public async Task HandleGetWeekPayModel(GetWeekPayModelRequest request)
        {
            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                //wait if a cluster coordinator is being modified
                while (clusterCoordinatorsSem.CurrentCount == 0)
                {
                    await Task.Delay(1000);
                }

                var res = ((WeekPayModel.WeekPayModelClient)clusterCoordinators[ClusterType.WeekPayModelCluster]).GetWeekPayModel(request);

                return res;
            });

            //persist in case of unacknowledgement
            if (response == null || !response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleGetWeekPayModel(request));
            }
        }

        public async Task HandleGetWeekPayModels(GetWeekPayModelsRequest request)
        {

            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                //wait if a cluster coordinator is being modified
                while (clusterCoordinatorsSem.CurrentCount == 0)
                {
                    await Task.Delay(1000);
                }

                var res = ((WeekPayModel.WeekPayModelClient)clusterCoordinators[ClusterType.WeekPayModelCluster]).GetWeekPayModels(request);


                return res;
            });

            //persist in case of unacknowledgement
            if (response == null || !response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleGetWeekPayModels(request));
            }
        }

        public async Task HandleDeleteWeekPayModel(DeleteWeekPayModelRequest request)
        {

            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                //wait if a cluster coordinator is being modified
                while (clusterCoordinatorsSem.CurrentCount == 0)
                {
                    await Task.Delay(1000);
                }

                var res = ((WeekPayModel.WeekPayModelClient)clusterCoordinators[ClusterType.WeekPayModelCluster]).DeleteWeekPayModel(request);

                return res;
            });

            //persist in case of unacknowledgement
            if (response == null || !response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleDeleteWeekPayModel(request));
            }
        }
        #endregion

        #region DayRate Handling
        public async Task HandleUpsertDayRate(UpsertDayRateRequest request)
        {
            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                //wait if a cluster coordinator is being modified
                while (clusterCoordinatorsSem.CurrentCount == 0)
                {
                    await Task.Delay(1000);
                }

                var res = ((DayRate.DayRateClient)clusterCoordinators[ClusterType.DayRateCluster]).UpsertDayRate(request);

                return res;
            });

            //persist in case of unacknowledgement
            if (response == null || !response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleUpsertDayRate(request));
            }
        }
        public async Task HandleGetDayRate(GetDayRateRequest request)
        {
            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                //wait if a cluster coordinator is being modified
                while (clusterCoordinatorsSem.CurrentCount == 0)
                {
                    await Task.Delay(1000);
                }

                var res = ((DayRate.DayRateClient)clusterCoordinators[ClusterType.DayRateCluster]).GetDayRate(request);

                return res;
            });

            //persist in case of unacknowledgement
            if (response == null || !response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleGetDayRate(request));
            }
        }

        public async Task HandleGetDayRates(GetDayRatesRequest request)
        {

            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                //wait if a cluster coordinator is being modified
                while (clusterCoordinatorsSem.CurrentCount == 0)
                {
                    await Task.Delay(1000);
                }

                var res = ((DayRate.DayRateClient)clusterCoordinators[ClusterType.DayRateCluster]).GetDayRates(request);

                return res;
            });

            //persist in case of unacknowledgement
            if (response == null || !response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleGetDayRates(request));
            }
        }

        public async Task HandleDeleteDayRate(DeleteDayRateRequest request)
        {

            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                //wait if a cluster coordinator is being modified
                while (clusterCoordinatorsSem.CurrentCount == 0)
                {
                    await Task.Delay(1000);
                }

                var res = ((DayRate.DayRateClient)clusterCoordinators[ClusterType.DayRateCluster]).DeleteDayRate(request);

                return res;
            });

            //persist in case of unacknowledgement
            if (response == null || !response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleDeleteDayRate(request));
            }
        }
        #endregion

        #region TimeInterval Handling
        public async Task HandleUpsertTimeInterval(UpsertTimeIntervalRequest request)
        {
            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                //wait if a cluster coordinator is being modified
                while (clusterCoordinatorsSem.CurrentCount == 0)
                {
                    await Task.Delay(1000);
                }

                var res = ((TimeInterval.TimeIntervalClient)clusterCoordinators[ClusterType.TimeIntervalCluster]).UpsertTimeInterval(request);

                return res;
            });

            //persist in case of unacknowledgement
            if (response == null || !response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleUpsertTimeInterval(request));
            }
        }

        public async Task HandleGetTimeInterval(GetTimeIntervalRequest request)
        {
            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                //wait if a cluster coordinator is being modified
                while (clusterCoordinatorsSem.CurrentCount == 0)
                {
                    await Task.Delay(1000);
                }

                var res = ((TimeInterval.TimeIntervalClient)clusterCoordinators[ClusterType.TimeIntervalCluster]).GetTimeInterval(request);

                return res;
            });

            //persist in case of unacknowledgement
            if (response == null || !response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleGetTimeInterval(request));
            }
        }

        public async Task HandleGetTimeIntervals(GetTimeIntervalsRequest request)
        {

            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                //wait if a cluster coordinator is being modified
                while (clusterCoordinatorsSem.CurrentCount == 0)
                {
                    await Task.Delay(1000);
                }

                var res = ((TimeInterval.TimeIntervalClient)clusterCoordinators[ClusterType.TimeIntervalCluster]).GetTimeIntervals(request);

                return res;
            });

            //persist in case of unacknowledgement
            if (response == null || !response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleGetTimeIntervals(request));
            }
        }

        public async Task HandleDeleteTimeInterval(DeleteTimeIntervalRequest request)
        {

            var response = await grpcCircuitRetryPolicy.ExecuteAsync(async () =>
            {
                //wait if a cluster coordinator is being modified
                while (clusterCoordinatorsSem.CurrentCount == 0)
                {
                    await Task.Delay(1000);
                }

                var res = ((TimeInterval.TimeIntervalClient)clusterCoordinators[ClusterType.TimeIntervalCluster]).DeleteTimeInterval(request);

                return res;
            });

            //persist in case of unacknowledgement
            if (response == null || !response.Awk)
            {
                ThreadPool.QueueUserWorkItem(async (object? state) => await HandleDeleteTimeInterval(request));
            }
        }
        #endregion

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
