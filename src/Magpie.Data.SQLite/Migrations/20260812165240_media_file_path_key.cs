using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Magpie.Data.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class media_file_path_key : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PathKey",
                table: "content_mediaFile",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE content_mediaFile SET PathKey = 'legacy:' || Id;");

            migrationBuilder.CreateIndex(
                name: "IX_content_mediaFile_PathKey",
                table: "content_mediaFile",
                column: "PathKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_content_mediaFile_PathKey",
                table: "content_mediaFile");

            migrationBuilder.DropColumn(
                name: "PathKey",
                table: "content_mediaFile");
        }
    }
}
