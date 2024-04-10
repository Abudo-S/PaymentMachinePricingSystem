using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibDTO
{
    public class TimeInterval
    {
        public int Id { get; set; }
        public TimeSpan From {  get; set; }
        public TimeSpan To { get; set; }
        public List<TimeIntervalRule> Rules { get; set; } = new();

        /// <summary>
        /// if it contains a value then it's related to a specific day; otherwise, it's applied to all days generally
        /// </summary>
        public int? DayPriceRateId { get; set; }
    }
}
