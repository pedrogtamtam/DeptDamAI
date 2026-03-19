using Microsoft.AspNetCore.Identity;

namespace DeptDam.Data;

using DeptDam.Models;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser, ITenantEntity
{
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }
}

