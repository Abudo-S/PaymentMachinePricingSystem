using AutoMapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MicroservicesProtos;
using LibDTO;

namespace LibDTO.Generic
{
    public class GenericMappingProfile : Profile
    {
        public GenericMappingProfile()
        {
            //WeekPayModel
            CreateMap<WeekPayModelType, LibDTO.WeekPayModel>();
            CreateMap<LibDTO.WeekPayModel, WeekPayModelType>();

            //DayRate
            CreateMap<DayRateType, LibDTO.DayRate>();
            CreateMap<LibDTO.DayRate, DayRateType>();

            //TimeInterval
            CreateMap<TimeIntervalType, LibDTO.TimeInterval>();
            CreateMap<LibDTO.TimeInterval, TimeIntervalType>();
        }
    }
}
