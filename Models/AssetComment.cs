using System.ComponentModel.DataAnnotations;

namespace DeptDam.Models;

public class AssetComment : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    [Required]
    public string AssetId { get; set; } = string.Empty;
    public Asset? Asset { get; set; }

    [Required]
    [MaxLength(4000)]
    public string Body { get; set; } = string.Empty;

    public string AuthorId { get; set; } = string.Empty;
    public Data.ApplicationUser? Author { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }

    /// <summary>Optional JSON {x, y} percentage coordinates for image annotations.</summary>
    [MaxLength(64)]
    public string? PinPosition { get; set; }

    /// <summary>True if this is a system-generated event (e.g. state change), not a user comment.</summary>
    public bool IsSystemEvent { get; set; }

    /// <summary>For system events: 'state_changed', 'version_created', etc.</summary>
    [MaxLength(64)]
    public string? EventType { get; set; }
}
