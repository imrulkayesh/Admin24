// Interface/IEmailService.cs
namespace AlphaCare.Services
{
    public interface IEmailService
    {
        /// <summary>Old SendEmail.SendSingleMail(mailTo, subject, body). Returns false when the mail could not be sent.</summary>
        Task<bool> SendAsync(string mailTo, string subject, string htmlBody);
    }
}
