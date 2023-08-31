using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using System;
using System.Collections.Generic;
using WebApi.Entities;
using WebApi.Models.Accounts;
using WebApi.Services;
using Microsoft.Extensions.Options;

using WebApi.Helpers;

using log4net;
using System.Linq;
using Microsoft.EntityFrameworkCore.Internal;
using AutoMapper.Internal;
using Microsoft.Extensions.Primitives;
using Org.BouncyCastle.Ocsp;
using static Google.Apis.Requests.BatchRequest;

namespace WebApi.Controllers
{
public class UserFriendlyException: Exception
    {
        public UserFriendlyException(string message) : base(message) { }
        public UserFriendlyException(string message, Exception innerException) : base(message, innerException) { }
    }
    
    [ApiController]
    [Route("[controller]")]
    public class AccountsController : BaseController
    {
        #region log4net
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        #endregion //log4net
        
        private readonly IAccountService _accountService;
        private readonly IMapper _mapper;
        private readonly AppSettings _appSettings;
        


        public AccountsController(
            IAccountService accountService,
            IMapper mapper,
            IOptions<AppSettings> appSettings)
        {
            _accountService = accountService;
            _mapper = mapper;
            _appSettings = appSettings.Value;
        }

        
        [HttpPost("authenticate")]
        public ActionResult<AuthenticateResponse> Authenticate(AuthenticateRequest model)
        {
            log.InfoFormat("Authenticating user {0} password {1} DOB {2} for ipaddress: {3}",
                model.Email,
                model.Password,
                model.Dob,
                ipAddress());
            var response = _accountService.Authenticate(model, ipAddress());
            setTokenCookie(response.RefreshToken);

            log.InfoFormat("Setting cookie - response.RefreshToken= {0} for E-mail: {1}",
                response.RefreshToken,
                model.Email);

            return Ok(response);
        }

        
        [HttpPost("refresh-token")]
        public ActionResult<AuthenticateResponse> RefreshToken()
        {
            var refreshToken = Request.Cookies["refreshToken"];

            Console.WriteLine("refreshToken is:"+refreshToken);
            var response = _accountService.RefreshToken(refreshToken, ipAddress());
            setTokenCookie(response.RefreshToken);

            return Ok(response);
        }

        [Authorize]
        [HttpPost("revoke-token")]
        public IActionResult RevokeToken(RevokeTokenRequest model)
        {
            var refreshToken = Request.Cookies["refreshToken"];

            // accept token from request body or cookie
            var token = model.Token ?? Request.Cookies["refreshToken"];

            if (string.IsNullOrEmpty(token))
                return BadRequest(new { message = "Token is required" });

            // users can revoke their own tokens and admins can revoke any tokens
            if (!Account.OwnsToken(token) && Account.Role != Role.Admin)
                return Unauthorized(new { message = "Unauthorized" });

            _accountService.RevokeToken(token, ipAddress());
            return Ok(new { message = "Token revoked" });
        }

        [HttpPost("register")]
        public IActionResult Register(RegisterRequest model)
        {
            _accountService.Register(model, Request.Headers["origin"]);
            return Ok(new { message = "Registration successful, please check your email for verification instructions" });
        }

        [HttpPost("verify-email")]
        public IActionResult VerifyEmail(VerifyEmailRequest model)
        {
            //log.Info("VerifyEmail before calling Parse");
            //DateTime dateTime = DateTime.Parse(model.Dob);
            _accountService.VerifyEmail(model/*model.Token, dateTime*/);
            return Ok(new { message = "Verification successful, you can now login" });
        }

        [HttpPost("forgot-password")]
        public IActionResult ForgotPassword(ForgotPasswordRequest model)
        {
            _accountService.ForgotPassword(model, Request.Headers["origin"]);
            return Ok(new { message = "Please check your email for password reset instructions" });
        }

