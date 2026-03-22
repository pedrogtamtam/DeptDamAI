namespace DeptDam.Services;

public interface IEmailService
{
    Task SendEmailAsync(string toEmail, string subject, string htmlBody);
    Task<bool> TestConnectionAsync();
}
