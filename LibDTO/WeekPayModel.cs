using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibDTO
{
    /// <summary>
    /// validation rule: assigned dayRate (ex. MondayRateId) should matches a DayRate's assigned-correspondent weekDay
    /// </summary>
    [BsonIgnoreExtraElements]
    public class WeekPayModel
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement]
        public int? MondayRateId { get; set; }

        [BsonElement]
        public int? TuesdayRateId { get; set; }

        [BsonElement]
        public int? WednesdayRateId { get; set; }

        [BsonElement]
        public int? ThursdayRateId { get; set; }

        [BsonElement]
        public int? FridayRateId { get; set; }

        [BsonElement]
        public int? SaturdayRateId { get; set; }

        [BsonElement]
        public int? SundayRateId { get; set; }

        /// <summary>
        /// to be applied on the sum of all day fees
        /// ex. 30.5% * (2 days' fee)
        /// </summary>
        [BsonElement]
        public double WeekDayPercentBonus { get; set; }

        /// <summary>
        /// In case of non-declared week-day rate
        /// </summary>
        [BsonElement]
        public required int WeekDayStandardRateId { get; set; }

    }
}
