using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeptDam.Migrations
{
    /// <inheritdoc />
    public partial class AddShareLinksToAsset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "UserGroups",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Tags",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "AssetId1",
                table: "ShareLinks",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserGroups_Name_TenantId",
                table: "UserGroups",
                columns: new[] { "Name", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name_TenantId",
                table: "Tags",
                columns: new[] { "Name", "TenantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShareLinks_AssetId1",
                table: "ShareLinks",
                column: "AssetId1");

            migrationBuilder.AddForeignKey(
                name: "FK_ShareLinks_Assets_AssetId1",
                table: "ShareLinks",
                column: "AssetId1",
                principalTable: "Assets",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShareLinks_Assets_AssetId1",
                table: "ShareLinks");

            migrationBuilder.DropIndex(
                name: "IX_UserGroups_Name_TenantId",
                table: "UserGroups");

            migrationBuilder.DropIndex(
                name: "IX_Tags_Name_TenantId",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_ShareLinks_AssetId1",
                table: "ShareLinks");

            migrationBuilder.DropColumn(
                name: "AssetId1",
                table: "ShareLinks");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "UserGroups",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Tags",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
