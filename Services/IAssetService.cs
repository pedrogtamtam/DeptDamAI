using DeptDam.Models;

namespace DeptDam.Services;

public interface IAssetService
{
    Task<Asset> CreateAssetAsync(Stream fileStream, string fileName, string contentType);
    Task<AssetVersion> CreateVersionAsync(string assetId, Stream fileStream, string fileName, string contentType, string? note = null);
    Task<Stream> ApplyFiltersAsync(Stream originalStream, string contentType, int grayscale, int sepia, int brightness, int contrast);
    Task DeleteAssetAsync(string assetId);
    Task ReAnalyzeAssetAsync(string assetId);
}
