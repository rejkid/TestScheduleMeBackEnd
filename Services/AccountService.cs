using AutoMapper;
using BC = BCrypt.Net.BCrypt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using WebApi.Entities;
using WebApi.Helpers;
using WebApi.Models.Accounts;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.WebUtilities;
using System.Diagnostics;
using System.Threading;
using log4net;
using Microsoft.EntityFrameworkCore.Storage;
using System.Security.Policy;
using Microsoft.AspNetCore.SignalR;
using WebApi.Hub;
using System.Runtime.Serialization;
using System.Security.Principal;

namespace WebApi.Services
{
    public interface IAccountService
    {
        AuthenticateResponse Authenticate(AuthenticateRequest model, string ipAddress);
        AuthenticateResponse RefreshToken(string token, string ipAddress);
        void RevokeToken(string token, string ipAddress);
        void Register(RegisterRequest model, string origin);
        void VerifyEmail(VerifyEmailRequest model);
        void ForgotPassword(ForgotPasswordRequest model, string origin);
        void ValidateResetToken(ValidateResetTokenRequest mode);
        void ResetPassword(ResetPasswordRequest model);
        IEnumerable<AccountResponse> GetAll();
        AccountResponse GetById(int id);

        public ScheduleDateTimeResponse GetAllDates();
        public DateFunctionTeamResponse GetTeamsByFunctionForDate(string date);

        AccountResponse Create(CreateRequest model);
        AccountResponse Update(int id, UpdateRequest model);
        public AccountResponse DeleteSchedule(int id, UpdateScheduleRequest scheduleReq);
        public AccountResponse AddSchedule(int id, UpdateScheduleRequest scheduleReq);
        public AccountResponse UpdateSchedule(int id, UpdateScheduleRequest scheduleReq);
        public AccountResponse DeleteFunction(int id, UpdateUserFunctionRequest functionReq);
        public AccountResponse AddFunction(int id, UpdateUserFunctionRequest functionReq);
        //public SchedulePoolElementsResponse ChangeUserAvailability(int id, UpdateScheduleRequest scheduleReq);
        public AccountResponse GetScheduleFromPool(int id, UpdateScheduleRequest scheduleReq);
        public AccountResponse MoveSchedule2Pool(int id, UpdateScheduleRequest scheduleReq);

        public SchedulePoolElementsResponse GetAvailableSchedules(int id);
        public SchedulePoolElementsResponse GetAllAvailableSchedules();

        public SchedulePoolElement RemoveFromPool(int id, string email, string userFunction);



        void Delete(int id);
    }

    public class AccountService : IAccountService
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private readonly DataContext _context;
        private readonly IMapper _mapper;
        private readonly AppSettings _appSettings;
        private readonly IEmailService _emailService;
        public static readonly object lockObject = new object();
        private readonly IHubContext<MessageHub, IMessageHubClient> _hubContext;
        public AccountService(
            DataContext context,
            IMapper mapper,
            IOptions<AppSettings> appSettings,
            IEmailService emailService,
            IHubContext<MessageHub, IMessageHubClient> hubContext)
        {
            _context = context;
            _mapper = mapper;
            _appSettings = appSettings.Value;
            _emailService = emailService;
            _hubContext = hubContext;
        }

