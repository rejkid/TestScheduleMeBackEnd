using System.ComponentModel.DataAnnotations;

namespace WebApi.Models.Accounts
{
    public class VerifyEmailRequest
    {
        [Required]
        public string Token { get; set; }
        public string Dob { get; set; }
    }
}