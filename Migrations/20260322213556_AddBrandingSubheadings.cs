using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeptDam.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandingSubheadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PageAssetsSubheading",
                table: "BrandingSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PageCollectionsSubheading",
                table: "BrandingSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PageWorkflowSubheading",
                table: "BrandingSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PageAssetsSubheading",
                table: "BrandingSettings");

            migrationBuilder.DropColumn(
                name: "PageCollectionsSubheading",
                table: "BrandingSettings");

            migrationBuilder.DropColumn(
                name: "PageWorkflowSubheading",
                table: "BrandingSettings");
        }
    }
}
