using System.ComponentModel.DataAnnotations;

namespace DeptDam.Models;

/// <summary>
/// Stores per-tenant SMTP configuration for sending emails.
/// </summary>
public class SmtpSettings : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    /// <summary>SMTP server host (e.g. smtp.gmail.com)</summary>
    [Required]
    public string Host { get; set; } = string.Empty;

    /// <summary>SMTP server port (e.g. 587)</summary>
    public int Port { get; set; } = 587;

    /// <summary>Whether to use SSL/TLS</summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>Authentication method: "Basic" or "OAuth2"</summary>
    public string AuthMethod { get; set; } = "Basic";

    /// <summary>SMTP username for authentication (Basic auth or OAuth2 mailbox address)</summary>
    public string? Username { get; set; }

    /// <summary>SMTP password for Basic authentication</summary>
    public string? Password { get; set; }

    /// <summary>Azure AD / Entra ID Tenant ID for OAuth2</summary>
    public string? OAuthTenantId { get; set; }

    /// <summary>Azure AD / Entra ID Application (Client) ID for OAuth2</summary>
    public string? OAuthClientId { get; set; }

    /// <summary>Azure AD / Entra ID Client Secret for OAuth2</summary>
    public string? OAuthClientSecret { get; set; }

    /// <summary>The "From" email address</summary>
    [Required]
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>The "From" display name</summary>
    public string? FromName { get; set; }

    /// <summary>Whether email sending is enabled for this tenant</summary>
    public bool IsEnabled { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
