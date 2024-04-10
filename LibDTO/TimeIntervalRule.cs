namespace LibDTO
{
    public class TimeIntervalRule
    {
        /// <summary>
        /// rule's priority in case of multiple rules covering a certain duration
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// price
        /// </summary>
        public double Amount { get; set; }

        /// <summary>
        /// minutes covered by amount
        /// </summary>
        public int FrequencyIntervalPerAmount { get; set; }

        /// <summary>
        /// total minutes coverable by this rule
        /// </summary>
        public int TotalCoveredMinutes { get; set; }
    }
}
