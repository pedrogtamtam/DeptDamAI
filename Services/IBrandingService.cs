using DeptDam.Models;

namespace DeptDam.Services;

public interface IBrandingService
{
    /// <summary>Returns the branding settings for the current tenant, or defaults if none are saved yet.</summary>
    Task<BrandingSettings> GetAsync();

    /// <summary>Persists branding settings and fires <see cref="OnChanged"/>.</summary>
    Task SaveAsync(BrandingSettings settings);

    /// <summary>Raised after a successful save. Carries the updated settings so consumers need not re-query the DB.</summary>
    event Action<BrandingSettings>? OnChanged;
}
