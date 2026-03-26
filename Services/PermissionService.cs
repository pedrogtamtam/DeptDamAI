using System.Security.Claims;
using DeptDam.Data;
using DeptDam.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DeptDam.Services;

public class PermissionService : IPermissionService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ITenantService _tenantService;

    public PermissionService(
        UserManager<ApplicationUser> userManager, 
        IDbContextFactory<ApplicationDbContext> dbContextFactory, 
        ITenantService tenantService)
    {
        _userManager = userManager;
        _dbContextFactory = dbContextFactory;
        _tenantService = tenantService;
    }

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission)
    {
        if (user == null || user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var permissions = await GetUserPermissionsAsync(user);
        return permissions.Contains(permission);
    }

    public async Task<List<string>> GetUserPermissionsAsync(ClaimsPrincipal user)
    {
        if (user == null || user.Identity?.IsAuthenticated != true)
        {
            return new List<string>();
        }

        // Check if this is an API Client token (Machine-to-Machine)
        if (user.HasClaim(c => c.Type == "client_name") && user.HasClaim(c => c.Type == "tenant_id"))
        {
            // API Clients get all asset, collection and config permissions for their tenant by default
            return AppPermissions.AllPermissions.Where(p => 
                p != AppPermissions.UsersManage && 
                p != AppPermissions.RolesManage).ToList();
        }

        var userId = _userManager.GetUserId(user);
        if (userId == null)
        {
            return new List<string>();
        }



        // Use a separate context to avoid concurrency issues with the scoped context used by UserManager
        // This is necessary in Blazor when multiple components might check permissions concurrently
        using var context = await _dbContextFactory.CreateDbContextAsync();
        
        var roleIds = await context.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        var roles = await context.Roles
            .OfType<ApplicationRole>()
            .Where(r => roleIds.Contains(r.Id))
            .ToListAsync();
        
        // SuperAdmin has all permissions
        if (roles.Any(r => r.Name == "SuperAdmin"))
        {
            return AppPermissions.AllPermissions.ToList();
        }

        var tenantId = _tenantService.GetCurrentTenantId();

        // Get permissions from all roles assigned to the user in this tenant
        var allPermissions = roles
            .Where(r => r.TenantId == tenantId)
            .SelectMany(r => r.Permissions)
            .Distinct()
            .ToList();

        return allPermissions;
    }
}
