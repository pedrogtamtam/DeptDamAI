namespace DeptDam.Services.Storage;

public class LocalFileSystemStorageProvider : IStorageProvider
{
    private readonly ITenantService _tenantService;
    private readonly IWebHostEnvironment _environment;

    public LocalFileSystemStorageProvider(ITenantService tenantService, IWebHostEnvironment environment)
    {
        _tenantService = tenantService;
        _environment = environment;
    }

    private string GetTenantStoragePath()
    {
        var tenantId = _tenantService.GetCurrentTenantId() ?? "default";
        var basePath = Path.Combine(_environment.ContentRootPath, "App_Data", "Tenants", tenantId, "Assets");
        if (!Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
        }
        return basePath;
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var basePath = GetTenantStoragePath();
        
        var extension = Path.GetExtension(fileName);
        var storageKey = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(basePath, storageKey);

        using var fileStreamToWrite = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await fileStream.CopyToAsync(fileStreamToWrite, cancellationToken);
        
        return storageKey;
    }

    public Task DeleteFileAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var basePath = GetTenantStoragePath();
        var filePath = Path.Combine(basePath, storageKey);
        
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    public Task<Stream> GetFileStreamAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var basePath = GetTenantStoragePath();
        var filePath = Path.Combine(basePath, storageKey);
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {storageKey}");
        }

        Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public string GetFileUrl(string storageKey)
    {
        var tenantId = _tenantService.GetCurrentTenantId() ?? "default";
        return $"/api/media/{tenantId}/{storageKey}";
    }
}
