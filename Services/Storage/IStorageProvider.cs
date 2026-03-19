namespace DeptDam.Services.Storage;

public interface IStorageProvider
{
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string storageKey, CancellationToken cancellationToken = default);
    Task<Stream> GetFileStreamAsync(string storageKey, CancellationToken cancellationToken = default);
    string GetFileUrl(string storageKey);
}
