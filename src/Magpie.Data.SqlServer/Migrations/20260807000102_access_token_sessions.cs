using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Magpie.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class access_token_sessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessTokenJti",
                table: "auth_refreshToken",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_refreshToken_AccessTokenJti",
                table: "auth_refreshToken",
                column: "AccessTokenJti",
                unique: true,
                filter: "[AccessTokenJti] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_auth_refreshToken_AccessTokenJti",
                table: "auth_refreshToken");

            migrationBuilder.DropColumn(
                name: "AccessTokenJti",
                table: "auth_refreshToken");
        }
    }
}
