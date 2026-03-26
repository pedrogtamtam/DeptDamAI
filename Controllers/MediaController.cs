using DeptDam.Services.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DeptDam.Controllers;

[ApiController]
[Route("api/media")]
[Authorize(AuthenticationSchemes = "Bearer,Identity.Application")]
public class MediaController : ControllerBase
{
    private readonly IStorageProvider _storageProvider;
    private readonly ILogger<MediaController> _logger;
    private readonly Data.ApplicationDbContext _context;
    private readonly DeptDam.Services.IWatermarkService _watermarkService;
    private readonly IWebHostEnvironment _env;

    public MediaController(
        IStorageProvider storageProvider, 
        ILogger<MediaController> logger,
        Data.ApplicationDbContext context,
        DeptDam.Services.IWatermarkService watermarkService,
        IWebHostEnvironment env)
    {
        _storageProvider = storageProvider;
        _logger = logger;
        _context = context;
        _watermarkService = watermarkService;
        _env = env;
    }

    [HttpGet("{tenantId}/{storageKey}")]
    [AllowAnonymous]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> GetMedia(
        string tenantId, 
        string storageKey, 
        [FromQuery] int? w, 
        [FromQuery] int? h, 
        [FromQuery] int? q, 
        [FromQuery] string? mode, 
        [FromQuery] string? format,
        [FromQuery] bool? gray,
        [FromQuery] bool? sepia,
        [FromQuery] int? bright,
        [FromQuery] int? cont)
    {
        try
        {
            var asset = await _context.Assets.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.StorageKey == storageKey);
            
            if (asset == null) return NotFound();

            // Privacy check: If the asset is not public, require authentication
            if (!asset.IsPublic && User.Identity?.IsAuthenticated != true)
            {
                return Unauthorized();
            }

            // Also check if the tenant matches if authenticated
            if (!asset.IsPublic && User.FindFirst("tenant_id")?.Value != null && asset.TenantId != User.FindFirst("tenant_id")?.Value)
            {
                 return Forbid();
            }

            bool needsWatermark = false;
            if (asset.WorkflowState != Models.AssetWorkflowState.Approved) needsWatermark = true;
            if (asset.ExpiresAt.HasValue && asset.ExpiresAt.Value < DateTime.UtcNow) needsWatermark = true;

            // Simple mime type detection based on extension
            var ext = Path.GetExtension(storageKey).ToLowerInvariant();
            var mimeType = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                ".mp4" => "video/mp4",
                _ => "application/octet-stream"
            };

            // If no resize or quality parameters, serve original stream (potentially watermarked)
            if (!w.HasValue && !h.HasValue && !q.HasValue && string.IsNullOrEmpty(format) && gray != true && sepia != true && bright == null && cont == null)
            {
                var originalStream = await _storageProvider.GetFileStreamAsync(storageKey);
                if (needsWatermark && mimeType.StartsWith("image/"))
                {
                    var watermarkText = asset?.ExpiresAt.HasValue == true && asset.ExpiresAt.Value < DateTime.UtcNow ? "EXPIRED" : "PREVIEW";
                    var watermarkedStream = await _watermarkService.ApplyWatermarkAsync(originalStream, watermarkText, mimeType);
                    originalStream.Dispose();
                    return File(watermarkedStream, mimeType);
                }
                return File(originalStream, mimeType);
            }

