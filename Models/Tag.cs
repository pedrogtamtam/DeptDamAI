using System.ComponentModel.DataAnnotations;

namespace DeptDam.Models;

public class Tag : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;
}

public class AssetTag : ITenantEntity
{
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    public string AssetId { get; set; } = string.Empty;
    public Asset? Asset { get; set; }

    public string TagId { get; set; } = string.Empty;
    public Tag? Tag { get; set; }
}

public class CollectionTag : ITenantEntity
{
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    public string CollectionId { get; set; } = string.Empty;
    public Collection? Collection { get; set; }

    public string TagId { get; set; } = string.Empty;
    public Tag? Tag { get; set; }
}
