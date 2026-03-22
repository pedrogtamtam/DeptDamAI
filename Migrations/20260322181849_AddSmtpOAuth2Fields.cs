using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeptDam.Migrations
{
    /// <inheritdoc />
    public partial class AddSmtpOAuth2Fields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthMethod",
                table: "SmtpSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OAuthClientId",
                table: "SmtpSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthClientSecret",
                table: "SmtpSettings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthTenantId",
                table: "SmtpSettings",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthMethod",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "OAuthClientId",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "OAuthClientSecret",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "OAuthTenantId",
                table: "SmtpSettings");
        }
    }
}
