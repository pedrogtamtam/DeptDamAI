using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeptDam.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using MimeKit;

namespace DeptDam.Services;

/// <summary>
/// Sends emails using the current tenant's email settings stored in the database.
/// Supports Basic SMTP, OAuth2 SMTP, and Microsoft Graph API.
/// Falls back to a no-op if not configured.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ITenantService _tenantService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(ApplicationDbContext dbContext, ITenantService tenantService, IHttpClientFactory httpClientFactory, ILogger<SmtpEmailService> logger)
    {
        _dbContext = dbContext;
        _tenantService = tenantService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private async Task<Models.SmtpSettings?> GetSettingsAsync()
    {
        var tenantId = _tenantService.GetCurrentTenantId();
        if (string.IsNullOrEmpty(tenantId)) return null;

        return await _dbContext.SmtpSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.IsEnabled);
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        var settings = await GetSettingsAsync();
        if (settings is null)
        {
            _logger.LogWarning("Email not configured for current tenant – email to {Email} with subject '{Subject}' was not sent.", toEmail, subject);
            return;
        }

        if (settings.AuthMethod == "MicrosoftGraph")
        {
            await SendViaGraphAsync(settings, toEmail, subject, htmlBody);
        }
        else
        {
            await SendViaSmtpAsync(settings, toEmail, subject, htmlBody);
        }
    }

    public async Task<bool> TestConnectionAsync()
    {
        var settings = await GetSettingsAsync();
        if (settings is null) return false;

        if (settings.AuthMethod == "MicrosoftGraph")
        {
            await SendViaGraphAsync(settings, settings.FromEmail, "DeptDAM Email Test",
                "<p>This is a test email from DeptDAM to verify your Microsoft Graph configuration.</p>");
        }
        else
        {
            var message = BuildMimeMessage(settings, settings.FromEmail, "DeptDAM SMTP Test",
                "<p>This is a test email from DeptDAM to verify your SMTP configuration.</p>");
            using var client = await ConnectAndAuthenticateAsync(settings);
            await client.SendAsync(message);
            await client.DisconnectAsync(quit: true);
        }

        return true;
    }

    // ?? Microsoft Graph ????????????????????????????????????????????????

    private async Task SendViaGraphAsync(Models.SmtpSettings settings, string toEmail, string subject, string htmlBody)
    {
        var accessToken = await AcquireGraphTokenAsync(settings);
        var fromEmail = settings.FromEmail.Trim();

        var httpClient = _httpClientFactory.CreateClient();

        // Resolve the mailbox to its stable object ID first.
        // Using the UPN directly in sendMail can return a bare 401 when the
        // UPN format isn't recognised by the Graph routing layer.
        var userId = await ResolveUserIdAsync(httpClient, accessToken, fromEmail);

        var payload = new
        {
            message = new
            {
                subject,
                body = new { contentType = "HTML", content = htmlBody },
                toRecipients = new[] { new { emailAddress = new { address = toEmail } } }
            },
            saveToSentItems = false
        };

        var url = $"https://graph.microsoft.com/v1.0/users/{userId}/sendMail";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var wwwAuth = response.Headers.WwwAuthenticate.ToString();
            _logger.LogError("Graph sendMail failed. Status: {Status}, WWW-Authenticate: {WwwAuth}, Body: {Body}",
                (int)response.StatusCode, wwwAuth, body);
            throw new InvalidOperationException(ParseGraphError(body, (int)response.StatusCode, wwwAuth, tokenInfo: null));
        }

        _logger.LogInformation("Email sent to {Email} via Microsoft Graph.", toEmail);
    }

    private static async Task<string> ResolveUserIdAsync(HttpClient httpClient, string accessToken, string upn)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(upn)}?$select=id,mail,userPrincipalName");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var wwwAuth = response.Headers.WwwAuthenticate.ToString();

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException(
                    $"Mailbox '{upn}' was not found in this tenant. " +
                    "Verify the From Email matches a licensed Exchange Online user in your Microsoft 365 tenant.");

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                throw new InvalidOperationException(
                    $"'{upn}' is not an Exchange Online mailbox accessible via Microsoft Graph. " +
                    "Microsoft Graph only works with organizational M365 accounts (e.g. user@company.com). " +
                    "Personal Microsoft accounts (@hotmail.com, @outlook.com, @live.com) are not supported. " +
                    "To send from a personal account, use SMTP – Basic auth instead " +
                    "(Host: smtp-mail.outlook.com, Port: 587, SSL on).");

            throw new InvalidOperationException(
                $"Could not resolve mailbox '{upn}' ({(int)response.StatusCode}). " +
                $"WWW-Authenticate: [{wwwAuth}]. Body: [{body}]");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("id", out var idEl))
            return idEl.GetString()!;

        throw new InvalidOperationException($"Graph returned a user object for '{upn}' but it had no 'id' field.");
    }

    private static string ParseGraphError(string responseBody, int statusCode, string wwwAuthenticate, string? tokenInfo)
    {
        var code = "";
        var message = responseBody;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var errorEl))
            {
                code = errorEl.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
                message = errorEl.TryGetProperty("message", out var m) ? m.GetString() ?? responseBody : responseBody;
            }
        }
        catch { /* use raw body */ }

        if (statusCode == 401)
        {
            var detail = !string.IsNullOrWhiteSpace(wwwAuthenticate) ? wwwAuthenticate : message;
            return $"Graph 401 Unauthorized. {detail}. " +
                   "Ensure Mail.Send is an APPLICATION permission (not Delegated) with admin consent granted.";
        }

        if (statusCode == 403)
            return $"Graph 403 Forbidden ({code}). " +
                   "Verify Mail.Send application permission has admin consent and the From email is a licensed Exchange Online mailbox. " +
                   $"Detail: {message}";

        return $"Microsoft Graph sendMail failed ({statusCode} {code}): {message}";
    }

    private static async Task<string> AcquireGraphTokenAsync(Models.SmtpSettings settings)
    {
        ValidateOAuthFields(settings);

        var app = ConfidentialClientApplicationBuilder
            .Create(settings.OAuthClientId)
            .WithClientSecret(settings.OAuthClientSecret)
            .WithAuthority(AzureCloudInstance.AzurePublic, settings.OAuthTenantId)
            .Build();

        try
        {
            var result = await app.AcquireTokenForClient(["https://graph.microsoft.com/.default"]).ExecuteAsync();
            return result.AccessToken;
        }
        catch (MsalServiceException ex)
        {
            throw WrapMsalException(ex);
        }
    }

    // ?? SMTP (Basic / OAuth2) ??????????????????????????????????????????

    private async Task SendViaSmtpAsync(Models.SmtpSettings settings, string toEmail, string subject, string htmlBody)
    {
        var message = BuildMimeMessage(settings, toEmail, subject, htmlBody);
        try
        {
            using var client = await ConnectAndAuthenticateAsync(settings);
            await client.SendAsync(message);
            await client.DisconnectAsync(quit: true);
        }
        catch (MailKit.Net.Smtp.SmtpCommandException ex) when (ex.Message.Contains("5.7.139") || ex.Message.Contains("basic authentication is disabled"))
        {
            throw new InvalidOperationException(
                "The SMTP server has basic authentication disabled for this account. " +
                (settings.Host.Contains("outlook") || settings.Host.Contains("hotmail")
                    ? "For Outlook.com / Hotmail: go to outlook.live.com ? Settings ? Mail ? Sync email ? enable POP/IMAP & SMTP. " +
                      "If 2-step verification is on, generate an App Password at account.microsoft.com/security and use it instead of your password."
                    : "Check your mail provider's settings to enable SMTP authentication, or use an app password."), ex);
        }
        catch (MailKit.Net.Smtp.SmtpCommandException ex) when (ex.Message.Contains("5.7.57") || ex.Message.Contains("Client not authenticated"))
        {
            throw new InvalidOperationException(
                "SMTP authentication failed. Verify the username and password are correct. " +
                "If 2-step verification is enabled, use an App Password instead of your account password.", ex);
        }
        _logger.LogInformation("Email sent to {Email} via {Host}:{Port}.", toEmail, settings.Host, settings.Port);
    }

    private static MimeMessage BuildMimeMessage(Models.SmtpSettings settings, string toEmail, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName ?? settings.FromEmail, settings.FromEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };
        return message;
    }

    private static async Task<SmtpClient> ConnectAndAuthenticateAsync(Models.SmtpSettings settings)
    {
        var client = new SmtpClient();

        var secureOption = settings.EnableSsl
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.Auto;

        await client.ConnectAsync(settings.Host, settings.Port, secureOption);

        if (settings.AuthMethod == "OAuth2")
        {
            var accessToken = await AcquireSmtpOAuth2TokenAsync(settings);
            var oauth2 = new SaslMechanismOAuth2(settings.Username ?? settings.FromEmail, accessToken);
            await client.AuthenticateAsync(oauth2);
        }
        else if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            await client.AuthenticateAsync(settings.Username, settings.Password);
        }

        return client;
    }

    private static async Task<string> AcquireSmtpOAuth2TokenAsync(Models.SmtpSettings settings)
    {
        ValidateOAuthFields(settings);

        var app = ConfidentialClientApplicationBuilder
            .Create(settings.OAuthClientId)
            .WithClientSecret(settings.OAuthClientSecret)
            .WithAuthority(AzureCloudInstance.AzurePublic, settings.OAuthTenantId)
            .Build();

        try
        {
            var result = await app.AcquireTokenForClient(["https://outlook.office365.com/.default"]).ExecuteAsync();
            return result.AccessToken;
        }
        catch (MsalServiceException ex)
        {
            throw WrapMsalException(ex);
        }
    }

    // ?? Shared helpers ?????????????????????????????????????????????????

    private static void ValidateOAuthFields(Models.SmtpSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OAuthTenantId) ||
            string.IsNullOrWhiteSpace(settings.OAuthClientId) ||
            string.IsNullOrWhiteSpace(settings.OAuthClientSecret))
        {
            throw new InvalidOperationException("Tenant ID, Client ID, or Client Secret is missing.");
        }
    }

    private static InvalidOperationException WrapMsalException(MsalServiceException ex)
    {
        if (ex.Message.Contains("AADSTS9002346") || ex.Message.Contains("/consumers"))
            return new InvalidOperationException(
                "The Entra ID app is registered for personal Microsoft accounts only. " +
                "Change Supported account types to 'Accounts in this organizational directory only (Single tenant)'. " +
                "If greyed out, re-register the app with the correct account type.", ex);

        if (ex.Message.Contains("AADSTS7000215"))
            return new InvalidOperationException(
                "Invalid client secret. Create a new secret under Certificates & secrets.", ex);

        if (ex.Message.Contains("AADSTS7000229"))
            return new InvalidOperationException(
                "The app is missing a service principal. Go to API permissions and click " +
                "'Grant admin consent for [your organization]'.", ex);

        if (ex.Message.Contains("AADSTS700016"))
            return new InvalidOperationException(
                "Application not found. Verify the Client ID and Tenant ID are correct.", ex);

        return new InvalidOperationException(ex.Message, ex);
    }
}
