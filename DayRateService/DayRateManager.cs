using LibDTO;

namespace DayRateService
{
    public class DayRateManager
    {
        private static readonly NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

        #region Singleton
        private static readonly Lazy<DayRateManager> lazy =
            new Lazy<DayRateManager>(() => new DayRateManager());
        public static DayRateManager Instance { get { return lazy.Value; } }
        #endregion

        private CancellationTokenSource cts = new CancellationTokenSource();

        private DayRateManager() {
            //when the manager of microservice is up it'll request a copy of ids of all pending requests and it will consider
            //the default timeout to start handling these requests
        }

        internal bool AppendRequest(DayRate request)
        {
            //CompletionQueue
            return false;
        }
    }
}
