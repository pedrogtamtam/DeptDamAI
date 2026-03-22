using System.ComponentModel.DataAnnotations;

namespace DeptDam.Models;

/// <summary>
/// Stores per-tenant Azure Blob Storage configuration.
/// </summary>
public class StorageSettings : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    /// <summary>The storage provider type: "Local" or "AzureBlob"</summary>
    public string ProviderType { get; set; } = "Local";

    /// <summary>Azure Blob Storage connection string</summary>
    public string? AzureBlobConnectionString { get; set; }

    /// <summary>Azure Blob Storage container name</summary>
    public string? AzureBlobContainerName { get; set; }

    /// <summary>
    /// Optional CDN base URL (e.g. https://cdn.example.com).
    /// When set, asset URLs are served as {CdnBaseUrl}/{tenantId}/{storageKey}
    /// instead of being routed through the local media controller.
    /// </summary>
    [MaxLength(500)]
    public string? CdnBaseUrl { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
