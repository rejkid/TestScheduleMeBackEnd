using System.ComponentModel.DataAnnotations;
using WebApi.Entities;
using System.Collections.Generic;
using System;

namespace WebApi.Models.Accounts
{
    public class UpdateUserFunctionRequest
    {
        //public int AccountId { get; set; }

        [Required]
        public string UserFunction { get; set; }
    }
}