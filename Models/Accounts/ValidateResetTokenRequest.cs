using System;
using System.ComponentModel.DataAnnotations;

namespace WebApi.Models.Accounts
{
    public class ValidateResetTokenRequest
    {
        [Required]
        public string Token { get; set; }

        [Required]
        public string Dob { get; set; }
    }
}