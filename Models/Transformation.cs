using System.ComponentModel.DataAnnotations;

namespace DeptDam.Models;

/// <summary>Per-tenant predefined download format/size combination.</summary>
public class Transformation : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Target width in pixels. Null = keep original.</summary>
    public int? Width { get; set; }

    /// <summary>Target height in pixels. Null = keep original.</summary>
    public int? Height { get; set; }

    /// <summary>Output format: "original", "jpeg", "png", "webp".</summary>
    [MaxLength(10)]
    public string Format { get; set; } = "original";

    /// <summary>JPEG/WebP compression quality 10–100.</summary>
    public int Quality { get; set; } = 85;

    /// <summary>Resize mode: "max", "crop", "pad", "stretch".</summary>
    [MaxLength(20)]
    public string ResizeMode { get; set; } = "max";

    public bool Grayscale { get; set; } = false;
    public bool Sepia { get; set; } = false;
    public int Brightness { get; set; } = 0; // -100 to 100 or similar
    public int Contrast { get; set; } = 0;   // -100 to 100 or similar

    public int SortOrder { get; set; }
}
