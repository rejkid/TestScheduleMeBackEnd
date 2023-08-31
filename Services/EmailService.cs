using Google.Apis.Auth.OAuth2;
using log4net;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using System.Security.Cryptography;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;
using WebApi.Helpers;
using System.IO;
using System.Threading.Tasks;
using Org.BouncyCastle.Security;
using GenerateEncryptedPassword;

namespace WebApi.Services
{
    public interface IEmailService
    {
        void Send(string to, string subject, string html, string from = null);
    }

    public class EmailService : IEmailService
    {

        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private readonly AppSettings _appSettings;
        private string Password="";

        public EmailService(IOptions<AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
            Password = AesOperation.DecryptString(GenerateEncryptedPassword.Program.symetricKey, _appSettings.SmtpPass);
        }

        public void Send(string to, string subject, string html, string from = null)
        {
            try
            {
                // create message
                var email = new MimeMessage();
                email.From.Add(MailboxAddress.Parse(from ?? _appSettings.EmailFrom));
                email.To.Add(MailboxAddress.Parse(to));
                email.Subject = subject;
                email.Body = new TextPart(TextFormat.Html) { Text = html };


                // send email
                using var smtp = new SmtpClient();


                smtp.Connect(_appSettings.SmtpHost, _appSettings.SmtpPort, SecureSocketOptions.StartTls);
                
                smtp.Authenticate(_appSettings.SmtpUser, Password);
                smtp.Send(email);
                smtp.Disconnect(true);
                log.InfoFormat("Success sending e-mail \n Subject: {0} Message: {1} to: {2}",
                    subject,
                    html,
                    to);
            }
            catch (System.Exception ex)
            {
                log.InfoFormat("Failure sending e-mail \n Subject: {0} Message: {1} to: {2}",
                    subject,
                    html,
                    to);
                log.Warn(ex);

            }
        }
    }
}