namespace LibDTO
{
    public class TimeIntervalRule
    {
        public int Priority { get; set; }
        public double Amount { get; set; }
        public int FrequencyIntervalPerAmount { get; set; }
        public int TotalCoveredMinutes { get; set; }
        public required TimeInterval TimeInterval {  get; set; }
    }
}
