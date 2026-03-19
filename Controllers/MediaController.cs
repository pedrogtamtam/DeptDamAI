using DeptDam.Services.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace DeptDam.Controllers;

[ApiController]
[Route("api/media")]
public class MediaController : ControllerBase
{
    private readonly IStorageProvider _storageProvider;
    private readonly ILogger<MediaController> _logger;
    private readonly Data.ApplicationDbContext _context;
    private readonly DeptDam.Services.IWatermarkService _watermarkService;

    public MediaController(
        IStorageProvider storageProvider, 
        ILogger<MediaController> logger,
        Data.ApplicationDbContext context,
        DeptDam.Services.IWatermarkService watermarkService)
    {
        _storageProvider = storageProvider;
        _logger = logger;
        _context = context;
        _watermarkService = watermarkService;
    }

    [HttpGet("{tenantId}/{storageKey}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> GetMedia(string tenantId, string storageKey, [FromQuery] int? w, [FromQuery] int? h, [FromQuery] int? q, [FromQuery] string? mode, [FromQuery] string? format)
    {
        try
        {
            // Note: tenantId is passed in the URL, but the local file storage provider uses the ITenantService 
            // to resolve the path. In a real-world scenario with external APIs, we might need a bypass or to 
            // ensure the tenant service handles the route data if it's a public URL. 
            // For now, the tenantId in the path is used by the frontend to construct the URL, and since it's 
            // in the same auth context, it should work for Blazor.
            
            var asset = await _context.Assets.FirstOrDefaultAsync(a => a.StorageKey == storageKey);
            var originalStream = await _storageProvider.GetFileStreamAsync(storageKey);

            bool needsWatermark = false;
            if (asset != null)
            {
                if (asset.WorkflowState != Models.AssetWorkflowState.Approved) needsWatermark = true;
                if (asset.ExpiresAt.HasValue && asset.ExpiresAt.Value < DateTime.UtcNow) needsWatermark = true;
            }

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

            if (needsWatermark && mimeType.StartsWith("image/"))
            {
                var watermarkText = asset?.ExpiresAt.HasValue == true && asset.ExpiresAt.Value < DateTime.UtcNow ? "EXPIRED" : "PREVIEW";
                var watermarkedStream = await _watermarkService.ApplyWatermarkAsync(originalStream, watermarkText, mimeType);
                originalStream.Dispose();
                originalStream = watermarkedStream;
            }

            // If no resize or quality parameters, serve current stream (potentially watermarked)
            if (!w.HasValue && !h.HasValue && !q.HasValue)
            {
                return File(originalStream, mimeType);
            }

            // Image manipulation path using ImageSharp (from current stream)
            using var image = await Image.LoadAsync(originalStream);

            if (w.HasValue || h.HasValue)
            {
                var resizeMode = mode?.ToLowerInvariant() switch
                {
                    "crop" => ResizeMode.Crop,
                    "pad" => ResizeMode.Pad,
                    "stretch" => ResizeMode.Stretch,
                    _ => ResizeMode.Max
                };

                var options = new ResizeOptions
                {
                    Size = new Size(w ?? 0, h ?? 0),
                    Mode = resizeMode
                };
                image.Mutate(x => x.Resize(options));
            }

            var outStream = new MemoryStream();
            var targetFormat = format?.ToLowerInvariant() ?? Path.GetExtension(storageKey).ToLowerInvariant().TrimStart('.');

            int quality = q ?? 80; // default quality

            switch (targetFormat)
            {
                case "jpg":
                case "jpeg":
                    await image.SaveAsJpegAsync(outStream, new JpegEncoder { Quality = quality });
                    outStream.Position = 0;
                    return File(outStream, "image/jpeg");
                case "webp":
                    await image.SaveAsWebpAsync(outStream, new WebpEncoder { Quality = quality });
                    outStream.Position = 0;
                    return File(outStream, "image/webp");
                case "png":
                    await image.SaveAsPngAsync(outStream);
                    outStream.Position = 0;
                    return File(outStream, "image/png");
                default:
                    // Fallback to jpeg
                    await image.SaveAsJpegAsync(outStream, new JpegEncoder { Quality = quality });
                    outStream.Position = 0;
                    return File(outStream, "image/jpeg");
            }
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving media: {StorageKey}", storageKey);
            return StatusCode(500, "Error serving media");
        }
    }
}
