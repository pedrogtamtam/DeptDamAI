using DeptDam.Data;
using DeptDam.Models;
using DeptDam.Services.Storage;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using System.Security.Claims;
using MetadataExtractor;
using System.Text.Json;

namespace DeptDam.Services;

public class AssetService : IAssetService
{
    private readonly ApplicationDbContext _context;
    private readonly IStorageProvider _storageProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantService _tenantService;
    private readonly IAiAnalysisService _aiService;

    public AssetService(
        ApplicationDbContext context, 
        IStorageProvider storageProvider, 
        IHttpContextAccessor httpContextAccessor, 
        ITenantService tenantService,
        IAiAnalysisService aiService)
    {
        _context = context;
        _storageProvider = storageProvider;
        _httpContextAccessor = httpContextAccessor;
        _tenantService = tenantService;
        _aiService = aiService;
    }

    public async Task<Asset> CreateAssetAsync(Stream fileStream, string fileName, string contentType)
    {
        var tenantId = _tenantService.GetCurrentTenantId();
        if (string.IsNullOrEmpty(tenantId))
             throw new UnauthorizedAccessException("Tenant context is missing");

        var userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms);
        ms.Position = 0;

        string fileHash;
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(ms);
            fileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
        ms.Position = 0;

        // Check for duplicates
        if (await _context.Assets.AnyAsync(a => a.TenantId == tenantId && a.FileHash == fileHash))
        {
            throw new InvalidOperationException("An exact duplicate of this file already exists in your library.");
        }

