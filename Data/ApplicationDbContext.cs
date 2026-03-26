using System.Linq.Expressions;
using System.Text.Json;
using DeptDam.Models;
using DeptDam.Services;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DeptDam.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    private readonly ITenantService? _tenantService;
    public string? CurrentTenantId => _tenantService?.GetCurrentTenantId();

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IEnumerable<ITenantService> tenantServices) 
        : base(options)
    {
        _tenantService = tenantServices.FirstOrDefault();
    }

    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<ApiClient> ApiClients { get; set; }
    public DbSet<Asset> Assets { get; set; }
    public DbSet<Tag> Tags { get; set; }
    public DbSet<AssetTag> AssetTags { get; set; }
    public DbSet<AssetVersion> AssetVersions { get; set; }
    public DbSet<Collection> Collections { get; set; }
    public DbSet<CollectionAsset> CollectionAssets { get; set; }
    public DbSet<CollectionTag> CollectionTags { get; set; }
    public DbSet<CustomField> CustomFields { get; set; }
    public DbSet<AssetMetadataValue> AssetMetadataValues { get; set; }
    public DbSet<ShareLink> ShareLinks { get; set; }
    public DbSet<AssetComment> AssetComments { get; set; }
    public DbSet<AiSettings> AiSettings { get; set; }
    public DbSet<SmtpSettings> SmtpSettings { get; set; }
    public DbSet<StorageSettings> StorageSettings { get; set; }
    public DbSet<BrandingSettings> BrandingSettings { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<DownloadPreset> DownloadPresets { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Tenant>().HasIndex(t => t.Subdomain).IsUnique();

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var tenantIdProperty = Expression.PropertyOrField(parameter, nameof(ITenantEntity.TenantId));

                // e => CurrentTenantId == null || e.TenantId == CurrentTenantId
                var currentTenantIdProperty = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
                
                var equalExpression = Expression.Equal(tenantIdProperty, currentTenantIdProperty);
                
                // Allow querying everything if CurrentTenantId is null (e.g. at design time or in admin tasks)
                var isNullExpression = Expression.Equal(currentTenantIdProperty, Expression.Constant(null, typeof(string)));
                var orExpression = Expression.OrElse(isNullExpression, equalExpression);

                var lambda = Expression.Lambda(orExpression, parameter);
                
                builder.Entity(entityType.ClrType).HasQueryFilter(lambda);

                // Add Index for TenantId for performance
                builder.Entity(entityType.ClrType).HasIndex(nameof(ITenantEntity.TenantId));
            }
        }

        // Configure ApplicationUser to require TenantId if it implements ITenantEntity
        // And adjust uniqueness: UserName should be unique per tenant, not globally!
        if (typeof(ITenantEntity).IsAssignableFrom(typeof(ApplicationUser)))
        {
            var entityType = builder.Model.FindEntityType(typeof(ApplicationUser));
            var userNameIndex = entityType?.GetIndexes().FirstOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == "NormalizedUserName");
            if (userNameIndex != null)
            {
                entityType?.RemoveIndex(userNameIndex);
            }

            builder.Entity<ApplicationUser>(b => 
            {
                // Add unique index per tenant
                b.HasIndex("NormalizedUserName", "TenantId").HasDatabaseName("UserNameIndex").IsUnique();
            });
        }
        
        if (typeof(ITenantEntity).IsAssignableFrom(typeof(ApplicationRole)))
        {
            var entityType = builder.Model.FindEntityType(typeof(ApplicationRole));
            var roleNameIndex = entityType?.GetIndexes().FirstOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == "NormalizedName");
            if (roleNameIndex != null)
            {
                entityType?.RemoveIndex(roleNameIndex);
            }

            builder.Entity<ApplicationRole>(b => 
            {
                // Add unique index per tenant for roles
                b.HasIndex("NormalizedName", "TenantId").HasDatabaseName("RoleNameIndex").IsUnique();
                b.HasOne(r => r.Tenant).WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);

                var permissionsComparer = new ValueComparer<List<string>>(
                    (c1, c2) => (c1 ?? new List<string>()).SequenceEqual(c2 ?? new List<string>()),
                    c => (c ?? new List<string>()).Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => (c ?? new List<string>()).ToList());

                b.Property(r => r.Permissions)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                        v => string.IsNullOrEmpty(v)
                            ? new List<string>()
                            : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions)null) ?? new List<string>())
                    .Metadata.SetValueComparer(permissionsComparer);
                
                b.Property(r => r.Permissions).HasColumnName("Permissions");
            });
        }

        builder.Entity<AssetTag>(b =>
        {
            b.HasKey(at => new { at.AssetId, at.TagId });
            b.HasOne(at => at.Tenant).WithMany().HasForeignKey(at => at.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(at => at.Asset).WithMany(a => a.Tags).HasForeignKey(at => at.AssetId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(at => at.Tag).WithMany().HasForeignKey(at => at.TagId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Asset>(b =>
        {
            b.HasOne(a => a.Tenant).WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(a => a.UploadedBy).WithMany().HasForeignKey(a => a.UploadedById).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AssetVersion>(b =>
        {
            b.HasOne(v => v.Tenant).WithMany().HasForeignKey(v => v.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(v => v.Asset).WithMany(a => a.Versions).HasForeignKey(v => v.AssetId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(v => v.CreatedBy).WithMany().HasForeignKey(v => v.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Tag>(b =>
        {
            b.HasIndex("Name", "TenantId").IsUnique();
            b.HasOne(t => t.Tenant).WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CollectionAsset>(b =>
        {
            b.HasKey(ca => new { ca.CollectionId, ca.AssetId });
            b.HasOne(ca => ca.Tenant).WithMany().HasForeignKey(ca => ca.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(ca => ca.Asset).WithMany().HasForeignKey(ca => ca.AssetId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(ca => ca.Collection).WithMany(c => c.Assets).HasForeignKey(ca => ca.CollectionId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Collection>(b =>
        {
            b.HasOne(c => c.Tenant).WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(c => c.CreatedBy).WithMany().HasForeignKey(c => c.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CollectionTag>(b =>
        {
            b.HasKey(ct => new { ct.CollectionId, ct.TagId });
            b.HasOne(ct => ct.Tenant).WithMany().HasForeignKey(ct => ct.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(ct => ct.Tag).WithMany().HasForeignKey(ct => ct.TagId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(ct => ct.Collection).WithMany(c => c.DynamicTags).HasForeignKey(ct => ct.CollectionId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<CustomField>(b =>
        {
            b.HasOne(f => f.Tenant).WithMany().HasForeignKey(f => f.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AssetMetadataValue>(b =>
        {
            b.HasKey(mv => new { mv.AssetId, mv.CustomFieldId });
            b.HasOne(mv => mv.Tenant).WithMany().HasForeignKey(mv => mv.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(mv => mv.Asset).WithMany(a => a.MetadataValues).HasForeignKey(mv => mv.AssetId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(mv => mv.CustomField).WithMany().HasForeignKey(mv => mv.CustomFieldId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ShareLink>(b =>
        {
            b.HasIndex(sl => sl.Token).IsUnique();
            b.HasOne(sl => sl.Tenant).WithMany().HasForeignKey(sl => sl.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(sl => sl.Asset).WithMany(a => a.ShareLinks).HasForeignKey(sl => sl.AssetId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(sl => sl.CreatedBy).WithMany().HasForeignKey(sl => sl.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AssetComment>(b =>
        {
            b.HasOne(c => c.Tenant).WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(c => c.Asset).WithMany().HasForeignKey(c => c.AssetId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(c => c.Author).WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
