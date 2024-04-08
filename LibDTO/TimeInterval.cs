using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibDTO
{
    public class TimeInterval
    {
        public TimeSpan From {  get; set; }
        public TimeSpan To { get; set; }
        public DayPriceRate DayPriceRate { get; set; }
    }
}