        [HttpPost("validate-reset-token")]
        public IActionResult ValidateResetToken(ValidateResetTokenRequest model)
        {
            
            _accountService.ValidateResetToken(model);
            return Ok(new { message = "Token is valid" });
        }

        [HttpPost("reset-password")]
        public IActionResult ResetPassword(ResetPasswordRequest model)
        {
            _accountService.ResetPassword(model);
            return Ok(new { message = "Password reset successful, you can now login" });
        }

        [Authorize(Role.Admin)]
        [HttpGet]
        public ActionResult<IEnumerable<AccountResponse>> GetAll()
        {
            var accounts = _accountService.GetAll();
            return Ok(accounts);
        }

        [Authorize]
        [HttpGet("{id:int}")]
        public ActionResult<AccountResponse> GetById(int id)
        {
            var account = _accountService.GetById(id);
            // users can get their own account and admins can get any account
            if (id != Account.AccountId && Account.Role != Role.Admin)
            {
                log.ErrorFormat("User AccountId:{0} First Name:{1} Last Name:{2} e-mail:{3} tried to get the info about \n " +
                    "AccountId:{4} First Name:{5} Last Name:{6} e-mail:{7}",
                    Account.AccountId,
                    Account.FirstName,
                    Account.LastName,
                    Account.Email,

                    id,
                    account.FirstName,
                    account.LastName,
                    account.Email
                    );

                return Unauthorized(new { message = "Unauthorized" });
            }

            return Ok(account);
        }

        [Authorize]
        [HttpGet("all-dates")]
        public ActionResult<ScheduleDateTimeResponse> GetAllDates()
        {
            var dates = _accountService.GetAllDates();
            return Ok(dates);
        }

        [HttpGet("teams-for-date/{date}")]
        public ActionResult<DateFunctionTeamResponse> GetTeamsByFunctionForDate(string date)
        {
            var users = _accountService.GetTeamsByFunctionForDate(date);
            return Ok(users);
        }

        [Authorize(Role.Admin)]
        [HttpPost]
        public ActionResult<AccountResponse> Create(CreateRequest model)
        {
            var account = _accountService.Create(model);
            return Ok(account);
        }

        [Authorize]
        [HttpPut("{id:int}")]
        public ActionResult<AccountResponse> Update(int id, UpdateRequest model)
        {
            // users can update their own account and admins can update any account
            if (id != Account.AccountId && Account.Role != Role.Admin)
                return Unauthorized(new { message = "Unauthorized" });

            // only admins can update role
            if (Account.Role != Role.Admin)
                model.Role = null;

            var account = _accountService.Update(id, model);
            return Ok(account);
        }
        [Authorize]
        [HttpPut("add-schedule/{id}")]
        public ActionResult<AccountResponse> AddSchedule(int id, UpdateScheduleRequest schedule)
        {
            // users can update their own account and admins can update any account
            if (id != Account.AccountId && Account.Role != Role.Admin)
                return Unauthorized(new { message = "Unauthorized" });

            var account = _accountService.AddSchedule(id, schedule);
            return Ok(account);
        }

        [Authorize]
        [HttpPost("update-schedule/{id}")]
        public ActionResult<AccountResponse> UpdateSchedule(int id, UpdateScheduleRequest schedule)
        {
            // users can update their own account and admins can update any account
            if (id != Account.AccountId && Account.Role != Role.Admin)
                return Unauthorized(new { message = "Unauthorized" });

            var account = _accountService.UpdateSchedule(id, schedule);
            return Ok(account);
        }

