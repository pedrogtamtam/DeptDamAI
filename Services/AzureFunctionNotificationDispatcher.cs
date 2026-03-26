using System.Net.Http.Json;
using System.Text.Json;

namespace DeptDam.Services;

/// <summary>
/// Sends notifications by posting to the configured Azure Function endpoint.
/// Falls back to <see cref="IEmailService"/> when the endpoint is not configured.
/// </summary>
public sealed class AzureFunctionNotificationDispatcher : INotificationDispatcher
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<AzureFunctionNotificationDispatcher> _logger;

    public AzureFunctionNotificationDispatcher(
        IHttpClientFactory httpFactory,
        IEmailService email,
        IConfiguration config,
        ILogger<AzureFunctionNotificationDispatcher> logger)
    {
        _httpFactory = httpFactory;
        _email = email;
        _config = config;
        _logger = logger;
    }

    public Task SendMentionAsync(
        string toEmail, string toUsername, string fromUsername,
        string assetName, string assetId, string commentPreview, string baseUrl) =>
        DispatchAsync(new NotificationRequest
        {
            Type = "mention",
            ToEmail = toEmail,
            ToUsername = toUsername,
            FromUsername = fromUsername,
            AssetName = assetName,
            AssetId = assetId,
            CommentPreview = commentPreview,
            BaseUrl = baseUrl
        });

    public Task SendStatusChangeAsync(
        string toEmail, string toUsername,
        string assetName, string assetId, string newStatus, string baseUrl) =>
        DispatchAsync(new NotificationRequest
        {
            Type = "status_change",
            ToEmail = toEmail,
            ToUsername = toUsername,
            AssetName = assetName,
            AssetId = assetId,
            NewStatus = newStatus,
            BaseUrl = baseUrl
        });

    private async Task DispatchAsync(NotificationRequest request)
    {
        var functionBaseUrl = _config["AzureFunctions:BaseUrl"];

        if (!string.IsNullOrWhiteSpace(functionBaseUrl))
        {
            try
            {
                var client = _httpFactory.CreateClient();
                var url = $"{functionBaseUrl.TrimEnd('/')}/api/notify";

                var functionKey = _config["AzureFunctions:FunctionKey"];
                if (!string.IsNullOrWhiteSpace(functionKey))
                    client.DefaultRequestHeaders.Add("x-functions-key", functionKey);

                var response = await client.PostAsJsonAsync(url, request);
                if (response.IsSuccessStatusCode) return;

                _logger.LogWarning("Azure Function notify returned {Status}. Falling back to direct email.", response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to call Azure Function. Falling back to direct email.");
            }
        }

        // Fallback: send email directly
        await SendFallbackEmailAsync(request);
    }

    private async Task SendFallbackEmailAsync(NotificationRequest r)
    {
        try
        {
            if (r.Type == "mention")
            {
                await _email.SendEmailAsync(
                    r.ToEmail,
                    $"You were mentioned in a comment on \"{r.AssetName}\"",
                    $"<p>Hi {r.ToUsername},</p>" +
                    $"<p><strong>{System.Net.WebUtility.HtmlEncode(r.FromUsername)}</strong> mentioned you in a comment on " +
                    $"<strong>{System.Net.WebUtility.HtmlEncode(r.AssetName)}</strong>:</p>" +
                    $"<blockquote style='border-left:3px solid #ccc;padding-left:1em;color:#555;'>" +
                    $"{System.Net.WebUtility.HtmlEncode(r.CommentPreview)}</blockquote>" +
                    $"<p><a href='{r.BaseUrl.TrimEnd('/')}/Assets/{r.AssetId}'>View asset ?</a></p>");
            }
            else if (r.Type == "status_change")
            {
                await _email.SendEmailAsync(
                    r.ToEmail,
                    $"Asset \"{r.AssetName}\" is now {r.NewStatus}",
                    $"<p>Hi {r.ToUsername},</p>" +
                    $"<p>The asset <strong>{System.Net.WebUtility.HtmlEncode(r.AssetName)}</strong> has been moved to " +
                    $"<strong>{r.NewStatus}</strong>.</p>" +
                    $"<p><a href='{r.BaseUrl.TrimEnd('/')}/Assets/{r.AssetId}'>Review asset ?</a></p>");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallback email notification failed for {Type}", r.Type);
        }
    }
}
