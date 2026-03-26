namespace DeptDam.Services;

public interface INotificationDispatcher
{
    Task SendMentionAsync(
        string toEmail,
        string toUsername,
        string fromUsername,
        string assetName,
        string assetId,
        string commentPreview,
        string baseUrl);

    Task SendStatusChangeAsync(
        string toEmail,
        string toUsername,
        string assetName,
        string assetId,
        string newStatus,
        string baseUrl);
}

public sealed class NotificationRequest
{
    public string Type { get; set; } = string.Empty;   // "mention" | "status_change"
    public string ToEmail { get; set; } = string.Empty;
    public string ToUsername { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? FromUsername { get; set; }
    public string? CommentPreview { get; set; }
    public string? NewStatus { get; set; }
}
