using DeptDam.Data;
using DeptDam.Models;
using Microsoft.EntityFrameworkCore;

namespace DeptDam.Services;

public class BrandingService : IBrandingService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly ITenantService _tenantService;

    public event Action<BrandingSettings>? OnChanged;

    public BrandingService(IDbContextFactory<ApplicationDbContext> factory, ITenantService tenantService)
    {
        _factory = factory;
        _tenantService = tenantService;
    }

    public async Task<BrandingSettings> GetAsync()
    {
        var tid = _tenantService.GetCurrentTenantId();
        if (string.IsNullOrEmpty(tid))
            return new BrandingSettings();

        await using var db = await _factory.CreateDbContextAsync();
        return await db.BrandingSettings
                   .IgnoreQueryFilters()
                   .FirstOrDefaultAsync(b => b.TenantId == tid)
               ?? new BrandingSettings { TenantId = tid };
    }

    public async Task SaveAsync(BrandingSettings settings)
    {
        settings.UpdatedAt = DateTime.UtcNow;

        await using var db = await _factory.CreateDbContextAsync();
        var existing = await db.BrandingSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.TenantId == settings.TenantId);

        if (existing == null)
        {
            db.BrandingSettings.Add(settings);
        }
        else
        {
            existing.HeroBadgeText          = settings.HeroBadgeText;
            existing.HeroTitle              = settings.HeroTitle;
            existing.HeroSubtitle           = settings.HeroSubtitle;
            existing.PrimaryButtonLabel     = settings.PrimaryButtonLabel;
            existing.SecondaryButtonLabel   = settings.SecondaryButtonLabel;
            existing.NavHomeLabel           = settings.NavHomeLabel;
            existing.NavAssetsLabel         = settings.NavAssetsLabel;
            existing.NavCollectionsLabel    = settings.NavCollectionsLabel;
            existing.NavWorkflowLabel       = settings.NavWorkflowLabel;
            existing.PageAssetsHeading      = settings.PageAssetsHeading;
            existing.PageAssetsSubheading   = settings.PageAssetsSubheading;
            existing.PageCollectionsHeading = settings.PageCollectionsHeading;
            existing.PageCollectionsSubheading = settings.PageCollectionsSubheading;
            existing.PageWorkflowHeading    = settings.PageWorkflowHeading;
            existing.PageWorkflowSubheading = settings.PageWorkflowSubheading;
            existing.UpdatedAt              = settings.UpdatedAt;
        }

        await db.SaveChangesAsync();
        OnChanged?.Invoke(existing ?? settings);
    }
}
