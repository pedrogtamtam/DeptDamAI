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
    private readonly IAuditService _auditService;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly IReAnalysisTracker _tracker;

    public AssetService(
        ApplicationDbContext context, 
        IStorageProvider storageProvider, 
        IHttpContextAccessor httpContextAccessor, 
        ITenantService tenantService,
        IAiAnalysisService aiService,
        IAuditService auditService,
        IBackgroundTaskQueue taskQueue,
        IReAnalysisTracker tracker)
    {
        _context = context;
        _storageProvider = storageProvider;
        _httpContextAccessor = httpContextAccessor;
        _tenantService = tenantService;
        _aiService = aiService;
        _auditService = auditService;
        _taskQueue = taskQueue;
        _tracker = tracker;
    }

    public async Task<Asset> CreateAssetAsync(Stream fileStream, string fileName, string contentType, bool useAi = false, bool isPublic = false)
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

        // Run AI Analysis (only when requested)
        AiAnalysisResult aiResult = useAi
            ? await _aiService.AnalyzeAssetAsync(ms, fileName)
            : AiAnalysisResult.Empty;
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
            WorkflowState = AssetWorkflowState.Draft,
            IsPublic = isPublic
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

        await _auditService.LogAsync(
            authorId:   userId,
            authorName: _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? userId,
            action:     "AssetUploaded",
            entityType: "Asset",
            entityId:   asset.Id,
            entityName: asset.OriginalFileName);

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
            .Include(a => a.Tags)
            .Include(a => a.MetadataValues)
            .Include(a => a.ShareLinks)
            .FirstOrDefaultAsync(a => a.Id == assetId);

        if (asset != null)
        {
            // Snapshot tag IDs before the AssetTag rows are removed
            var tagIds = asset.Tags.Select(at => at.TagId).ToList();

            // Delete files from storage
            foreach (var version in asset.Versions)
            {
                await _storageProvider.DeleteFileAsync(version.StorageKey);
            }
            await _storageProvider.DeleteFileAsync(asset.StorageKey);

            // Remove comments separately (no navigation property on Asset)
            var comments = await _context.AssetComments.Where(c => c.AssetId == assetId).ToListAsync();
            _context.AssetComments.RemoveRange(comments);

        // Remove join-table rows that use Restrict delete behavior with identifying FKs
        _context.AssetTags.RemoveRange(asset.Tags);

        var collectionAssets = await _context.CollectionAssets
            .Where(ca => ca.AssetId == assetId).ToListAsync();
        _context.CollectionAssets.RemoveRange(collectionAssets);

            _context.Assets.Remove(asset);
            await _context.SaveChangesAsync();

            // Delete tags that are no longer used by any asset
            foreach (var tagId in tagIds)
            {
                var isUsedElsewhere = await _context.AssetTags.AnyAsync(at => at.TagId == tagId);
                if (!isUsedElsewhere)
                {
                    var tag = await _context.Tags.FindAsync(tagId);
                    if (tag != null) _context.Tags.Remove(tag);
                }
            }
            await _context.SaveChangesAsync();

            var delUser = _httpContextAccessor.HttpContext?.User;
            var delUserId = delUser?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
            await _auditService.LogAsync(
                authorId:   delUserId,
                authorName: delUser?.Identity?.Name ?? delUserId,
                action:     "AssetDeleted",
                entityType: "Asset",
                entityId:   assetId,
                entityName: asset.OriginalFileName);
        }
    }
    
    public async Task ReAnalyzeAssetAsync(string assetId)
    {
        var asset = await _context.Assets
            .Include(a => a.Tags)
                .ThenInclude(at => at.Tag)
            .FirstOrDefaultAsync(a => a.Id == assetId);
        if (asset == null) return;

        // Ensure the tenant context is set — when called from a background scope there is
        // no HttpContext or auth state, so GetCurrentTenantId() would return null and the
        // storage provider would look in the wrong folder.
        _tenantService.SetCurrentTenantId(asset.TenantId);

        using var originalStream = await _storageProvider.GetFileStreamAsync(asset.StorageKey);
        using var ms = new MemoryStream();
        await originalStream.CopyToAsync(ms);
        ms.Position = 0;

        var aiResult = await _aiService.AnalyzeAssetAsync(ms, asset.OriginalFileName);

        asset.ExtractedText = aiResult.ExtractedText;
        asset.FacesDetected = aiResult.FacesDetected.Any() ? string.Join(", ", aiResult.FacesDetected) : null;

        var tenantId = _tenantService.GetCurrentTenantId()!;
        var newTagNames = aiResult.Tags.Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove AssetTags no longer returned by AI; delete the Tag itself if it becomes orphaned
        var tagsToRemove = asset.Tags
            .Where(at => at.Tag != null && !newTagNames.Contains(at.Tag.Name))
            .ToList();
        foreach (var assetTag in tagsToRemove)
        {
            _context.AssetTags.Remove(assetTag);
            var isUsedElsewhere = await _context.AssetTags
                .AnyAsync(at => at.TagId == assetTag.TagId && at.AssetId != assetId);
            if (!isUsedElsewhere && assetTag.Tag != null)
                _context.Tags.Remove(assetTag.Tag);
        }

        // Flush deletions so the change tracker is clean before adding new links
        await _context.SaveChangesAsync();

        // Add tags that are new (query DB instead of stale in-memory collection)
        foreach (var tagName in newTagNames)
        {
            var alreadyLinked = await _context.AssetTags
                .AnyAsync(at => at.AssetId == assetId && at.Tag!.Name == tagName);
            if (alreadyLinked) continue;

            var tag = await _context.Tags.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Name == tagName);
            if (tag == null)
            {
                tag = new Models.Tag { TenantId = tenantId, Name = tagName };
                _context.Tags.Add(tag);
            }
            _context.AssetTags.Add(new AssetTag { TenantId = tenantId, AssetId = assetId, TagId = tag.Id });
        }

        await _context.SaveChangesAsync();
    }

    public Task EnqueueReAnalysisAsync(string assetId)
    {
        _tracker.Start(assetId);
        _taskQueue.Enqueue(async (sp, ct) =>
        {
            try
            {
                var svc = sp.GetRequiredService<IAssetService>();
                await svc.ReAnalyzeAssetAsync(assetId);
            }
            finally
            {
                sp.GetRequiredService<IReAnalysisTracker>().Complete(assetId);
            }
        });
        return Task.CompletedTask;
    }

    // ?? Bulk operations ??????????????????????????????????????????????????????

    public async Task BulkAddTagAsync(IEnumerable<string> assetIds, string tagName)
    {
        var tenantId = _tenantService.GetCurrentTenantId()!;
        var ids = assetIds.ToList();

        var tag = await _context.Tags.FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Name == tagName);
        if (tag == null)
        {
            tag = new Models.Tag { TenantId = tenantId, Name = tagName };
            _context.Tags.Add(tag);
            await _context.SaveChangesAsync();
        }

        foreach (var assetId in ids)
        {
            var alreadyTagged = await _context.AssetTags
                .AnyAsync(at => at.AssetId == assetId && at.TagId == tag.Id);
            if (!alreadyTagged)
                _context.AssetTags.Add(new AssetTag { TenantId = tenantId, AssetId = assetId, TagId = tag.Id });
        }

        await _context.SaveChangesAsync();
    }

    public async Task BulkMoveToCollectionAsync(IEnumerable<string> assetIds, string collectionId)
    {
        var tenantId = _tenantService.GetCurrentTenantId()!;
        var ids = assetIds.ToList();

        foreach (var assetId in ids)
        {
            var exists = await _context.CollectionAssets
                .AnyAsync(ca => ca.CollectionId == collectionId && ca.AssetId == assetId);
            if (!exists)
                _context.CollectionAssets.Add(new CollectionAsset
                {
                    TenantId = tenantId,
                    CollectionId = collectionId,
                    AssetId = assetId
                });
        }

        await _context.SaveChangesAsync();
    }

    public async Task BulkSetWorkflowStateAsync(IEnumerable<string> assetIds, AssetWorkflowState newState)
    {
        var ids = assetIds.ToList();
        var assets = await _context.Assets.Where(a => ids.Contains(a.Id)).ToListAsync();
        foreach (var asset in assets)
            asset.WorkflowState = newState;
        await _context.SaveChangesAsync();
    }
}
