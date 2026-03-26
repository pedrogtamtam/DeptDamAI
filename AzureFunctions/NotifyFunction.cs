using System.Net;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace DeptDam.Functions;

public sealed class NotifyFunction
{
    private readonly IConfiguration _config;
    private readonly ILogger<NotifyFunction> _logger;

    public NotifyFunction(IConfiguration config, ILogger<NotifyFunction> logger)
    {
        _config = config;
        _logger = logger;
    }

    [Function("notify")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "notify")] HttpRequestData req,
        CancellationToken ct)
    {
        NotificationRequest? notification;
        try
        {
            var body = await req.ReadAsStringAsync() ?? "{}";
            notification = JsonSerializer.Deserialize<NotificationRequest>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize notification request");
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        if (notification == null || string.IsNullOrWhiteSpace(notification.ToEmail))
        {
            _logger.LogWarning("Notification request missing required fields");
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        try
        {
            await SendEmailAsync(notification, ct);
            return req.CreateResponse(HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification email to {Email}", notification.ToEmail);
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }
    }

    private async Task SendEmailAsync(NotificationRequest r, CancellationToken ct)
    {
        var (subject, htmlBody) = r.Type switch
        {
            "mention" => BuildMentionEmail(r),
            "status_change" => BuildStatusChangeEmail(r),
            _ => throw new InvalidOperationException($"Unknown notification type: {r.Type}")
        };

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_config["Smtp:From"] ?? "noreply@deptdam.local"));
        message.To.Add(MailboxAddress.Parse(r.ToEmail));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var smtp = new SmtpClient();
        var host = _config["Smtp:Host"] ?? "localhost";
        var port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;
        var enableSsl = !string.Equals(_config["Smtp:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase);

        await smtp.ConnectAsync(host, port, enableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);

        var user = _config["Smtp:User"];
        var pass = _config["Smtp:Password"];
        if (!string.IsNullOrWhiteSpace(user))
            await smtp.AuthenticateAsync(user, pass, ct);

        await smtp.SendAsync(message, ct);
        await smtp.DisconnectAsync(true, ct);

        _logger.LogInformation("Sent {Type} notification to {Email}", r.Type, r.ToEmail);
    }

    private static (string subject, string html) BuildMentionEmail(NotificationRequest r)
    {
        var assetUrl = $"{r.BaseUrl?.TrimEnd('/')}/Assets/{r.AssetId}";
        var subject = $"You were mentioned in a comment on \"{r.AssetName}\"";
        var html =
            $"<p>Hi {WebUtility.HtmlEncode(r.ToUsername)},</p>" +
            $"<p><strong>{WebUtility.HtmlEncode(r.FromUsername)}</strong> mentioned you in a comment on " +
            $"<strong>{WebUtility.HtmlEncode(r.AssetName)}</strong>:</p>" +
            $"<blockquote style='border-left:3px solid #4f46e5;padding-left:1em;color:#555;margin:1em 0;'>" +
            $"{WebUtility.HtmlEncode(r.CommentPreview)}</blockquote>" +
            $"<p><a href='{assetUrl}' style='background:#4f46e5;color:#fff;padding:8px 16px;border-radius:4px;text-decoration:none;'>View asset ?</a></p>";
        return (subject, html);
    }

    private static (string subject, string html) BuildStatusChangeEmail(NotificationRequest r)
    {
        var assetUrl = $"{r.BaseUrl?.TrimEnd('/')}/Assets/{r.AssetId}";
        var subject = $"Asset \"{r.AssetName}\" moved to {r.NewStatus}";
        var html =
            $"<p>Hi {WebUtility.HtmlEncode(r.ToUsername)},</p>" +
            $"<p>The asset <strong>{WebUtility.HtmlEncode(r.AssetName)}</strong> has been moved to " +
            $"<strong>{r.NewStatus}</strong> and is awaiting your review.</p>" +
            $"<p><a href='{assetUrl}' style='background:#4f46e5;color:#fff;padding:8px 16px;border-radius:4px;text-decoration:none;'>Review asset ?</a></p>";
        return (subject, html);
    }
}

public sealed class NotificationRequest
{
    public string Type { get; set; } = string.Empty;
    public string ToEmail { get; set; } = string.Empty;
    public string ToUsername { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? FromUsername { get; set; }
    public string? CommentPreview { get; set; }
    public string? NewStatus { get; set; }
}
