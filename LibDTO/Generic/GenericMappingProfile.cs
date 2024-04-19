using AutoMapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MicroservicesProtos;

namespace LibDTO.Generic
{
    public class GenericMappingProfile : Profile
    {
        public GenericMappingProfile()
        {
            //WeekPayModel
            CreateMap<WeekPayModelType, WeekPayModel>();
            CreateMap<WeekPayModel, WeekPayModelType>();

            //DayRate
            CreateMap<DayRateType, DayRate>();
            CreateMap<DayRate, DayRateType>();

            //TimeInterval
            CreateMap<TimeIntervalType, TimeInterval>();
            CreateMap<TimeInterval, TimeIntervalType>();
        }
    }
}
