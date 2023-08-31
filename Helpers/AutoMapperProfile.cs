using AutoMapper;
using WebApi.Entities;
using WebApi.Models.Accounts;

namespace WebApi.Helpers
{
    public class AutoMapperProfile : Profile
    {
        // mappings between model and entity objects
        public AutoMapperProfile()
        {
            CreateMap<Account, User>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.AccountId));
            

            //CreateMap<Schedule, SchedulePoolElement>()
            //    /*.ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.ScheduleId))*/;

            // CreateMap<Schedule, SchedulePoolElement>();
            // CreateMap<SchedulePoolElement, Schedule>();



            CreateMap<Account, AccountResponse>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.AccountId));

            CreateMap<Account, AuthenticateResponse>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.AccountId));

            CreateMap<RegisterRequest, Account>();

            CreateMap<CreateRequest, Account>();

            CreateMap<UpdateScheduleRequest, Schedule>();

            CreateMap<UpdateScheduleRequest, SchedulePoolElement>();


            CreateMap<UpdateUserFunctionRequest, Function>();

            
            CreateMap<UpdateRequest, Account>()
            // .ForMember(d => d.Role, 
            //     op => op.MapFrom(o=> MapGrade(o.Role)))

            .ForMember(d => d.Schedules, op => op.Ignore())
            .ForMember(d => d.UserFunctions, op => op.Ignore())
                .ForAllMembers(x => x.Condition(
                    (src, dest, prop) =>
                    {
                        // ignore null & empty string properties
                        if (prop == null) return false;
                        if (prop.GetType() == typeof(string) && string.IsNullOrEmpty((string)prop)) return false;

                        // ignore null role
                        if (x.DestinationMember.Name == "Role" && src.Role == null) return false;

                        return true;
                    }
                ));
                
            //CreateMap<Account, UpdateRequest>();
        }
        public static Role MapGrade(string grade)
        {
            //TODO: function to map a string to a SchoolGradeDTO
            return Role.Admin;
        }
    }
}