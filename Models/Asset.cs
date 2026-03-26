using System.ComponentModel.DataAnnotations;

namespace DeptDam.Models;

public class Asset : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    [Required]
    public string OriginalFileName { get; set; } = string.Empty;
    
    [Required]
    public string StorageKey { get; set; } = string.Empty;
    
    [Required]
    public string ContentType { get; set; } = string.Empty;
    
    public long SizeBytes { get; set; }
    
    [MaxLength(64)]
    public string? FileHash { get; set; }
    
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public string UploadedById { get; set; } = string.Empty;
    public Data.ApplicationUser? UploadedBy { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }

    public AssetWorkflowState WorkflowState { get; set; } = AssetWorkflowState.Draft;
    
    public bool IsPublic { get; set; } = false;

    public string? ExifData { get; set; }
    public string? ExtractedText { get; set; }
    public string? FacesDetected { get; set; }
    
    public DateTime? ExpiresAt { get; set; }

    // Navigation properties for metadata
    public ICollection<AssetTag> Tags { get; set; } = new List<AssetTag>();
    public ICollection<AssetVersion> Versions { get; set; } = new List<AssetVersion>();
    public ICollection<AssetMetadataValue> MetadataValues { get; set; } = new List<AssetMetadataValue>();
    public ICollection<ShareLink> ShareLinks { get; set; } = new List<ShareLink>();
}

public enum AssetWorkflowState
{
    Draft,
    InReview,
    Approved,
    Rejected
}
