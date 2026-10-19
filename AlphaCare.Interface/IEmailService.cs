using AlphaCare.Interface;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

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

        public Task<bool> SendAsync(string mailTo, string subject, string htmlBody)
        {
            // Implementation here
            return Task.FromResult(true);
        }
    }
}