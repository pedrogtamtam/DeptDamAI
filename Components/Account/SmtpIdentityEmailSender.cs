using Microsoft.AspNetCore.Identity;
using DeptDam.Data;
using DeptDam.Services;

namespace DeptDam.Components.Account;

/// <summary>
/// Tenant-aware Identity email sender that uses SMTP settings from the database.
/// Falls back to no-op logging when SMTP is not configured.
/// </summary>
internal sealed class SmtpIdentityEmailSender : IEmailSender<ApplicationUser>
{
    private readonly IEmailService _emailService;

    public SmtpIdentityEmailSender(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        _emailService.SendEmailAsync(email, "Confirm your email",
            $"""
            <h2>Confirm your email</h2>
            <p>Please confirm your account by <a href='{confirmationLink}'>clicking here</a>.</p>
            """);

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        _emailService.SendEmailAsync(email, "Reset your password",
            $"""
            <h2>Reset your password</h2>
            <p>Please reset your password by <a href='{resetLink}'>clicking here</a>.</p>
            """);

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        _emailService.SendEmailAsync(email, "Reset your password",
            $"""
            <h2>Reset your password</h2>
            <p>Please reset your password using the following code: <strong>{resetCode}</strong></p>
            """);
}