        public AuthenticateResponse Authenticate(AuthenticateRequest model, string ipAddress)
        {
            log.Info("Authenticate before locking");
            Monitor.Enter(lockObject);
            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var account = _context.Accounts.Include(x => x.RefreshTokens).SingleOrDefault(x => x.Email == model.Email && x.DOB == model.Dob);

                    if (account == null || !account.IsVerified || !BC.Verify(model.Password, account.PasswordHash))
                        throw new AppException("Email, DOB or password is incorrect");

                    // authentication successful so generate jwt and refresh tokens
                    var jwtToken = generateJwtToken(account);

                    var refreshToken = generateRefreshToken(ipAddress);
                    account.RefreshTokens.Add(refreshToken);

                    // remove old refresh tokens from account
                    removeOldRefreshTokens(account);

                    // save changes to db
                    _context.Update(account);
                    _context.SaveChanges();

                    var response = _mapper.Map<AuthenticateResponse>(account);
                    response.JwtToken = jwtToken;
                    response.RefreshToken = refreshToken.Token;

                    transaction.Commit();
                    return response;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    log.Error(Thread.CurrentThread.Name + "Error occurred:" + ex.Message);
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    log.Info("Authenticate after locking");
                }
            }
        }

        public AuthenticateResponse RefreshToken(string token, string ipAddress)
        {
            log.Info("RefreshToken before locking");
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var (refreshToken, account) = getRefreshToken(token);

                    log.InfoFormat("Old RefreshToken= {0} for {1} {2}",
                        refreshToken.Token,
                        account.FirstName,
                        account.LastName);

                    // replace old refresh token with a new one and save
                    var newRefreshToken = generateRefreshToken(ipAddress);
                    refreshToken.Revoked = DateTime.UtcNow;
                    refreshToken.RevokedByIp = ipAddress;
                    refreshToken.ReplacedByToken = newRefreshToken.Token;
                    account.RefreshTokens.Add(newRefreshToken);

                    removeOldRefreshTokens(account);

                    log.InfoFormat("New RefreshToken= {0} for {1} {2}",
                        newRefreshToken.Token,
                        account.FirstName,
                        account.LastName);

                    _context.Update(account);
                    _context.SaveChanges();

                    // generate new jwt
                    var jwtToken = generateJwtToken(account);

                    var response = _mapper.Map<AuthenticateResponse>(account);
                    response.JwtToken = jwtToken;
                    response.RefreshToken = newRefreshToken.Token;

                    transaction.Commit();
                    return response;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    log.Error("RefreshToken:" + ex.Message);
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    log.Info("RefreshToken after locking");
                }
            }
        }

        public void RevokeToken(string token, string ipAddress)
        {
            log.Info("RevokeToken before locking");
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var (refreshToken, account) = getRefreshToken(token);

                    // revoke token and save
                    refreshToken.Revoked = DateTime.UtcNow;
                    refreshToken.RevokedByIp = ipAddress;
                    _context.Update(account);
                    _context.SaveChanges();
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    log.Info("RevokeToken after locking");
                }
            }
        }

        public void Register(RegisterRequest model, string origin)
        {
            log.Info("Register before locking");
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    // validate
                    if (_context.Accounts.Any(x => x.Email == model.Email && x.DOB == model.Dob))
                    {
                        var clientTimeZoneId = _appSettings.ClientTimeZoneId;
                        var scheduleDate = TimeZoneInfo.ConvertTimeFromUtc(model.Dob, TimeZoneInfo.FindSystemTimeZoneById(clientTimeZoneId));


                        // send already registered error in email to prevent account enumeration
                        sendAlreadyRegisteredEmail(model.Email, scheduleDate.ToString(ConstantsDefined.DateTimeFormat), origin);
                        transaction.Commit();
                        return;
                    }

                    // map model to new account object
                    var account = _mapper.Map<Account>(model);

                    // first registered account is an admin
                    var isFirstAccount = _context.Accounts.Count() == 0;
                    account.Role = isFirstAccount ? Role.Admin : Role.User;
                    account.Created = DateTime.UtcNow;
                    account.VerificationToken = randomTokenString();

                    // hash password
                    account.PasswordHash = BC.HashPassword(model.Password);

                    // save account
                    _context.Accounts.Add(account);
                    _context.SaveChanges();

                    // send email
                    sendVerificationEmail(account, origin);

                    transaction.Commit();
                    log.WarnFormat("Registration successful for = {0} ", model.Email);
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    log.Error("Error during registering:" + ex.Message);
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    log.Info("Register after locking");
                }
            }
        }

        public void VerifyEmail(VerifyEmailRequest model)
        {
            log.Info("VerifyEmail before locking");
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    // Convert DOB string to DateTime
                    DateTime dateTime = DateTime.ParseExact(model.Dob, ConstantsDefined.DateTimeFormat, System.Globalization.CultureInfo.InvariantCulture);

                    // Convert client DOB to server date time
                    var clientTimeZoneId = _appSettings.ClientTimeZoneId;
                    DateTime dob = TimeZoneInfo.ConvertTimeToUtc(dateTime, TimeZoneInfo.FindSystemTimeZoneById(clientTimeZoneId));

                    var account = _context.Accounts.SingleOrDefault(x => x.VerificationToken == model.Token && (DateTime.Compare(x.DOB, dob) == 0));

                    if (account == null) throw new AppException("Verification failed");

                    account.Verified = DateTime.UtcNow;
                    account.VerificationToken = null;

                    _context.Accounts.Update(account);
                    _context.SaveChanges();
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name);
                    log.Error("Error during veryfication:" + ex.Message);
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    log.Info("VerifyEmail after locking");
                }
            }
        }

        public void ForgotPassword(ForgotPasswordRequest model, string origin)
        {
            log.Info("ForgotPassword before locking");
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var account = _context.Accounts.SingleOrDefault(x => x.Email == model.Email && x.DOB == model.Dob);

                    // always return ok response to prevent email enumeration
                    if (account == null)
                    {
                        throw new AppException("Email or DOB is incorrect");
                        //transaction.Commit();
                        //return;
                    }

                    // create reset token that expires after 1 day
                    account.ResetToken = randomTokenString();
                    account.ResetTokenExpires = DateTime.UtcNow.AddDays(1);

                    _context.Accounts.Update(account);
                    _context.SaveChanges();

                    // send email
                    sendPasswordResetEmail(account, origin);

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    log.Info("ForgotPassword after locking");
                }
            }
        }

        public void ValidateResetToken(ValidateResetTokenRequest model)
        {
            log.Info("ValidateResetToken before locking");
            Monitor.Enter(lockObject);
            try
            {
                // Convert model.Dob string to DateTime
                DateTime dateTime = DateTime.ParseExact(model.Dob, ConstantsDefined.DateTimeFormat, System.Globalization.CultureInfo.InvariantCulture);

                // Convert client DOB to server date time
                var clientTimeZoneId = _appSettings.ClientTimeZoneId;
                DateTime dob = TimeZoneInfo.ConvertTimeToUtc(dateTime, TimeZoneInfo.FindSystemTimeZoneById(clientTimeZoneId));

                var account = _context.Accounts.SingleOrDefault(x => x.ResetToken == model.Token && (DateTime.Compare(x.DOB, dob) == 0) && x.ResetTokenExpires > DateTime.UtcNow);

                if (account == null)
                    throw new AppException("Invalid token");
            }
            catch (Exception ex)
            {
                log.InfoFormat("Exception in ValidateResetToken "+ex.Message);
                Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                throw ex;
            }
            finally
            {
                Monitor.Exit(lockObject);
                log.Info("ValidateResetToken after locking");
            }
        }

        public void ResetPassword(ResetPasswordRequest model)
        {
            log.Info("ResetPassword before locking");
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var account = _context.Accounts.SingleOrDefault(x =>
                        x.ResetToken == model.Token &&
                        x.ResetTokenExpires > DateTime.UtcNow);

                    if (account == null)
                        throw new AppException("Invalid token");

                    // update password and remove reset token
                    account.PasswordHash = BC.HashPassword(model.Password);
                    account.PasswordReset = DateTime.UtcNow;
                    account.ResetToken = null;
                    account.ResetTokenExpires = null;

                    _context.Accounts.Update(account);
                    _context.SaveChanges();

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    log.Info("ResetPassword after locking");
                }
            }
        }

        public IEnumerable<AccountResponse> GetAll()
        {
            log.Info("GetAll before locking");
            Monitor.Enter(lockObject);

            try
            {
                var accounts = _context.Accounts;
                return _mapper.Map<IList<AccountResponse>>(accounts);
            }
            catch (Exception ex)
            {
                Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                throw ex;
            }
            finally
            {
                Monitor.Exit(lockObject);
                log.Info("GetAll after locking");
            }
        }

        public ScheduleDateTimeResponse GetAllDates()
        {
            log.Info("GetAllDates before locking");
            Monitor.Enter(lockObject);
            try
            {
                ScheduleDateTimeResponse response = new ScheduleDateTimeResponse();
                response.ScheduleDateTimes = new List<ScheduleDateTime>();

                var accounts = _context.Accounts;
                var accountAll = _context.Accounts.Include(x => x.Schedules).ToList();
                foreach (var item in accountAll)
                {
                    foreach (var schedule in item.Schedules)
                    {
                        Boolean found = false;
                        foreach (var dt in response.ScheduleDateTimes)
                        {
                            if (dt.Date == schedule.Date)
                            {
                                found = true; // DateTime already exists - break the for loop
                                break;
                            }
                        }
                        if (!found)
                        {
                            ScheduleDateTime sdt = new ScheduleDateTime();
                            sdt.Date = schedule.Date;
                            response.ScheduleDateTimes.Add(sdt);
                        }
                    }
                }
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                throw ex;
            }
            finally
            {
                Monitor.Exit(lockObject);
                log.Info("GetAllDates after locking");
            }
        }

        public DateFunctionTeamResponse GetTeamsByFunctionForDate(string dateStr)
        {
            log.Info("GetTeamsByFunctionForDate before locking");
            Monitor.Enter(lockObject);

            try
            {
                var accountAll = _context.Accounts.Include(x => x.Schedules).ToList();
                var dateTime = DateTime.Parse(dateStr);

                var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);

                log.InfoFormat("Date requested string {0} parsed value {1} offset {2}",
                                dateStr,
                                dateTime,
                                offset);

                DateFunctionTeamResponse response = new DateFunctionTeamResponse();
                response.DateFunctionTeams = new List<DateFunctionTeam>();

                foreach (var account in accountAll)
                {
                    foreach (var schedule in account.Schedules)
                    {
                        DateFunctionTeam team = null;

                        if (schedule.Date == dateTime)
                        {
                            // Find existing team for the date and function
                            foreach (var item in response.DateFunctionTeams)
                            {
                                if (schedule.Date == item.Date && item.Function == schedule.UserFunction)
                                {
                                    team = item;
                                    break;
                                }
                            }
                            if (team == null)
                            {
                                team = new DateFunctionTeam(dateTime, schedule.UserFunction);
                                response.DateFunctionTeams.Add(team);
                            }

                            User user = new User();
                            user = _mapper.Map<User>(account);
                            user.Function = schedule.UserFunction;
                            user.UserAvailability = schedule.UserAvailability;
                            user.ScheduleGroup = schedule.ScheduleGroup;
                            team.Users.Add(user);
                        }
                    }
                }
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                throw ex;
            }
            finally
            {
                Monitor.Exit(lockObject);
                log.Info("GetTeamsByFunctionForDate after locking");
            }
        }
        public AccountResponse GetById(int id)
        {
            log.Info("GetById before locking");
            Monitor.Enter(lockObject);

            try
            {
                var account = getAccount(id);

                return _mapper.Map<AccountResponse>(account);
            }
            catch (Exception ex)
            {
                Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                throw ex;
            }
            finally
            {
                Monitor.Exit(lockObject);
                log.Info("GetById after locking");
            }
        }

        public AccountResponse Create(CreateRequest model)
        {
            log.Info("Create before locking");
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    // validate
                    if (_context.Accounts.Any(x => x.Email == model.Email && x.DOB == model.Dob))
                        throw new AppException($"Email '{model.Email}' DOB: '{model.Dob}' is already registered");

                    // map model to new account object
                    var account = _mapper.Map<Account>(model);
                    account.Created = DateTime.UtcNow;
                    account.Verified = DateTime.UtcNow;

                    // hash password
                    account.PasswordHash = BC.HashPassword(model.Password);

                    // save account
                    _context.Accounts.Add(account);
                    _context.SaveChanges();

                    AccountResponse response = _mapper.Map<AccountResponse>(account);
                    transaction.Commit();

                    return response;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    Console.WriteLine(Thread.CurrentThread.Name + " Exit from critical section");
                    log.Info("Create after locking");
                }
            }
        }

        public AccountResponse Update(int id, UpdateRequest model)
        {
            log.Info("Update before locking"); ;
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var account = getAccount(id);
                    // validate
                    if (account.Email != model.Email && _context.Accounts.Any(x => x.Email == model.Email && x.DOB == model.Dob))
                        throw new AppException($"Email '{model.Email}' is already taken");

                    // hash password if it was entered
                    if (!string.IsNullOrEmpty(model.Password))
                        account.PasswordHash = BC.HashPassword(model.Password);

                    _mapper.Map(model, account);

                    account.Updated = DateTime.UtcNow;
                    _context.Accounts.Update(account);
                    _context.SaveChanges();

                    AccountResponse response = _mapper.Map<AccountResponse>(account);

                    transaction.Commit();
                    return response;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    log.Info("Update after locking"); ;
                }
            }
        }
        public AccountResponse DeleteSchedule(int id, UpdateScheduleRequest scheduleReq)
        {
            log.InfoFormat(Thread.CurrentThread.Name + "Entering critical section");
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var account = getAccount(id);

                    Schedule toRemove = null;

                    foreach (var item in account.Schedules)
                    {
                        if (DateTime.Compare(item.Date, scheduleReq.Date) == 0 && item.UserFunction == scheduleReq.UserFunction)
                        {
                            toRemove = item;
                            break; // Found
                        }
                    }
                    if (toRemove != null)
                    {
                        _context.Schedules.RemoveRange(toRemove);
                    }
                    else
                    {
                        log.Info("DeleteSchedule got NULL from Schedules");
                        throw new AppException("The schedule has been already deleted");
                    }

                    account.Updated = DateTime.UtcNow;
                    _context.SaveChanges();
                    _hubContext.Clients.All.SendUpdate(id);

                    AccountResponse response = _mapper.Map<AccountResponse>(account);

                    transaction.Commit();

                    return response;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    Console.WriteLine(Thread.CurrentThread.Name + " Exit from critical section");
                }
            }
        }

        public AccountResponse AddSchedule(int id, UpdateScheduleRequest scheduleReq)
        {
            log.Info("AddSchedule before locking");
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var account = getAccount(id);
                    var newSchedule = new Schedule();
                    newSchedule = _mapper.Map<Schedule>(scheduleReq);
                    account.Schedules.Add(newSchedule);
                    _context.Accounts.Update(account);
                    _context.SaveChanges();
                    _hubContext.Clients.All.SendUpdate(id);

                    AccountResponse response = _mapper.Map<AccountResponse>(account);

                    transaction.Commit();

                    return response;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    Console.WriteLine(Thread.CurrentThread.Name + " Exit from critical section");
                    log.Info("AddSchedule after locking");
                }
            }
        }

        public AccountResponse UpdateSchedule(int id, UpdateScheduleRequest scheduleReq)
        {
            log.Info("UpdateSchedule before locking");
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var account = getAccount(id);
                    
                    foreach (var schedule in account.Schedules)
                    {
                        if(schedule.Date.CompareTo(scheduleReq.Date) == 0 && schedule.UserFunction == scheduleReq.UserFunction)
                        {
                            schedule.Date = scheduleReq.NewDate;
                            schedule.UserFunction = scheduleReq.NewUserFunction;

                            // Reset notification flags
                            schedule.NotifiedWeekBefore = false;
                            schedule.NotifiedThreeDaysBefore = false;
                            break;
                        }
                    }
                    _context.Accounts.Update(account);
                    _context.SaveChanges();
                    _hubContext.Clients.All.SendUpdate(id);

                    AccountResponse response = _mapper.Map<AccountResponse>(account);

                    transaction.Commit();

                    return response;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    Console.WriteLine(Thread.CurrentThread.Name + " Exit from critical section");
                    log.Info("UpdateSchedule after locking");
                }
            }
        }
        public AccountResponse DeleteFunction(int id, UpdateUserFunctionRequest functionReq)
        {
            log.Info("DeleteFunction before locking");
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var account = getAccount(id);

                    Function toRemove = null;
                    // Purge all schedules & UserFunctions  - we don't know which were changed
                    foreach (var item in account.UserFunctions)
                    {
                        if (item.UserFunction == functionReq.UserFunction)
                        {
                            toRemove = item;
                            break; // Found
                        }
                    }
                    if (toRemove != null)
                    {
                        _context.UserFunctions.RemoveRange(toRemove);
                    }

                    account.Updated = DateTime.UtcNow;
                    _context.SaveChanges();
                    transaction.Commit();

                    return _mapper.Map<AccountResponse>(account);
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    log.Info("DeleteFunction after locking"); ;
                }
            }
        }

        public AccountResponse AddFunction(int id, UpdateUserFunctionRequest functionReq)
        {
            log.Info("AddFunction before locking");
            Monitor.Enter(lockObject);
            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var account = getAccount(id);
                    var newFunction = new Function();
                    newFunction = _mapper.Map<Function>(functionReq);
                    account.UserFunctions.Add(newFunction);
                    _context.Accounts.Update(account);
                    _context.SaveChanges();
                    transaction.Commit();

                    return _mapper.Map<AccountResponse>(account);
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    log.Info("AddFunction after locking");
                }
            }
        }

        /*
        User functions
        */
        public AccountResponse MoveSchedule2Pool(int id, UpdateScheduleRequest scheduleReq)
        {
            log.Info("MoveSchedule2Pool before locking");
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var autEmail = bool.Parse(_appSettings.autoEmail);

                    var account = getAccount(id);
                    log.InfoFormat("MoveSchedule2Pool before locking for {0}. Date {1} function {2}",
                        account.FirstName, scheduleReq.Date, scheduleReq.UserFunction);

                    Schedule toRemove = null;

                    foreach (var item in account.Schedules)
                    {

                        if (DateTime.Compare(item.Date, scheduleReq.Date) == 0 && item.UserFunction == scheduleReq.UserFunction)
                        {
                            toRemove = item;
                            break; // Found
                        }
                    }
                    if (toRemove != null)
                    {
                        log.Info("MoveSchedule2Pool putting: " + scheduleReq.UserFunction + " to pool");
                        PushToPool(account, scheduleReq);

                        account.Schedules.Remove(toRemove);
                        _context.Schedules.RemoveRange(toRemove); // To remove from DB
                        account.Updated = DateTime.UtcNow;
                        _context.Accounts.Update(account);
                        _context.SaveChanges();
                        _hubContext.Clients.All.SendUpdate(id);

                        if (autEmail)
                        {
                            SendEmail2AllRolesAndAdmins(account, toRemove);
                        }
                    }
                    else
                    {
                        log.WarnFormat("Schedule did not exist in the schdule list for {0}. Date {1} function {2}",
                            account.FirstName, scheduleReq.Date, scheduleReq.UserFunction);
                        throw new AppException("The schedule has been already removed");
                    }
                    transaction.Commit();
                    return _mapper.Map<AccountResponse>(account);
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    Console.WriteLine(Thread.CurrentThread.Name + " Exit from critical section");
                    log.Info("MoveSchedule2Pool after locking");
                }
            }
        }

        public AccountResponse GetScheduleFromPool(int id, UpdateScheduleRequest scheduleReq)
        {

            log.Info("GetScheduleFromPool before locking");
            Monitor.Enter(lockObject);
            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    log.Info("MoveSchedule2Pool removing: " + scheduleReq.UserFunction + " from pool");

                    var account = getAccount(id);

                    var poolElement = PopFromPool(account, scheduleReq);

                    if (poolElement != null)
                    {
                        // Schedule not found in the current schedules - create one
                        Schedule schedule = new Schedule();
                        schedule.Date = poolElement.Date;
                        schedule.UserFunction = poolElement.UserFunction;
                        schedule.UserAvailability = scheduleReq.UserAvailability;
                        schedule.Required = scheduleReq.Required;
                        schedule.ScheduleGroup = scheduleReq.ScheduleGroup;


                        account.Schedules.Add(schedule);
                        _context.Accounts.Update(account);
                        _context.SaveChanges();
                        _hubContext.Clients.All.SendUpdate(id);

                        AccountResponse response = _mapper.Map<AccountResponse>(account);

                        transaction.Commit();

                        return response;
                    }
                    else
                    {
                        // Pool element not found - do nothing for now
                        log.Info("GetScheduleFromPool got NULL from Pool elements");
                        account = null;
                        throw new AppException("The schedule has been already taken");
                    }
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                    throw ex;
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    Console.WriteLine(Thread.CurrentThread.Name + " Exit from critical section");
                    log.Info("GetScheduleFromPool after locking");
                }
            }
        }
        public SchedulePoolElementsResponse GetAllAvailableSchedules()
        {
            SchedulePoolElementsResponse response = new SchedulePoolElementsResponse();
            log.Info("GetAllAvailableSchedules before locking");
            Monitor.Enter(lockObject);
            try
            {
                response.SchedulePoolElements = _context.SchedulePoolElements.ToList();
            }
            finally
            {
                Monitor.Exit(lockObject);
                log.Info("GetAllAvailableSchedules after locking");
            }

            return response;
        }

        public SchedulePoolElementsResponse GetAvailableSchedules(int id)
        {
            SchedulePoolElementsResponse response = new SchedulePoolElementsResponse();
            log.Info("GetAvailableSchedules before locking");
            Monitor.Enter(lockObject);
            try
            {
                var account = getAccount(id);
                List<SchedulePoolElement> list = new List<SchedulePoolElement>();

                foreach (var poolElement in _context.SchedulePoolElements.ToList())
                {
                    foreach (var function in account.UserFunctions)
                    {
                        if (function.UserFunction == poolElement.UserFunction)
                        {
                            list.Add(poolElement);
                            break;
                        }
                    }
                }
                response.SchedulePoolElements = list;
            }
            finally
            {
                Monitor.Exit(lockObject);
                log.Info("GetAvailableSchedules after locking");
            }
            return response;
        }
        public SchedulePoolElement RemoveFromPool(int id, string email, string userFunction)
        {
            log.Info("GetScheduleFromPool before locking");
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var schedulePoolAll = _context.SchedulePoolElements.ToList();
                    SchedulePoolElement poolElement = null;
                    foreach (var elem in schedulePoolAll)
                    {
                        if (id == elem.Id && email == elem.Email && userFunction == elem.UserFunction)
                        {
                            poolElement = elem;
                            break;
                        }
                    }
                    if (poolElement != null)
                    {
                        _context.SchedulePoolElements.Remove(poolElement);
                        _context.SaveChanges();
                        transaction.Commit();
                        return poolElement;
                    }
                    else
                    {
                        transaction.Commit();
                        return null;
                    }
                }
                catch (Exception )
                {
                    transaction.Rollback();
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    log.Info("GetScheduleFromPool after locking");
                }
            }
            return null;
        }
        public void Delete(int id)
        {
            log.Info("Delete before locking");
            Monitor.Enter(lockObject);

            using (IDbContextTransaction transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var account = getAccount(id);

                    // Purge all schedules for the account
                    //foreach (var item in account.Schedules)
                    //{
                    //    _context.Schedules.Remove(item);
                    //}
                    //foreach (var item in account.UserFunctions)
                    //{
                    //    _context.UserFunctions.Remove(item);
                    //}
                    //foreach (var item in account.RefreshTokens)
                    //{
                    //    _context.RefreshTokens.Remove(item);
                    //}

                    // Remove children
                    account.Schedules.Clear();
                    account.UserFunctions.Clear();
                    account.RefreshTokens.Clear();

                    _context.Accounts.Remove(account);
                    _context.SaveChanges();
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    log.Error("Delete failed"+ ex.Message);
                    Console.WriteLine(Thread.CurrentThread.Name + "Error occurred.");
                }
                finally
                {
                    Monitor.Exit(lockObject);
                    log.Info("Delete after locking");
                }
            }
        }


        /* Private helper functions */
        private SchedulePoolElement PopFromPool(Account account, UpdateScheduleRequest item)
        {
            var schedulePoolAll = _context.SchedulePoolElements.ToList();
            SchedulePoolElement poolElement = null;
            foreach (var elem in schedulePoolAll)
            {
                // 
                if (item.Date == elem.Date && /* account.Email == elem.Email && */ item.UserFunction == elem.UserFunction)
                {
                    poolElement = elem;
                    break;
                }
            }
            if (poolElement != null)
            {
                _context.SchedulePoolElements.Remove(poolElement);
                _context.SaveChanges();
                return poolElement;
            }
            else
            {
                return null;
            }
        }

        private void PushToPool(Account account, UpdateScheduleRequest item)
        {
            var newPoolElement = new SchedulePoolElement();
            newPoolElement = _mapper.Map<SchedulePoolElement>(item);
            newPoolElement.Email = account.Email;
            newPoolElement.ScheduleGroup = item.ScheduleGroup;
            _context.SchedulePoolElements.Add(newPoolElement);
            _context.SaveChanges();
        }
        private void SendEmail2AllRolesAndAdmins(Account a, Schedule schedule)
        {
            var accountAll = _context.Accounts.ToList();
            foreach (var account in accountAll)
            {
                var clientTimeZoneId = _appSettings.ClientTimeZoneId;
                var scheduleDate = TimeZoneInfo.ConvertTimeFromUtc(schedule.Date, TimeZoneInfo.FindSystemTimeZoneById(clientTimeZoneId));

                if (account.Role == Role.Admin)
                {
                    string message = $@"<i>{a.FirstName} {a.LastName}</i> is unable to attend their duties on " + scheduleDate.ToString(ConstantsDefined.DateTimeFormat);
                    string subject = $@"Warning Administrator: {account.FirstName} {account.LastName}, {schedule.UserFunction}" + " is needed";
                    _emailService.Send(
                        to: account.Email,
                        subject: subject,
                        html: message
                    );
                }

                foreach (var f in account.UserFunctions)
                {
                    if (f.UserFunction == schedule.UserFunction || f.UserFunction == schedule.UserFunction) // TODO second or to be removed
                    {
                        string message = $@"<i>{a.FirstName} {a.LastName}</i> is unable to attend their duties on " + scheduleDate.ToString(ConstantsDefined.DateTimeFormat);
                        string subject = $@"{account.FirstName} {account.LastName}, {f.UserFunction}" + " is needed";
                        _emailService.Send(
                            to: account.Email,
                            subject: subject,
                            html: message
                        );
                        break;
                    }
                }
            }
        }
        // helper methods

        private Account getAccount(int id)
        {
            Account account = null;
            var accountAll = _context.Accounts.Include(x => x.RefreshTokens).Include(x => x.Schedules).Include(x => x.UserFunctions)
                    .ToList();
            account = accountAll.Find(x => x.AccountId == id);
            if (account == null)
            {
                throw new KeyNotFoundException("Account not found");
            }
            return account;
        }

        private (RefreshToken, Account) getRefreshToken(string token)
        {

            var account = _context.Accounts.Include(x => x.RefreshTokens).SingleOrDefault(u => u.RefreshTokens.Any(t => t.Token == token));
            //var account = _context.Accounts.SingleOrDefault(u => u.RefreshTokens.Any(t => t.Token == token));
            if (account == null)
            {
                Console.WriteLine("Exception thrown");
                throw new AppException("Invalid token");
            }
            var refreshToken = account.RefreshTokens.Single(x => x.Token == token);
            if (!refreshToken.IsActive) throw new AppException("Invalid token");
            return (refreshToken, account);
        }

        private string generateJwtToken(Account account)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_appSettings.Secret);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim("id", account.AccountId.ToString()) }),
                Expires = DateTime.UtcNow.AddMinutes(15),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor) as JwtSecurityToken;

            // JD
            var tokenExp = token.Claims.First(claim => claim.Type.Equals("exp")).Value;
            var ticks = long.Parse(tokenExp);
            var tokenDate = DateTimeOffset.FromUnixTimeSeconds(ticks).UtcDateTime;
            var Expires = DateTime.Now.AddMinutes(15);
            //log.Info("JWT expiration date for : " + account.FirstName + + tokenDate.ToLocalTime().ToString());
            log.InfoFormat("JWT Next expiration date for {0} {1} is {2}", account.FirstName, account.LastName, tokenDate.ToLocalTime().ToString());
            // JD
            return tokenHandler.WriteToken(token);
        }
        /* JD Test*/
        private Task<string> GetJWTToken(string user)
        {

            var now = DateTime.UtcNow;
            //constructing part 1: header.Encode()
            JwtHeader jwtHeader = new JwtHeader();
            var sha512 = new HMACSHA512();
            jwtHeader.Add("alg", sha512);
            var partOne = jwtHeader.Base64UrlEncode();

            //constructing part 2: payload.Encode  
            JwtPayload payload = new JwtPayload();
            payload.Add("sub", user);
            payload.Add("exp", ConvertToUnixTimestamp(now.AddMinutes(15)));
            payload.Add("nbf", ConvertToUnixTimestamp(now));
            payload.Add("iat", ConvertToUnixTimestamp(now));
            var partTwo = payload.Base64UrlEncode();

            //constructing part 3: HS512(part1 + "." + part2, key)
            var tobeHashed = string.Join(".", partOne, partTwo);
            var sha = new HMACSHA512(Encoding.UTF8.GetBytes(_appSettings.Secret));
            var hashedByteArray = sha.ComputeHash(Encoding.UTF8.GetBytes(tobeHashed));

            //You need to base64UrlEncode the signature hash value
            var partThree = Base64UrlEncode(hashedByteArray);

            //Now construct the token
            var tokenString = string.Join(".", tobeHashed, partThree);

            //await was not used so no need for `async` keyword. Just return task
            return Task.FromResult(tokenString);
        }
        private static double ConvertToUnixTimestamp(DateTime date)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan diff = date.ToUniversalTime() - origin;
            return Math.Floor(diff.TotalSeconds);
        }
        // from JWT spec
        private static string Base64UrlEncode(byte[] input)
        {
            var output = Convert.ToBase64String(input);
            output = output.Split('=')[0]; // Remove any trailing '='s
            output = output.Replace('+', '-'); // 62nd char of encoding
            output = output.Replace('/', '_'); // 63rd char of encoding
            return output;
        }


        private RefreshToken generateRefreshToken(string ipAddress)
        {
            return new RefreshToken
            {
                Token = randomTokenString(),
                Expires = DateTime.UtcNow.AddDays(7),
                Created = DateTime.UtcNow,
                CreatedByIp = ipAddress
            };
        }

        private void removeOldRefreshTokens(Account account)
        {
            account.RefreshTokens.RemoveAll(x =>
                !x.IsActive &&
                x.Created.AddDays(_appSettings.RefreshTokenTTL) <= DateTime.UtcNow);
        }

        private string randomTokenString()
        {
            using var rng = RandomNumberGenerator.Create();
            //using var rngCryptoServiceProvider = new RNGCryptoServiceProvider(); // OLD
            var randomBytes = new byte[40];

            rng.GetBytes(randomBytes);
            //rngCryptoServiceProvider.GetBytes(randomBytes); // OLD

            // convert random bytes to hex string
            return BitConverter.ToString(randomBytes).Replace("-", "");
        }

        private void sendVerificationEmail(Account account, string origin)
        {
            string message;

            var clientTimeZoneId = _appSettings.ClientTimeZoneId;
            var scheduleDate = TimeZoneInfo.ConvertTimeFromUtc(account.DOB, TimeZoneInfo.FindSystemTimeZoneById(clientTimeZoneId));

            if (!string.IsNullOrEmpty(origin))
            {
                var verifyUrl = $"{origin}/account/verify-email?token={account.VerificationToken}&DOB={scheduleDate.ToString(ConstantsDefined.DateTimeFormat)}";
                message = $@"<p>Please click the below link to verify your email address:</p>
                             <p><a href=""{verifyUrl}"">{verifyUrl}</a></p>";
            }
            else
            {
                message = $@"<p>Please use the below token to verify your email address with the <code>/accounts/verify-email</code> api route:</p>
                             <p><code>{account.VerificationToken+"&"+ scheduleDate.ToString(ConstantsDefined.DateTimeFormat)}</code></p>";
            }

            _emailService.Send(
                to: account.Email,
                subject: "Sign-up Verification API - Verify Email",
                html: $@"<h4>Verify Email</h4>
                         <p>Thanks for registering!</p>
                         {message}"
            );
        }

        private void sendAlreadyRegisteredEmail(string email, string dob, string origin)
        {
            string message;

            if (!string.IsNullOrEmpty(origin))
                message = $@"<p>If you don't know your password please visit the <a href=""{origin}/account/forgot-password"">forgot password</a> page.</p>";
            else
                message = "<p>If you don't know your password you can reset it via the <code>/accounts/forgot-password</code> api route.</p>";

            _emailService.Send(
                to: email,
                subject: "Sign-up Verification API - Email Already Registered",
                html: $@"<h4>Email Already Registered</h4>
                         <p>Your email <strong>{email}</strong> and DOB: {dob} is already registered.</p>
                         {message}"
            );
        }


        private void sendPasswordResetEmail(Account account, string origin)
        {
            string message;

            var clientTimeZoneId = _appSettings.ClientTimeZoneId;
            var scheduleDate = TimeZoneInfo.ConvertTimeFromUtc(account.DOB, TimeZoneInfo.FindSystemTimeZoneById(clientTimeZoneId));

            if (!string.IsNullOrEmpty(origin))
            {
                var resetUrl = $"{origin}/account/reset-password?token={account.ResetToken}&DOB={System.Web.HttpUtility.UrlEncode(scheduleDate.ToString(ConstantsDefined.DateTimeFormat))}";
                message = $@"<p>Please click the below link to reset your password, the link will be valid for 1 day:</p>
                             <p><a href=""{resetUrl}"">{resetUrl}</a></p>";
            }
            else
            {
                message = $@"<p>Please use the below token to reset your password with the <code>/accounts/reset-password</code> api route:</p>
                             <p><code>{account.ResetToken + "&" + account.DOB}</code></p>";
            }

            _emailService.Send(
                to: account.Email,
                subject: "Sign-up Verification API - Reset Password",
                html: $@"<h4>Reset Password Email</h4>
                         {message}"
            );
        }
    }
}
