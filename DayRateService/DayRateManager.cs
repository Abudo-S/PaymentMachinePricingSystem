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

        private DayRateManager() { }

        internal bool AppendRequest(DayRate request)
        {
            //CompletionQueue
            return false;
        }
    }
}
