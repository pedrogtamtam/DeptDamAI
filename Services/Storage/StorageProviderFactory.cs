using DeptDam.Data;
using Microsoft.EntityFrameworkCore;

namespace DeptDam.Services.Storage;

/// <summary>
/// Resolves the correct IStorageProvider based on the current tenant's storage settings.
/// </summary>
public class StorageProviderFactory : IStorageProvider
{
    private readonly ITenantService _tenantService;
    private readonly IWebHostEnvironment _environment;
    private readonly ApplicationDbContext _dbContext;

    public StorageProviderFactory(ITenantService tenantService, IWebHostEnvironment environment, ApplicationDbContext dbContext)
    {
        _tenantService = tenantService;
        _environment = environment;
        _dbContext = dbContext;
    }

    private IStorageProvider ResolveProvider()
    {
        var tenantId = _tenantService.GetCurrentTenantId();
        if (!string.IsNullOrEmpty(tenantId))
        {
            var settings = _dbContext.StorageSettings
                .IgnoreQueryFilters()
                .FirstOrDefault(s => s.TenantId == tenantId);

            if (settings is { ProviderType: "AzureBlob" }
                && !string.IsNullOrWhiteSpace(settings.AzureBlobConnectionString)
                && !string.IsNullOrWhiteSpace(settings.AzureBlobContainerName))
            {
                return new AzureBlobStorageProvider(settings.AzureBlobConnectionString, settings.AzureBlobContainerName, _tenantService);
            }
        }

        return new LocalFileSystemStorageProvider(_tenantService, _environment);
    }

    public Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default)
        => ResolveProvider().UploadFileAsync(fileStream, fileName, contentType, cancellationToken);

    public Task DeleteFileAsync(string storageKey, CancellationToken cancellationToken = default)
        => ResolveProvider().DeleteFileAsync(storageKey, cancellationToken);

    public Task<Stream> GetFileStreamAsync(string storageKey, CancellationToken cancellationToken = default)
        => ResolveProvider().GetFileStreamAsync(storageKey, cancellationToken);

    public string GetFileUrl(string storageKey)
        => ResolveProvider().GetFileUrl(storageKey);
}
