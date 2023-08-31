using System;
using System.Collections.Generic;

using WebApi.Entities;
namespace WebApi.Models.Accounts
{
    public class ScheduleDateTimeResponse
    {
        public int Id { get; set; }
        
        public List<ScheduleDateTime> ScheduleDateTimes { get; set; }
    }
}