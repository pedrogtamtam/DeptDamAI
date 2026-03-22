using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace DeptDam.Services.Storage;

public class AzureBlobStorageProvider : IStorageProvider
{
    private readonly BlobContainerClient _containerClient;
    private readonly ITenantService _tenantService;

    public AzureBlobStorageProvider(string connectionString, string containerName, ITenantService tenantService)
    {
        _tenantService = tenantService;
        var serviceClient = new BlobServiceClient(connectionString);
        _containerClient = serviceClient.GetBlobContainerClient(containerName);
        _containerClient.CreateIfNotExists(PublicAccessType.None);
    }

    private string GetBlobName(string storageKey)
    {
        var tenantId = _tenantService.GetCurrentTenantId() ?? "default";
        return $"{tenantId}/{storageKey}";
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName);
        var storageKey = $"{Guid.NewGuid():N}{extension}";
        var blobName = GetBlobName(storageKey);

        var blobClient = _containerClient.GetBlobClient(blobName);
        var headers = new BlobHttpHeaders { ContentType = contentType };
        await blobClient.UploadAsync(fileStream, new BlobUploadOptions { HttpHeaders = headers }, cancellationToken);

        return storageKey;
    }

    public async Task DeleteFileAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var blobName = GetBlobName(storageKey);
        var blobClient = _containerClient.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task<Stream> GetFileStreamAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var blobName = GetBlobName(storageKey);
        var blobClient = _containerClient.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            throw new FileNotFoundException($"Blob not found: {storageKey}");
        }

        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    public string GetFileUrl(string storageKey)
    {
        var tenantId = _tenantService.GetCurrentTenantId() ?? "default";
        return $"/api/media/{tenantId}/{storageKey}";
    }
}
