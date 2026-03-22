using DeptDam.Data;
using DeptDam.Models;
using Microsoft.EntityFrameworkCore;

namespace DeptDam.Services;

public class AuditService : IAuditService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly ITenantService _tenantService;

    public AuditService(IDbContextFactory<ApplicationDbContext> factory, ITenantService tenantService)
    {
        _factory = factory;
        _tenantService = tenantService;
    }

    public async Task LogAsync(
        string authorId,
        string authorName,
        string action,
        string? entityType = null,
        string? entityId = null,
        string? entityName = null,
        string? details = null)
    {
        var tid = _tenantService.GetCurrentTenantId();
        if (string.IsNullOrEmpty(tid)) return;

        await using var db = await _factory.CreateDbContextAsync();
        db.AuditLogs.Add(new AuditLog
        {
            TenantId    = tid,
            Timestamp   = DateTime.UtcNow,
            AuthorId    = authorId,
            AuthorName  = authorName,
            Action      = action,
            EntityType  = entityType,
            EntityId    = entityId,
            EntityName  = entityName,
            Details     = details
        });
        await db.SaveChangesAsync();
    }
}
