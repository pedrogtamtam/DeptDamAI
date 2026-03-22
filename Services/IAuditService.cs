namespace DeptDam.Services;

public interface IAuditService
{
    Task LogAsync(
        string authorId,
        string authorName,
        string action,
        string? entityType = null,
        string? entityId = null,
        string? entityName = null,
        string? details = null);
}
