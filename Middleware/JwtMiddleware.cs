using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebApi.Helpers;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Middleware
{
    public class JwtMiddleware
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private readonly RequestDelegate _next;
        private readonly AppSettings _appSettings;

        public JwtMiddleware(RequestDelegate next, IOptions<AppSettings> appSettings)
        {
            _next = next;
            _appSettings = appSettings.Value;
        }

        public async Task Invoke(HttpContext context, DataContext dataContext)
        {
            var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();

            if (token != null)
                await attachAccountToContext(context, dataContext, token);

            await _next(context);
        }

        private async Task attachAccountToContext(HttpContext context, DataContext dataContext, string token)
        {
            try
            {
                // JD Test
                var handler = new JwtSecurityTokenHandler();
                var jsonToken = handler.ReadToken(token);
                var tokenS = jsonToken as JwtSecurityToken;
                bool tokenValid = CheckTokenIsValid(token);
                if (!tokenValid)
                {
                    var jwtSecurityToken = handler.ReadJwtToken(token);
                    var jwtTokenTest = (JwtSecurityToken)jwtSecurityToken;
                    var accountID = int.Parse(jwtTokenTest.Claims.First(x => x.Type == "id").Value);
                    var accountTest = await dataContext.Accounts.FindAsync(accountID);

                    var tokenExpTest = jwtTokenTest.Claims.First(claim => claim.Type.Equals("exp")).Value;
                    var ticksTest = long.Parse(tokenExpTest);
                    var tokenDateTest = DateTimeOffset.FromUnixTimeSeconds(ticksTest).UtcDateTime;
                    log.InfoFormat("JWT expiration(Invalid) date for {0} {1} was {2}", accountTest.FirstName, accountTest.LastName, tokenDateTest.ToLocalTime().ToString());
                    var accountIdTest = int.Parse(jwtTokenTest.Claims.Where(x => x.Type == "id").FirstOrDefault().Value); //int.Parse();
                    var securityToken = handler.ReadToken(token) as JwtSecurityToken;
                    var stringClaimValue = securityToken.Claims.First(claim => claim.Type == "id").Value;
                }
                // JD Test

                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(_appSettings.Secret);

                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    // set clockskew to zero so tokens expire exactly at token expiration time (instead of 5 minutes later)
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
                var accountId = int.Parse(jwtToken.Claims.First(x => x.Type == "id").Value);

                // Attach account to context on successful jwt validation
                context.Items["Account"] = dataContext.Accounts.Include(x => x.RefreshTokens).SingleOrDefault(x => x.AccountId == accountId);
                //context.Items["Account"] = await dataContext.Accounts.Include(x => x.RefreshTokens).FindAsync(accountId);
                //var account = await dataContext.Accounts.FindAsync(accountId);
                log.Info("JWT (valid): for path: " + context.Request.Path);

                var tokenExp = jwtToken.Claims.First(claim => claim.Type.Equals("exp")).Value;
                var ticks = long.Parse(tokenExp);
                var tokenDate = DateTimeOffset.FromUnixTimeSeconds(ticks).UtcDateTime;
                //log.InfoFormat("JWT expiration(Valid) date for {0} {1} is {2}", account.FirstName, account.LastName, tokenDate.ToLocalTime().ToString());

            }
            catch (Exception error)
            {
                // do nothing if jwt validation fails
                // account is not attached to context so request won't have access to secure routes
                var account = context.Items["Account"];
                Console.WriteLine("Failed:" + error);
                log.Info("JWT (expired): for path: " + context.Request.Path);
            }
        }
        public static bool CheckTokenIsValid(string token)
        {
            var tokenTicks = GetTokenExpirationTime(token);
            var tokenDate = DateTimeOffset.FromUnixTimeSeconds(tokenTicks).UtcDateTime;

            var now = DateTime.Now.ToUniversalTime();

            var valid = tokenDate >= now;
            log.Info("JWT expiration date - local time: " + tokenDate.ToLocalTime() + " Now: " + now.ToLocalTime());
            if(!valid)
            {
                log.Info("JWT expiration date (expired): " + tokenDate.ToLocalTime() + " Now: " + now.ToLocalTime());
            }
            return valid;
        }
        public static long GetTokenExpirationTime(string token)
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtSecurityToken = handler.ReadJwtToken(token);
            var tokenExp = jwtSecurityToken.Claims.First(claim => claim.Type.Equals("exp")).Value;
            var ticks = long.Parse(tokenExp);
            return ticks;
        }
    }
}