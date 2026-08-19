using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Magpie.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class media_file_path_key : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_content_mediaFile_FilePath",
                table: "content_mediaFile");

            migrationBuilder.AlterColumn<string>(
                name: "FilePath",
                table: "content_mediaFile",
                type: "nvarchar(450)",
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<string>(
                name: "PathKey",
                table: "content_mediaFile",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.Sql(
                "UPDATE [content_mediaFile] SET [PathKey] = 'legacy:' + CONVERT(nvarchar(36), [Id]);");

            migrationBuilder.CreateIndex(
                name: "IX_content_mediaFile_FilePath",
                table: "content_mediaFile",
                column: "FilePath",
                unique: true);

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

            migrationBuilder.DropIndex(
                name: "IX_content_mediaFile_FilePath",
                table: "content_mediaFile");

            migrationBuilder.AlterColumn<string>(
                name: "FilePath",
                table: "content_mediaFile",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.CreateIndex(
                name: "IX_content_mediaFile_FilePath",
                table: "content_mediaFile",
                column: "FilePath",
                unique: true);
        }
    }
}
