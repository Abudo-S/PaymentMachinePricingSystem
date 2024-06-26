﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibDTO.Config
{
    public class PricingSystemDataBaseConfig
    {
        public string ConnectionString { get; set; } = null!;

        public string DatabaseName { get; set; } = null!;

        public string TimeIntervalCollectionName { get; set; } = null!;

        public string DayRateCollectionName { get; set; } = null!;

        public string WeekPayModelCollectionName { get; set; } = null!;
    }
}
