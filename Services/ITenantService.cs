namespace DeptDam.Services;

public interface ITenantService
{
    string? GetCurrentTenantId();
    void SetCurrentTenantId(string tenantId);
}
