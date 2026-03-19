using System.ComponentModel.DataAnnotations;

namespace DeptDam.Models;

public class AssetVersion : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    [Required]
    public string AssetId { get; set; } = string.Empty;
    public Asset? Asset { get; set; }

    [Required]
    public string StorageKey { get; set; } = string.Empty;
    
    public long SizeBytes { get; set; }
    
    [MaxLength(64)]
    public string? FileHash { get; set; }
    
    public int VersionNumber { get; set; }
    
    public string CreatedById { get; set; } = string.Empty;
    public Data.ApplicationUser? CreatedBy { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public string? VersionNote { get; set; }
}
