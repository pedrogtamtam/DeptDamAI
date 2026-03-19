using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace DeptDam.Services;

public class TenantService : ITenantService
{
    private string? _currentTenantId;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantService(AuthenticationStateProvider authStateProvider, IHttpContextAccessor httpContextAccessor)
    {
        _authStateProvider = authStateProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetCurrentTenantId()
    {
        if (!string.IsNullOrEmpty(_currentTenantId))
            return _currentTenantId;

        // Try to get from Authentication State (for logged-in users in interactive sessions)
        try 
        {
            // Note: We use Task.Run/Result carefully here for synchronous interface, 
            // but usually in Blazor Server this is safe since the state is already cached in the provider after initialization.
            var authState = _authStateProvider.GetAuthenticationStateAsync().GetAwaiter().GetResult();
            var tenantClaim = authState.User.FindFirstValue("TenantId");
            if (!string.IsNullOrEmpty(tenantClaim))
            {
                _currentTenantId = tenantClaim;
                return _currentTenantId;
            }
        }
        catch { /* Context might not be ready yet */ }

        // Try to get from HttpContext (initial request or static SSR)
        var context = _httpContextAccessor.HttpContext;
        if (context != null)
        {
            if (context.Items.TryGetValue("TenantId", out var idObj) && idObj is string id)
            {
                _currentTenantId = id;
                return _currentTenantId;
            }
        }

        return null;
    }

    public void SetCurrentTenantId(string tenantId)
    {
        _currentTenantId = tenantId;
    }
}
