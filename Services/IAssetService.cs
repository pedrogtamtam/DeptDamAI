using DeptDam.Models;

namespace DeptDam.Services;

public interface IAssetService
{
    Task<Asset> CreateAssetAsync(Stream fileStream, string fileName, string contentType, bool useAi = false);
    Task<AssetVersion> CreateVersionAsync(string assetId, Stream fileStream, string fileName, string contentType, string? note = null);
    Task<Stream> ApplyFiltersAsync(Stream originalStream, string contentType, int grayscale, int sepia, int brightness, int contrast);
    Task DeleteAssetAsync(string assetId);
    Task ReAnalyzeAssetAsync(string assetId);
    Task EnqueueReAnalysisAsync(string assetId);

    // ?? Bulk operations ??????????????????????????????????????????????????????
    Task BulkAddTagAsync(IEnumerable<string> assetIds, string tagName);
    Task BulkMoveToCollectionAsync(IEnumerable<string> assetIds, string collectionId);
    Task BulkSetWorkflowStateAsync(IEnumerable<string> assetIds, AssetWorkflowState newState);
}
