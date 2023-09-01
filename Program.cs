using AutoMapper.Configuration;
using log4net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;

using WebApi.Helpers;

namespace WebApi
{
    public class Program
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        public static void Main(string[] args)
        {
            CancellationTokenSource tokenSource = new CancellationTokenSource(); // Create a token source.

            // Create an AutoResetEvent to signal the timeout threshold in the
            // timer callback has been reached.
            //var autoEvent = new AutoResetEvent(false);
            IHost ihost = CreateHostBuilder(args).Build();
            //var services = ihost.Services.GetService(typeof(IServiceProvider));

            ScheduleFunctionRemainder scheduleFunctionRemainder = new ScheduleFunctionRemainder(ihost.Services);
            // Create a thread
            Thread backgroundThread = new Thread(new ThreadStart(ScheduleFunctionRemainder.CheckStatus));

            // Start thread
            backgroundThread.Start();

            log.Info("Schedule reminder started");

            //Timer stateTimer = new Timer(scheduleFunctionRemainder.CheckStatus, autoEvent, 0, 1000 * 60 * 5);
            ihost.Run();

            // Stop the timer
            //stateTimer.Change(Timeout.Infinite, Timeout.Infinite); ;
            /* Make autoEvent to signal main thread to finish the job */
            scheduleFunctionRemainder.Terminate();

            // When autoEvent signals , dispose of the timer.
            //autoEvent.WaitOne();
            //stateTimer.Dispose();
            log.Info("\nDestroying timer.");
            log.Info("\nService exited gracefully.");
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                        //.UseUrls("https://192.168.0.19:4000");
                        //.UseUrls("http://192.168.0.19:4000");
                        //.UseUrls("http://localhost:4000");
                        //.UseUrls("https://webapijanusz.azurewebsites.net:4000");
                        //.UseUrls("https://localhost:4000");
                });

    }
}
