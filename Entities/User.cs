using System;

namespace WebApi.Entities
{
    public class User
    {
        public int Id { get; set; }
        public string FirstName { get; set; }

        public string LastName { get; set; }
        public string Email { get; set; }
        public string Function { get; set; }
        public DateTime Date { get; set; }
        public Boolean UserAvailability { get; set; }
        public string ScheduleGroup { get; set; }
    }
}