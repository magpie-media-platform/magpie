using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Magpie.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class shotgun_monitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeoutAt",
                table: "job_execution");

            migrationBuilder.AddColumn<bool>(
                name: "AutoImport",
                table: "system_rootFolder",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "indexer_releaseScope",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ImportCollisionMode",
                table: "download_download",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportRecoveryState",
                table: "download_download",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DownloadId",
                table: "content_mediaFile",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FileModifiedAt",
                table: "content_mediaFile",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReplacedAt",
                table: "content_mediaFile",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ContentHierarchyItemId",
                table: "content_libraryImportCandidate",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentFileQualityRank",
                table: "content_item",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MediaReconciledAt",
                table: "content_item",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrimaryMediaFileCount",
                table: "content_item",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "PrimaryMediaSize",
                table: "content_item",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<int>(
                name: "Monitoring",
                table: "content_hierarchyItem",
                type: "int",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "EffectiveMonitoring",
                table: "content_hierarchyItem",
                type: "int",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AddColumn<string>(
                name: "PreviousTokenHash",
                table: "auth_refreshToken",
                type: "nvarchar(44)",
                maxLength: 44,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedRenewal",
                table: "auth_refreshToken",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RenewalExpiresAt",
                table: "auth_refreshToken",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenewalKeyHash",
                table: "auth_refreshToken",
                type: "nvarchar(44)",
                maxLength: 44,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_mediaFile_DownloadId",
                table: "content_mediaFile",
                column: "DownloadId");

            migrationBuilder.CreateIndex(
                name: "IX_content_libraryImportCandidate_ContentHierarchyItemId",
                table: "content_libraryImportCandidate",
                column: "ContentHierarchyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_auth_refreshToken_RenewalKeyHash",
                table: "auth_refreshToken",
                column: "RenewalKeyHash");

            migrationBuilder.AddForeignKey(
                name: "FK_content_mediaFile_download_download_DownloadId",
                table: "content_mediaFile",
                column: "DownloadId",
                principalTable: "download_download",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_content_mediaFile_download_download_DownloadId",
                table: "content_mediaFile");

            migrationBuilder.DropIndex(
                name: "IX_content_mediaFile_DownloadId",
                table: "content_mediaFile");

            migrationBuilder.DropIndex(
                name: "IX_content_libraryImportCandidate_ContentHierarchyItemId",
                table: "content_libraryImportCandidate");

            migrationBuilder.DropIndex(
                name: "IX_auth_refreshToken_RenewalKeyHash",
                table: "auth_refreshToken");

            migrationBuilder.DropColumn(
                name: "AutoImport",
                table: "system_rootFolder");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "indexer_releaseScope");

            migrationBuilder.DropColumn(
                name: "ImportCollisionMode",
                table: "download_download");

            migrationBuilder.DropColumn(
                name: "ImportRecoveryState",
                table: "download_download");

            migrationBuilder.DropColumn(
                name: "DownloadId",
                table: "content_mediaFile");

            migrationBuilder.DropColumn(
                name: "FileModifiedAt",
                table: "content_mediaFile");

            migrationBuilder.DropColumn(
                name: "ReplacedAt",
                table: "content_mediaFile");

            migrationBuilder.DropColumn(
                name: "ContentHierarchyItemId",
                table: "content_libraryImportCandidate");

            migrationBuilder.DropColumn(
                name: "CurrentFileQualityRank",
                table: "content_item");

            migrationBuilder.DropColumn(
                name: "MediaReconciledAt",
                table: "content_item");

            migrationBuilder.DropColumn(
                name: "PrimaryMediaFileCount",
                table: "content_item");

            migrationBuilder.DropColumn(
                name: "PrimaryMediaSize",
                table: "content_item");

            migrationBuilder.DropColumn(
                name: "PreviousTokenHash",
                table: "auth_refreshToken");

            migrationBuilder.DropColumn(
                name: "ProtectedRenewal",
                table: "auth_refreshToken");

            migrationBuilder.DropColumn(
                name: "RenewalExpiresAt",
                table: "auth_refreshToken");

            migrationBuilder.DropColumn(
                name: "RenewalKeyHash",
                table: "auth_refreshToken");

            migrationBuilder.AddColumn<DateTime>(
                name: "TimeoutAt",
                table: "job_execution",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "Monitoring",
                table: "content_hierarchyItem",
                type: "bit",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "EffectiveMonitoring",
                table: "content_hierarchyItem",
                type: "bit",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
