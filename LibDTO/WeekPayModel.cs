using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibDTO
{
    public class WeekPayModel
    {
        public required DayPriceRate MondayRate { get; set; }
        public required DayPriceRate TuesdayRate { get; set; }
        public required DayPriceRate wednesdayRate { get; set; }
        public required DayPriceRate ThursdayRate { get; set; }
        public required DayPriceRate FridayRate { get; set; }
        public required DayPriceRate SaturdayRate { get; set; }
        public required DayPriceRate SundayRate { get; set; }
        public int WeekDayPercentBonus {  get; set; } //to be applied on the sum of all day fees

    }
}
