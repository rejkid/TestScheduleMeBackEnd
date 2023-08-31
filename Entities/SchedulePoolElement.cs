using System;

namespace WebApi.Entities
{
    public class SchedulePoolElement
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public DateTime Date { get; set; }
        public Boolean Required { get; set; }
        public Boolean UserAvailability { get; set; }
        public string UserFunction { get; set; }
        public string ScheduleGroup { get; set; }
    }
}