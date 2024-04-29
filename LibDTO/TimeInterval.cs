using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibDTO
{
    [BsonIgnoreExtraElements]
    public class TimeInterval
    {

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement]
        public TimeSpan From {  get; set; }

        [BsonElement]
        public TimeSpan To { get; set; }

        [BsonElement]
        public List<TimeIntervalRule> Rules { get; set; } = new();

        /// <summary>
        /// if it contains a value then it's related to a specific day; otherwise, it's applied to all days generally
        /// </summary>
        [BsonElement]
        public int? DayPriceRateId { get; set; }
    }
}
