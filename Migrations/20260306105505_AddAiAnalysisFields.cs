using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeptDam.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAnalysisFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractedText",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacesDetected",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtractedText",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "FacesDetected",
                table: "Assets");
        }
    }
}
