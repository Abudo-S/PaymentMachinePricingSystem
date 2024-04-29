using MongoDB.Bson.Serialization.Attributes;

namespace LibDTO
{
    [BsonIgnoreExtraElements]
    public class TimeIntervalRule
    {
        /// <summary>
        /// rule's priority in case of multiple rules covering a certain duration
        /// </summary>
        [BsonElement]
        public int Priority { get; set; }

        /// <summary>
        /// price
        /// </summary>
        [BsonElement]
        public double Amount { get; set; }

        /// <summary>
        /// minutes covered by amount
        /// </summary>
        [BsonElement]
        public int FrequencyIntervalPerAmount { get; set; }

        /// <summary>
        /// total minutes coverable by this rule
        /// </summary>
        [BsonElement]
        public int TotalCoveredMinutes { get; set; }
    }
}
