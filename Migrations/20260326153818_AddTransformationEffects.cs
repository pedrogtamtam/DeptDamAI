using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeptDam.Migrations
{
    /// <inheritdoc />
    public partial class AddTransformationEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Brightness",
                table: "Transformations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Contrast",
                table: "Transformations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Grayscale",
                table: "Transformations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ResizeMode",
                table: "Transformations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Sepia",
                table: "Transformations",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Brightness",
                table: "Transformations");

            migrationBuilder.DropColumn(
                name: "Contrast",
                table: "Transformations");

            migrationBuilder.DropColumn(
                name: "Grayscale",
                table: "Transformations");

            migrationBuilder.DropColumn(
                name: "ResizeMode",
                table: "Transformations");

            migrationBuilder.DropColumn(
                name: "Sepia",
                table: "Transformations");
        }
    }
}
