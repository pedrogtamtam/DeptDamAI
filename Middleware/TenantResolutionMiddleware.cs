using DeptDam.Data;
using DeptDam.Services;
using Microsoft.EntityFrameworkCore;

namespace DeptDam.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantService tenantService, ApplicationDbContext dbContext)
    {
        var host = context.Request.Host.Host;

        // Try to get subsystem/subdomain (e.g. client1 from client1.deptdam.com or client1.local)
        var subdomain = host.Split('.')[0].ToLowerInvariant();

        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Subdomain == subdomain && t.IsActive);

        if (tenant != null)
        {
            tenantService.SetCurrentTenantId(tenant.Id);
            context.Items["TenantId"] = tenant.Id;
        }

        await _next(context);
    }
}
