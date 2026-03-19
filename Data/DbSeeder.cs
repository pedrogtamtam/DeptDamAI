using DeptDam.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace DeptDam.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await context.Database.MigrateAsync();

        if (!await context.Tenants.AnyAsync(t => t.Subdomain == "localhost"))
        {
            context.Tenants.Add(new Tenant
            {
                Name = "Local Dev Tenant",
                Subdomain = "localhost",
                IsActive = true
            });
            await context.SaveChangesAsync();
        }
        
        if (!await context.Tenants.AnyAsync(t => t.Subdomain == "client1"))
        {
            context.Tenants.Add(new Tenant
            {
                Name = "Client 1 Tenant",
                Subdomain = "client1",
                IsActive = true
            });
            await context.SaveChangesAsync();
        }

        // Seed Roles for all tenants
        var tenants = await context.Tenants.ToListAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<ApplicationRole>>();
        
        foreach (var tenant in tenants)
        {
            string[] roles = { "SuperAdmin", "DAM_Admin", "DAM_Contributor", "DAM_Viewer" };
            foreach (var roleName in roles)
            {
                var roleExists = await context.Roles.IgnoreQueryFilters().AnyAsync(r => r.Name == roleName && ((ApplicationRole)r).TenantId == tenant.Id);
                if (!roleExists)
                {
                    context.Roles.Add(new ApplicationRole 
                    { 
                        Name = roleName, 
                        NormalizedName = roleName.ToUpperInvariant(),
                        TenantId = tenant.Id 
                    });
                }
            }
            await context.SaveChangesAsync();

            // Special case: Ensure admin@deptdam.com exists and has DAM_Admin in localhost
            if (tenant.Subdomain == "localhost")
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var adminEmail = "admin@deptdam.com";
                var adminUser = await userManager.FindByEmailAsync(adminEmail);
                if (adminUser == null)
                {
                    adminUser = new ApplicationUser
                    {
                        UserName = adminEmail,
                        Email = adminEmail,
                        EmailConfirmed = true,
                        TenantId = tenant.Id
                    };
                    await userManager.CreateAsync(adminUser, "P@ssword123!");
                }
                else if (adminUser.TenantId != tenant.Id)
                {
                     // Fix tenant if wrong
                     adminUser.TenantId = tenant.Id;
                     await userManager.UpdateAsync(adminUser);
                }

                var userRoles = await context.UserRoles.Where(ur => ur.UserId == adminUser.Id).ToListAsync();

                var damAdminRole = await context.Roles.IgnoreQueryFilters().FirstAsync(r => r.NormalizedName == "DAM_ADMIN" && ((ApplicationRole)r).TenantId == tenant.Id);
                if (!userRoles.Any(ur => ur.RoleId == damAdminRole.Id))
                {
                    context.UserRoles.Add(new IdentityUserRole<string> { UserId = adminUser.Id, RoleId = damAdminRole.Id });
                }

                var superAdminRole = await context.Roles.IgnoreQueryFilters().FirstAsync(r => r.NormalizedName == "SUPERADMIN" && ((ApplicationRole)r).TenantId == tenant.Id);
                if (!userRoles.Any(ur => ur.RoleId == superAdminRole.Id))
                {
                    context.UserRoles.Add(new IdentityUserRole<string> { UserId = adminUser.Id, RoleId = superAdminRole.Id });
                }
                
                await context.SaveChangesAsync();
            }
        }
    }
}
