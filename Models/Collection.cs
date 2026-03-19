using System.ComponentModel.DataAnnotations;

namespace DeptDam.Models;

public class Collection : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;
    
    public string Description { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public string CreatedById { get; set; } = string.Empty;
    public Data.ApplicationUser? CreatedBy { get; set; }

    // If true, anyone with the link can view the collection
    public bool IsPublic { get; set; } = false;
    
    // Optional expiry for the public link
    public DateTime? PublicLinkExpiresAt { get; set; }

    public ICollection<CollectionAsset> Assets { get; set; } = new List<CollectionAsset>();
    public ICollection<CollectionTag> DynamicTags { get; set; } = new List<CollectionTag>();
}

public class CollectionAsset : ITenantEntity
{
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    public string CollectionId { get; set; } = string.Empty;
    public Collection? Collection { get; set; }

    public string AssetId { get; set; } = string.Empty;
    public Asset? Asset { get; set; }
    
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
