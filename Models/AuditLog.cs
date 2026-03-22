using System.ComponentModel.DataAnnotations;

namespace DeptDam.Models;

public class AuditLog : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string AuthorId { get; set; } = string.Empty;

    [MaxLength(256)]
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>Short machine-readable action key, e.g. "AssetUploaded".</summary>
    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    /// <summary>"Asset" or "Collection".</summary>
    [MaxLength(50)]
    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    [MaxLength(500)]
    public string? EntityName { get; set; }

    /// <summary>Human-readable extra context, e.g. "Draft ? Approved".</summary>
    [MaxLength(1000)]
    public string? Details { get; set; }
}
