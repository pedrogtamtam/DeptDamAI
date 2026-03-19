using System.ComponentModel.DataAnnotations;

namespace DeptDam.Models;

public class AssetMetadataValue : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    [Required]
    public string AssetId { get; set; } = string.Empty;
    public Asset? Asset { get; set; }

    [Required]
    public string CustomFieldId { get; set; } = string.Empty;
    public CustomField? CustomField { get; set; }

    public string? Value { get; set; }
}
