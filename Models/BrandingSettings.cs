using System.ComponentModel.DataAnnotations;

namespace DeptDam.Models;

/// <summary>
/// Stores per-tenant branding configuration for the homepage and navigation labels.
/// </summary>
public class BrandingSettings : ITenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    // ?? Homepage hero ??????????????????????????????????????????????????????????

    [MaxLength(100)]
    public string HeroBadgeText { get; set; } = "WELCOME BACK";

    [MaxLength(200)]
    public string HeroTitle { get; set; } = "Your Creative Workspace";

    [MaxLength(500)]
    public string HeroSubtitle { get; set; } =
        "Manage, organize, and distribute your digital assets with enterprise-grade security and speed.";

    [MaxLength(100)]
    public string PrimaryButtonLabel { get; set; } = "Upload New Asset";

    [MaxLength(100)]
    public string SecondaryButtonLabel { get; set; } = "Create Collection";

    // ?? Navigation labels ??????????????????????????????????????????????????????

    [MaxLength(60)]
    public string NavHomeLabel { get; set; } = "Home";

    [MaxLength(60)]
    public string NavAssetsLabel { get; set; } = "Assets";

    [MaxLength(60)]
    public string NavCollectionsLabel { get; set; } = "Collections";

    [MaxLength(60)]
    public string NavWorkflowLabel { get; set; } = "Workflow";

    // ?? Page headings ??????????????????????????????????????????????????????????

    [MaxLength(200)]
    public string PageAssetsHeading { get; set; } = "Asset Library";

    [MaxLength(500)]
    public string PageAssetsSubheading { get; set; } = "Browse, search and manage your digital assets.";

    [MaxLength(200)]
    public string PageCollectionsHeading { get; set; } = "Collections";

    [MaxLength(500)]
    public string PageCollectionsSubheading { get; set; } = "Group and share your assets through static or dynamic collections.";

    [MaxLength(200)]
    public string PageWorkflowHeading { get; set; } = "Workflow";

    [MaxLength(500)]
    public string PageWorkflowSubheading { get; set; } = "Manage asset approvals and lifecycle stages.";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
