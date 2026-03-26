using System.Security.Claims;

namespace DeptDam.Services;

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission);
    Task<List<string>> GetUserPermissionsAsync(ClaimsPrincipal user);
}