            // Transformation (resized/formatted/effects) path - Try cache first
            if (mimeType.StartsWith("image/"))
            {
                var cacheFileName = GetCacheFileName(storageKey, w, h, q, mode, format, gray, sepia, bright, cont, needsWatermark);
                var cachePath = Path.Combine(_env.WebRootPath, "cache", "transformations", cacheFileName);

                if (System.IO.File.Exists(cachePath))
                {
                    var cacheMime = GetMimeTypeFromFormat(format ?? ext.TrimStart('.'));
                    return File(System.IO.File.OpenRead(cachePath), cacheMime);
                }

                // Generate and Cache
                var originalStream = await _storageProvider.GetFileStreamAsync(storageKey);
                if (needsWatermark)
                {
                    var watermarkText = asset?.ExpiresAt.HasValue == true && asset.ExpiresAt.Value < DateTime.UtcNow ? "EXPIRED" : "PREVIEW";
                    var watermarkedStream = await _watermarkService.ApplyWatermarkAsync(originalStream, watermarkText, mimeType);
                    originalStream.Dispose();
                    originalStream = watermarkedStream;
                }

                using var image = await Image.LoadAsync(originalStream);
                originalStream.Dispose();

                image.Mutate(ctx => 
                {
                    if (w.HasValue || h.HasValue)
                    {
                        var resizeMode = mode?.ToLowerInvariant() switch
                        {
                            "crop" => ResizeMode.Crop,
                            "pad" => ResizeMode.Pad,
                            "stretch" => ResizeMode.Stretch,
                            _ => ResizeMode.Max
                        };
                        ctx.Resize(new ResizeOptions { Size = new Size(w ?? 0, h ?? 0), Mode = resizeMode });
                    }

                    if (gray == true) ctx.Grayscale();
                    if (sepia == true) ctx.Sepia();
                    if (bright.HasValue) ctx.Brightness((float)(bright.Value / 100.0) + 1.0f); // bright is -100 to 100, 0 is no change
                    if (cont.HasValue) ctx.Contrast((float)(cont.Value / 100.0) + 1.0f);     // cont is -100 to 100, 0 is no change
                });

                var targetFormat = format?.ToLowerInvariant() ?? ext.TrimStart('.');
                var outMime = GetMimeTypeFromFormat(targetFormat);
                var outStream = new MemoryStream();
                int quality = q ?? 85;

                switch (targetFormat)
                {
                    case "jpg" or "jpeg": await image.SaveAsJpegAsync(outStream, new JpegEncoder { Quality = quality }); break;
                    case "webp": await image.SaveAsWebpAsync(outStream, new WebpEncoder { Quality = quality }); break;
                    case "png": await image.SaveAsPngAsync(outStream); break;
                    default: await image.SaveAsJpegAsync(outStream, new JpegEncoder { Quality = quality }); outMime = "image/jpeg"; break;
                }

                // Save to cache
                var cacheDir = Path.GetDirectoryName(cachePath)!;
                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
                
                outStream.Position = 0;
                using (var fs = System.IO.File.Create(cachePath))
                {
                    await outStream.CopyToAsync(fs);
                }
                
                outStream.Position = 0;
                return File(outStream, outMime);
            }

            // Fallback for non-images if parameters passed by mistake
            return File(await _storageProvider.GetFileStreamAsync(storageKey), mimeType);
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving media: {StorageKey}", storageKey);
            return StatusCode(500, "Error serving media");
        }
    }

    private string GetCacheFileName(string storageKey, int? w, int? h, int? q, string? mode, string? format, bool? gray, bool? sepia, int? bright, int? cont, bool watermark)
    {
        var rawKey = $"{storageKey}_{w}_{h}_{q}_{mode}_{format}_{gray}_{sepia}_{bright}_{cont}_{watermark}";
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawKey));
        var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        var ext = format ?? Path.GetExtension(storageKey).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = "jpg";
        return $"{hex}.{ext}";
    }

    private string GetMimeTypeFromFormat(string format) => format.ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "webp" => "image/webp",
        "png" => "image/png",
        _ => "image/jpeg"
    };

    /// <summary>
    /// Streams a ZIP archive of the requested assets.
    /// GET /api/media/bulk-download?ids=id1,id2,...
    /// </summary>
    [HttpGet("bulk-download")]
    [Authorize]
    public async Task<IActionResult> BulkDownload([FromQuery] string ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
            return BadRequest("No asset IDs provided.");

        var assetIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var assets = await _context.Assets
            .Where(a => assetIds.Contains(a.Id))
            .ToListAsync();

        if (!assets.Any())
            return NotFound("No matching assets found.");

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets)
            {
                // Deduplicate filenames inside the ZIP
                var name = asset.OriginalFileName;
                if (usedNames.TryGetValue(name, out var count))
                {
                    usedNames[name] = ++count;
                    var ext = Path.GetExtension(name);
                    name = $"{Path.GetFileNameWithoutExtension(name)} ({count}){ext}";
                }
                else
                {
                    usedNames[name] = 1;
                }

                try
                {
                    var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await using var fileStream = await _storageProvider.GetFileStreamAsync(asset.StorageKey);
                    await fileStream.CopyToAsync(entryStream);
                }
                catch (FileNotFoundException)
                {
                    // Skip missing files rather than aborting the whole ZIP
                }
            }
        }

        ms.Position = 0;
        return File(ms, "application/zip", $"assets-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
    }
}

