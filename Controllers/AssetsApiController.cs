using DeptDam.Data;
using DeptDam.Models;
using DeptDam.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeptDam.Controllers;

[ApiController]
[Route("api/assets")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class AssetsApiController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPermissionService _permissionService;

    public AssetsApiController(ApplicationDbContext dbContext, IPermissionService permissionService)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
    }

    [HttpGet]
    public async Task<IActionResult> ListAssets([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (!await _permissionService.HasPermissionAsync(User, AppPermissions.AssetsView))
        {
            return Forbid();
        }

        // The tenant context is extracted from the JWT token via the "tenant_id" claim
        var tenantIdClaim = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdClaim))
        {
            return Forbid();
        }

        var assets = await _dbContext.Assets
            .IgnoreQueryFilters() // We manually filter by the token's tenant claim for external API
            .Where(a => a.TenantId == tenantIdClaim)
            .OrderByDescending(a => a.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.OriginalFileName,
                a.ContentType,
                a.SizeBytes,
                a.UploadedAt,
                a.WorkflowState,
                a.Width,
                a.Height,
                a.ExpiresAt,
                a.IsPublic,
                Tags = a.Tags.Select(at => at.Tag!.Name).ToList(),
                Transformations = _dbContext.Transformations
                    .IgnoreQueryFilters()
                    .Where(r => r.TenantId == tenantIdClaim)
                    .OrderBy(r => r.SortOrder)
                    .Select(r => new {
                        r.Name,
                        Url = $"/api/media/{tenantIdClaim}/{a.StorageKey}?w={r.Width}&h={r.Height}&format={r.Format}&q={r.Quality}" +
                              $"&mode={r.ResizeMode}{(r.Grayscale ? "&gray=true" : "")}{(r.Sepia ? "&sepia=true" : "")}" +
                              (r.Brightness != 0 ? $"&bright={r.Brightness}" : "") +
                              (r.Contrast != 0 ? $"&cont={r.Contrast}" : "")
                    })
                    .ToList(),
                MediaUrl = $"/api/media/{a.TenantId}/{a.StorageKey}"
            })
            .ToListAsync();

        return Ok(new
        {
            Page = page,
            PageSize = pageSize,
            Data = assets
        });
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAsset(string id)
    {
        var tenantIdClaim = User.FindFirst("tenant_id")?.Value;

        var tempAsset = await _dbContext.Assets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == id);

        if (tempAsset == null) return NotFound();

        // Privacy: If not public, require authentication and permission
        if (!tempAsset.IsPublic)
        {
            if (User.Identity?.IsAuthenticated != true) return Unauthorized();
            if (!await _permissionService.HasPermissionAsync(User, AppPermissions.AssetsView)) return Forbid();
            if (string.IsNullOrEmpty(tenantIdClaim) || tempAsset.TenantId != tenantIdClaim) return Forbid();
        }

        var asset = await _dbContext.Assets
            .IgnoreQueryFilters()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                a.Id,
                a.OriginalFileName,
                a.ContentType,
                a.SizeBytes,
                a.UploadedAt,
                a.WorkflowState,
                a.Width,
                a.Height,
                a.ExpiresAt,
                a.IsPublic,
                a.FileHash,
                a.UploadedById,
                a.ExtractedText,
                a.ExifData,
                a.FacesDetected,
                Tags = a.Tags.Select(at => at.Tag!.Name).ToList(),
                Metadata = a.MetadataValues.Select(mv => new
                {
                    Field = mv.CustomField!.Name,
                    mv.Value
                }).ToList(),
                Versions = a.Versions.Select(v => new
                {
                    v.VersionNumber,
                    v.CreatedAt,
                    User = v.CreatedBy != null ? v.CreatedBy.Email : "System",
                    v.VersionNote,
                    v.SizeBytes
                }).OrderByDescending(v => v.VersionNumber).ToList(),
                Collections = _dbContext.CollectionAssets
                    .Where(ca => ca.AssetId == a.Id && ca.TenantId == a.TenantId)
                    .Select(ca => ca.Collection!.Name)
                    .ToList(),
                Transformations = _dbContext.Transformations
                    .IgnoreQueryFilters()
                    .Where(r => r.TenantId == a.TenantId)
                    .OrderBy(r => r.SortOrder)
                    .Select(r => new {
                        r.Name,
                        Url = $"/api/media/{a.TenantId}/{a.StorageKey}?w={r.Width}&h={r.Height}&format={r.Format}&q={r.Quality}" +
                              $"&mode={r.ResizeMode}{(r.Grayscale ? "&gray=true" : "")}{(r.Sepia ? "&sepia=true" : "")}" +
                              (r.Brightness != 0 ? $"&bright={r.Brightness}" : "") +
                              (r.Contrast != 0 ? $"&cont={r.Contrast}" : "")
                    })
                    .ToList(),
                MediaUrl = $"/api/media/{a.TenantId}/{a.StorageKey}"
            })
            .FirstOrDefaultAsync();

        return Ok(asset);
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadAsset(
        [FromServices] DeptDam.Services.Storage.IStorageProvider storageProvider, 
        IFormFile file)
    {
        if (!await _permissionService.HasPermissionAsync(User, AppPermissions.AssetsUpload))
        {
            return Forbid();
        }

        var tenantIdClaim = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdClaim))
        {
            return Forbid();
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

        // Use sub (clientId) or client_name for UploadedById
        var uploadedById = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                           ?? User.FindFirst("sub")?.Value 
                           ?? User.FindFirst("client_name")?.Value 
                           ?? "ApiClient";

        // Generate a unique storage key
        var extension = Path.GetExtension(file.FileName);
        var storageKey = $"{Guid.NewGuid()}{extension}";

        try
        {
            using var stream = file.OpenReadStream();
            var savedKey = await storageProvider.UploadFileAsync(stream, file.FileName, file.ContentType);

            var asset = new Asset
            {
                OriginalFileName = file.FileName,
                ContentType = file.ContentType,
                SizeBytes = file.Length,
                StorageKey = savedKey,
                TenantId = tenantIdClaim,
                UploadedById = uploadedById,
                WorkflowState = AssetWorkflowState.Draft
            };

            _dbContext.Assets.Add(asset);
            await _dbContext.SaveChangesAsync();

            return Created($"/api/assets/{asset.Id}", new
            {
                asset.Id,
                asset.OriginalFileName,
                asset.ContentType,
                asset.SizeBytes,
                asset.UploadedAt,
                asset.WorkflowState,
                asset.UploadedById,
                Tags = new List<string>(),
                MediaUrl = $"/api/media/{asset.TenantId}/{asset.StorageKey}"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
}