        // Extract EXIF/Metadata
        string? exifData = null;
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(ms);
            var metadataDict = new Dictionary<string, string>();
            foreach (var directory in directories)
            {
                foreach (var tag in directory.Tags)
                {
                    var key = $"{directory.Name} - {tag.Name}";
                    // Limit dictionary size to prevent huge JSON blobs
                    if (!metadataDict.ContainsKey(key) && metadataDict.Count < 200)
                    {
                        var value = tag.Description;
                        if (value != null && value.Length > 300) value = value.Substring(0, 300) + "...";
                        metadataDict[key] = value ?? "";
                    }
                }
            }
            if (metadataDict.Any())
            {
                exifData = JsonSerializer.Serialize(metadataDict);
            }
        }
        catch (Exception)
        {
            // Ignore if it's not an image or doesn't support metadata
        }
        finally
        {
            ms.Position = 0;
        }

        // Run AI Analysis
        var aiResult = await _aiService.AnalyzeAssetAsync(ms, fileName);
        ms.Position = 0;

        // Upload to storage
        var storageKey = await _storageProvider.UploadFileAsync(ms, fileName, contentType);

        var asset = new Asset
        {
            TenantId = tenantId,
            OriginalFileName = fileName,
            ContentType = contentType,
            SizeBytes = ms.Length,
            StorageKey = storageKey,
            FileHash = fileHash,
            ExifData = exifData,
            ExtractedText = aiResult.ExtractedText,
            FacesDetected = aiResult.FacesDetected.Any() ? string.Join(", ", aiResult.FacesDetected) : null,
            UploadedById = userId,
            UploadedAt = DateTime.UtcNow,
            WorkflowState = AssetWorkflowState.Draft
        };

        _context.Assets.Add(asset);

        // Auto-tag via AI
        foreach (var tagName in aiResult.Tags.Distinct())
        {
            var tag = await _context.Tags.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Name == tagName);
            if (tag == null)
            {
                tag = new Models.Tag { TenantId = tenantId, Name = tagName };
                _context.Tags.Add(tag);
            }
            asset.Tags.Add(new AssetTag { TenantId = tenantId, Tag = tag, Asset = asset });
        }

        await _context.SaveChangesAsync();

        // Create version 1
        var version = new AssetVersion
        {
            AssetId = asset.Id,
            TenantId = tenantId,
            StorageKey = storageKey,
            SizeBytes = ms.Length,
            FileHash = fileHash,
            VersionNumber = 1,
            CreatedById = userId,
            VersionNote = "Initial upload",
            CreatedAt = DateTime.UtcNow
        };

        _context.AssetVersions.Add(version);

        _context.AssetComments.Add(new AssetComment
        {
            AssetId = asset.Id,
            TenantId = tenantId,
            AuthorId = userId,
            IsSystemEvent = true,
            EventType = "asset_created",
            Body = "Asset uploaded"
        });

        await _context.SaveChangesAsync();

        return asset;
    }

    public async Task<AssetVersion> CreateVersionAsync(string assetId, Stream fileStream, string fileName, string contentType, string? note = null)
    {
        var asset = await _context.Assets.FirstOrDefaultAsync(a => a.Id == assetId);
        if (asset == null)
            throw new ArgumentException("Asset not found");

        var tenantId = _tenantService.GetCurrentTenantId();
        if (string.IsNullOrEmpty(tenantId))
             throw new UnauthorizedAccessException("Tenant context is missing");

        var userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms);
        ms.Position = 0;

        string fileHash;
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(ms);
            fileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
        ms.Position = 0;

        // Upload file
        var storageKey = await _storageProvider.UploadFileAsync(ms, fileName, contentType);

        // Get max version number
        var latestVersion = await _context.AssetVersions
            .Where(v => v.AssetId == assetId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync();
            
        var versionNumber = (latestVersion?.VersionNumber ?? 0) + 1;

        var version = new AssetVersion
        {
            AssetId = assetId,
            TenantId = tenantId,
            StorageKey = storageKey,
            SizeBytes = ms.Length,
            FileHash = fileHash,
            VersionNumber = versionNumber,
            CreatedById = userId,
            VersionNote = note,
            CreatedAt = DateTime.UtcNow
        };

        // Update the asset to the latest version details
        asset.StorageKey = storageKey;
        asset.SizeBytes = version.SizeBytes;
        asset.FileHash = version.FileHash;
        
        _context.AssetVersions.Add(version);

        _context.AssetComments.Add(new AssetComment
        {
            AssetId = asset.Id,
            TenantId = tenantId,
            AuthorId = userId,
            IsSystemEvent = true,
            EventType = "version_created",
            Body = $"Version {versionNumber} uploaded" + (string.IsNullOrWhiteSpace(note) ? "" : $": {note}")
        });

        await _context.SaveChangesAsync();

        return version;
    }

    public async Task<Stream> ApplyFiltersAsync(Stream originalStream, string contentType, int grayscale, int sepia, int brightness, int contrast)
    {
        using var image = await Image.LoadAsync(originalStream);
        
        image.Mutate(x => {
            if (grayscale > 0) x.Grayscale(grayscale / 100f);
            if (sepia > 0) x.Sepia(sepia / 100f);
            
            // ImageSharp brightness is scale (1.0 = normal)
            if (brightness != 100) x.Brightness(brightness / 100f);
            if (contrast != 100) x.Contrast(contrast / 100f);
        });

        var outStream = new MemoryStream();
        
        if (contentType == "image/png")
        {
             await image.SaveAsPngAsync(outStream);
        }
        else
        {
             await image.SaveAsJpegAsync(outStream, new JpegEncoder { Quality = 90 });
        }

        outStream.Position = 0;
        return outStream;
    }

    public async Task DeleteAssetAsync(string assetId)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || !user.IsInRole("DAM_Admin"))
        {
            throw new UnauthorizedAccessException("Only DAM Admins can delete assets.");
        }

        var asset = await _context.Assets
            .Include(a => a.Versions)
            .FirstOrDefaultAsync(a => a.Id == assetId);

        if (asset != null)
        {
            // Delete files from storage
            foreach (var version in asset.Versions)
            {
                await _storageProvider.DeleteFileAsync(version.StorageKey);
            }
            await _storageProvider.DeleteFileAsync(asset.StorageKey);

            _context.Assets.Remove(asset);
            await _context.SaveChangesAsync();
        }
    }
    
    public async Task ReAnalyzeAssetAsync(string assetId)
    {
        var asset = await _context.Assets.FirstOrDefaultAsync(a => a.Id == assetId);
        if (asset == null) return;
        
        using var originalStream = await _storageProvider.GetFileStreamAsync(asset.StorageKey);
        using var ms = new MemoryStream();
        await originalStream.CopyToAsync(ms);
        ms.Position = 0;
        
        var aiResult = await _aiService.AnalyzeAssetAsync(ms, asset.OriginalFileName);
        
        asset.ExtractedText = aiResult.ExtractedText;
        asset.FacesDetected = aiResult.FacesDetected.Any() ? string.Join(", ", aiResult.FacesDetected) : null;
        
        // Update tags
        var tenantId = _tenantService.GetCurrentTenantId()!;
        foreach (var tagName in aiResult.Tags.Distinct())
        {
            if (!asset.Tags.Any(at => at.Tag?.Name == tagName))
            {
                var tag = await _context.Tags.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Name == tagName);
                if (tag == null)
                {
                    tag = new Models.Tag { TenantId = tenantId, Name = tagName };
                    _context.Tags.Add(tag);
                }
                asset.Tags.Add(new AssetTag { TenantId = tenantId, Tag = tag, Asset = asset });
            }
        }
        
        await _context.SaveChangesAsync();
    }
}
