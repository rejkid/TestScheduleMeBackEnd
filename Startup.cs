using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using WebApi.Helpers;
using WebApi.Middleware;
using WebApi.Services;
using Microsoft.Extensions.Hosting;

using log4net;
using log4net.Config;
using Microsoft.AspNetCore.Authentication.Cookies;
using Google.Apis.Auth.AspNetCore3;
using Google.Apis.Auth.OAuth2;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;
using Google.Apis.Drive.v3;
using System.Collections.Generic;
using Google.Apis.Gmail.v1.Data;
using WebApi.Hub;
using System.Text.Json.Serialization;

[assembly: log4net.Config.XmlConfigurator(ConfigFile = "log4net.config", Watch = true)]
namespace WebApi
{
    public class Startup
    {
        #region log4net
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        #endregion //log4net

        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
             //BasicConfigurator.Configure();
            log.Debug("Program started");
        }

        // add services to the DI container
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<DataContext>();
            services.AddCors();
            //services.AddControllers().AddJsonOptions(x => x.JsonSerializerOptions.IgnoreNullValues = true);
            services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

            services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());
            services.AddSwaggerGen();

            services.AddSignalR();
            
            /* Add authenticate */
            //services.AddAuthentication(options =>
            //{
            //    // This forces challenge results to be handled by Google OpenID Handler, so there's no
            //    // need to add an AccountController that emits challenges for Login.
            //    options.DefaultChallengeScheme = GoogleOpenIdConnectDefaults.AuthenticationScheme;

            //    // This forces forbid results to be handled by Google OpenID Handler, which checks if
            //    // extra scopes are required and does automatic incremental auth.
            //    options.DefaultForbidScheme = GoogleOpenIdConnectDefaults.AuthenticationScheme;

            //    // Default scheme that will handle everything else.
            //    // Once a user is authenticated, the OAuth2 token info is stored in cookies.
            //    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            //})
            //.AddCookie(options =>
            //{
            //    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            //})
            //.AddGoogleOpenIdConnect(options =>
            //{
            //    var secrets = GoogleClientSecrets.FromFile("client_secret.json").Secrets;
            //    options.ClientId = secrets.ClientId;
            //    options.ClientSecret = secrets.ClientSecret;
            //});

            /* End authenticate */


            // configure strongly typed settings object
            services.Configure<AppSettings>(Configuration.GetSection("AppSettings"));

            // configure DI for application services
            services.AddScoped<IAccountService, AccountService>();
            services.AddScoped<IEmailService, EmailService>();
        }

        // configure the HTTP request pipeline
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, DataContext context)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "WebApplication5 v1"));
            }
            // migrate database changes on startup (includes initial db creation)
            context.Database.Migrate();

            // generated swagger json and swagger ui middleware
            app.UseSwagger();
            app.UseSwaggerUI(x => x.SwaggerEndpoint("/swagger/v1/swagger.json", "ASP.NET Core Sign-up and Verification API"));

            app.UseRouting();

            /* Start Adding authentication */
            //app.UseHttpsRedirection();
            //app.UseStaticFiles();

            ////app.UseRouting();

            //app.UseAuthentication();
            //app.UseAuthorization();
            /* End Adding authentication */

            // global cors policy
            app.UseCors(x => x
                .SetIsOriginAllowed(origin => true)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials());

            // global error handler
            app.UseMiddleware<ErrorHandlerMiddleware>();

            // custom jwt auth middleware
            app.UseMiddleware<JwtMiddleware>();

            app.UseEndpoints(x => x.MapControllers());

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHub<MessageHub>("/update");
            });
        }
    }
}
