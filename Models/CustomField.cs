using System.ComponentModel.DataAnnotations;

namespace DeptDam.Models;

public class CustomField : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public FieldType FieldType { get; set; } = FieldType.Text;

    // For Dropdown type, comma separated list of valid options
    public string? Options { get; set; }
}

public enum FieldType
{
    Text,
    Dropdown,
    Boolean,
    Date
}
