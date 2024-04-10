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
    public class WeekPayModel
    {
        public int Id { get; set; }
        public int? MondayRateId { get; set; }
        public int? TuesdayRateId { get; set; }
        public int? wednesdayRateId { get; set; }
        public int? ThursdayRateId { get; set; }
        public int? FridayRateId { get; set; }
        public int? SaturdayRateId { get; set; }
        public int? SundayRateId { get; set; }

        /// <summary>
        /// to be applied on the sum of all day fees
        /// ex. 30.5 * (2 days' fee)
        /// </summary>
        public double WeekDayPercentBonus { get; set; }

        /// <summary>
        /// In case of non-declared week-day rate
        /// </summary>
        public required int WeekDayStandardRateId { get; set; }

    }
}
