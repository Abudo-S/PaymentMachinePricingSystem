using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibDTO
{
    public class DayPriceRate
    {
        public int Id { get; set; }
        public int FreeMinutes { get; set; }
        public double MaxAmount { get; set; }
        public DayOfWeek CorrespondentDay { get; set; }
    }
}
