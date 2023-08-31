using System;
using System.Collections.Generic;

namespace WebApi.Entities
{
    public class DateFunctionTeam
    {
        public DateFunctionTeam(DateTime date, string function)
        {
            Function = function;
            Date = date;
            Users = new List<User>(); 
        }
        public int Id { get; set; }
        public DateTime Date;
        public string Function { get; set; }
        public List<User> Users { get; set; }
    }
}