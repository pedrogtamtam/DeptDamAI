using System.ComponentModel.DataAnnotations;

namespace DeptDam.Models;

/// <summary>
/// Stores per-tenant AI configuration (Google Gemini API key, etc.)
/// </summary>
public class AiSettings : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    /// <summary>Google Gemini API key for this tenant</summary>
    public string? GoogleAiApiKey { get; set; }

    /// <summary>Which Gemini model to use. Default: gemini-1.5-flash</summary>
    public string GeminiModel { get; set; } = "gemini-2.0-flash";

    /// <summary>Whether AI auto-tagging is enabled on upload</summary>
    public bool AutoAnalyzeOnUpload { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
