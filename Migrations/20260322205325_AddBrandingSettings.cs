using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeptDam.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrandingSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    HeroBadgeText = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    HeroTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HeroSubtitle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PrimaryButtonLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SecondaryButtonLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NavHomeLabel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    NavAssetsLabel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    NavCollectionsLabel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    NavWorkflowLabel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PageAssetsHeading = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PageCollectionsHeading = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PageWorkflowHeading = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandingSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrandingSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrandingSettings_TenantId",
                table: "BrandingSettings",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrandingSettings");
        }
    }
}
