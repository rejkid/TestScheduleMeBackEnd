using System;
using System.Collections.Generic;

using WebApi.Entities;
namespace WebApi.Models.Accounts
{
    public class DateFunctionTeamResponse
    {
        public int Id { get; set; }
        
        
        public List<DateFunctionTeam> DateFunctionTeams { get; set; }
    }
}