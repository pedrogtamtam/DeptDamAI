using System.ComponentModel.DataAnnotations;

namespace DeptDam.Models;

public class ShareLink : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    [Required]
    public string AssetId { get; set; } = string.Empty;
    public Asset? Asset { get; set; }

    /// <summary>Opaque random token embedded in the shareable URL.</summary>
    [Required]
    [MaxLength(128)]
    public string Token { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>null = never expires</summary>
    public DateTime? ExpiresAt { get; set; }

    public string CreatedById { get; set; } = string.Empty;
    public Data.ApplicationUser? CreatedBy { get; set; }

    /// <summary>Human-readable label, e.g. "Marketing Team Q1"</summary>
    [MaxLength(256)]
    public string? Label { get; set; }

    public bool IsRevoked { get; set; }

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
    public bool IsActive => !IsRevoked && !IsExpired;
}
