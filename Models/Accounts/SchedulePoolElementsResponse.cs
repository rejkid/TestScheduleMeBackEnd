
using System.Collections.Generic;
using WebApi.Entities;

namespace WebApi.Models.Accounts
{
    public class SchedulePoolElementsResponse
    {
       public int Id { get; set; }
        
        
        public List<SchedulePoolElement> SchedulePoolElements { get; set; }
    }
}