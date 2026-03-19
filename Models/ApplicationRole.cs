using Microsoft.AspNetCore.Identity;

namespace DeptDam.Models;

public class ApplicationRole : IdentityRole, ITenantEntity
{
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }
}
