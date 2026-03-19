using DeptDam.Data;
using DeptDam.Models;
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

    public AssetsApiController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> ListAssets([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
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
    public async Task<IActionResult> GetAsset(string id)
    {
        var tenantIdClaim = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdClaim))
        {
            return Forbid();
        }

        var asset = await _dbContext.Assets
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantIdClaim && a.Id == id)
            .Select(a => new
            {
                a.Id,
                a.OriginalFileName,
                a.ContentType,
                a.SizeBytes,
                a.UploadedAt,
                a.WorkflowState,
                MediaUrl = $"/api/media/{a.TenantId}/{a.StorageKey}"
            })
            .FirstOrDefaultAsync();

        if (asset == null)
        {
            return NotFound();
        }

        return Ok(asset);
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadAsset(
        [FromServices] DeptDam.Services.Storage.IStorageProvider storageProvider, 
        IFormFile file)
    {
        var tenantIdClaim = User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantIdClaim))
        {
            return Forbid();
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

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
                WorkflowState = AssetWorkflowState.Draft
            };

            _dbContext.Assets.Add(asset);
            await _dbContext.SaveChangesAsync();

            return Created($"/api/assets/{asset.Id}", new
            {
                asset.Id,
                asset.OriginalFileName,
                asset.WorkflowState,
                MediaUrl = $"/api/media/{asset.TenantId}/{asset.StorageKey}"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
}
