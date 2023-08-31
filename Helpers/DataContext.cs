using log4net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Configuration;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using WebApi.Entities;


namespace WebApi.Helpers
{
    public class DataContext : DbContext
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        public DbSet<SystemInfo> SystemInformation { get; set; }

        public DbSet<Account> Accounts { get; set; }
        public DbSet<Schedule> Schedules { get; set; }

        public DbSet<Function> UserFunctions { get; set; }
        public DbSet<SchedulePoolElement> SchedulePoolElements { get; set; }
        
        public DbSet<RefreshToken> RefreshTokens { get; set; }

        private readonly IConfiguration Configuration;

        public DataContext(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            // connect to sqlite database
            options.UseSqlite(Configuration.GetConnectionString("WebApiDatabase"));
        }
        //protected override void OnModelCreating(ModelBuilder modelBuilder)
        //{
        //    modelBuilder.Entity<Account>().HasMany(e => e.RefreshTokens).WithOne(e => e.Account).IsRequired();
        //}
    }
}