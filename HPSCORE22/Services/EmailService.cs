// Services/EmailService.cs
// Port of the old SendEmail.SendSingleMail. Settings come from appsettings.json -> "Smtp".
// Program.cs:  builder.Services.AddSingleton<IEmailService, EmailService>();
//using Admin360.Interface;
using System.Net;
using System.Net.Mail;

namespace AlphaCare.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<bool> SendAsync(string mailTo, string subject, string htmlBody)
        {
            try
            {
                string host = _config["Smtp:Host"] ?? "";                      // old: SmtpServer = 172.17.2.29
                int port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 25;
                string user = _config["Smtp:User"] ?? "";                      // old: Username (empty)
                string password = _config["Smtp:Password"] ?? "";              // old: Password (empty)
                string fromAddress = _config["Smtp:FromAddress"] ?? "";        // old: rsms@prangroup.com
                string fromName = _config["Smtp:FromName"] ?? "YOUR ADMIN";
                bool ssl = bool.TryParse(_config["Smtp:EnableSsl"], out var s) && s;

                if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress))
                {
                    _logger.LogError("Smtp:Host / Smtp:FromAddress is not configured in appsettings.json");
                    return false;
                }

                var to = new MailAddress(mailTo.Trim());           // throws when the address is not valid

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = ssl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Timeout = 20000
                };
                if (!string.IsNullOrEmpty(user))
                    client.Credentials = new NetworkCredential(user, password);

                using var message = new MailMessage
                {
                    From = new MailAddress(fromAddress, fromName),
                    // a subject must be a single line
                    Subject = (subject ?? "").Replace("\r", " ").Replace("\n", " "),
                    Body = htmlBody,
                    IsBodyHtml = true,
                    BodyEncoding = System.Text.Encoding.UTF8,
                    SubjectEncoding = System.Text.Encoding.UTF8
                };
                message.To.Add(to);

                var sendTask = client.SendMailAsync(message);
                var finished = await Task.WhenAny(sendTask, Task.Delay(TimeSpan.FromSeconds(25)));
                if (finished != sendTask)
                {
                    _ = sendTask.ContinueWith(t => { var _ = t.Exception; });     // observe the late error
                    _logger.LogError("Mail timeout. To={To}", mailTo);
                    return false;
                }

                await sendTask;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mail could not be sent. To={To}", mailTo);   // old: err = ex.Message; return false
                return false;
            }
        }
    }
}