        [Authorize]
        [HttpPost("delete-schedule/{id}")]
        public ActionResult<AccountResponse> DeleteSchedule(int id, UpdateScheduleRequest schedule)
        {
            // users can update their own account and admins can update any account
            if (id != Account.AccountId && Account.Role != Role.Admin)
                return Unauthorized(new { message = "Unauthorized" });

            var account = _accountService.DeleteSchedule(id, schedule);
            return Ok(account);
        }
        [Authorize]
        [HttpPut("add-function/{id}")]
        public ActionResult<AccountResponse> AddFunction(int id, UpdateUserFunctionRequest function)
        {
            // users can update their own account and admins can update any account
            if (id != Account.AccountId && Account.Role != Role.Admin)
                return Unauthorized(new { message = "Unauthorized" });

            var account = _accountService.AddFunction(id, function);
            return Ok(account);
        }

        [Authorize]
        [HttpPost("delete-function/{id}")]
        public ActionResult<AccountResponse> DeleteFunction(int id, UpdateUserFunctionRequest function)
        {
            // users can update their own account and admins can update any account
            if (id != Account.AccountId && Account.Role != Role.Admin)
                return Unauthorized(new { message = "Unauthorized" });

            var account = _accountService.DeleteFunction(id, function);
            return Ok(account);
        }

        [Authorize]
        [HttpPost("move-schedule-to-pool/{id}")]
        public ActionResult<AccountResponse> MoveSchedule2Pool(int id, UpdateScheduleRequest scheduleReq)
        {
            // users can update their own account and admins can update any account
            if (id != Account.AccountId && Account.Role != Role.Admin)
                return Unauthorized(new { message = "Unauthorized" });

            var pool = _accountService.MoveSchedule2Pool(id, scheduleReq);
            return Ok(pool);
        }

        [Authorize]
        [HttpPost("get-schedule-from-pool/{id}")]
        public ActionResult<AccountResponse> GetScheduleFromPool(int id, UpdateScheduleRequest scheduleReq)
        {
            // users can update their own account and admins can update any account
            if (id != Account.AccountId && Account.Role != Role.Admin)
                return Unauthorized(new { message = "Unauthorized" });

            var account = _accountService.GetScheduleFromPool(id, scheduleReq);
            if(account == null)
            {
                return NotFound ();
            }
            
            return Ok(account);
        }
        
        [HttpGet("available_schedules/{id}")]
        public ActionResult<SchedulePoolElementsResponse> GetAvailableSchedules(int id)
        {
            var dates = _accountService.GetAvailableSchedules(id);
            return Ok(dates);
        }

        [HttpPost("remove-pool-element/{id}/{email}/{userFunction}")]
        public ActionResult<SchedulePoolElementsResponse> RemovePoolElement(int id, string email, string userFunction)
        {
            var dates = _accountService.RemoveFromPool(id, email, userFunction);
            return Ok(dates);
        }

        [HttpGet("all-available_schedules")]
        public ActionResult<SchedulePoolElementsResponse> GetAllAvailableSchedules(int id)
        {
            var dates = _accountService.GetAllAvailableSchedules();
            return Ok(dates);
        }


        [Authorize]
        [HttpDelete("{id:int}")]
        public IActionResult Delete(int id)
        {
            // users can delete their own account and admins can delete any account
            if (id != Account.AccountId && Account.Role != Role.Admin)
                return Unauthorized(new { message = "Unauthorized" });

            _accountService.Delete(id);
            return Ok(new { message = "Account deleted successfully" });
        }
        [HttpGet("role-configuration")]
        public ActionResult<AuthenticateResponse> RoleConfiguration()
        {
            string s = _appSettings.Roles;
            string[] subs = s.Split(',');
            return Ok(subs);
        }

        // helper methods

        private void setTokenCookie(string token)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = false,
                Expires = DateTime.UtcNow.AddDays(7),
                Secure = true,
                SameSite = SameSiteMode.None

            };
            Response.Cookies.Append("refreshToken", token, cookieOptions);
        }

        private string ipAddress()
        {
            if (Request.Headers.ContainsKey("X-Forwarded-For"))
                return Request.Headers["X-Forwarded-For"];
            else
                return HttpContext.Connection.RemoteIpAddress.MapToIPv4().ToString();
        }
    }
}
