using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Magpie.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class clustering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_download_download_indexer_releaseSource_ReleaseSourceId",
                table: "download_download");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_Status",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_content_mediaFile_FilePath",
                table: "content_mediaFile");

            migrationBuilder.DropIndex(
                name: "IX_content_mediaFile_PathKey",
                table: "content_mediaFile");

            migrationBuilder.AddColumn<bool>(
                name: "Monitoring",
                table: "wishlist_wishlist",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "JobExecutionId",
                table: "system_audit",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "JobType",
                table: "job_execution",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "PathKey",
                table: "content_mediaFile",
                type: "nvarchar(max)",
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "FilePath",
                table: "content_mediaFile",
                type: "nvarchar(max)",
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "GroupKey",
                table: "content_libraryImportCandidate",
                type: "nvarchar(max)",
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "LayoutKey",
                table: "content_libraryImportCandidate",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FulfilledAt",
                table: "content_item",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParameterContentHierarchyItemId",
                table: "job_execution",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                computedColumnSql: "LOWER(CONVERT(nvarchar(256), JSON_VALUE([Parameters], '$.\"contentHierarchyItemId\"')))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "ParameterContentItemId",
                table: "job_execution",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                computedColumnSql: "LOWER(CONVERT(nvarchar(256), JSON_VALUE([Parameters], '$.\"contentItemId\"')))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "ParameterDownloadId",
                table: "job_execution",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                computedColumnSql: "LOWER(CONVERT(nvarchar(256), JSON_VALUE([Parameters], '$.\"downloadId\"')))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "TriggerContextContentHierarchyItemId",
                table: "job_execution",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                computedColumnSql: "LOWER(CONVERT(nvarchar(256), JSON_VALUE([TriggerContext], '$.\"event.context.content.hierarchyItemId\"')))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "TriggerContextContentItemId",
                table: "job_execution",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                computedColumnSql: "LOWER(CONVERT(nvarchar(256), JSON_VALUE([TriggerContext], '$.\"event.context.content.itemId\"')))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "TriggerContextDownloadId",
                table: "job_execution",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                computedColumnSql: "LOWER(CONVERT(nvarchar(256), JSON_VALUE([TriggerContext], '$.\"event.context.download.id\"')))",
                stored: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "FilePathHash",
                table: "content_mediaFile",
                type: "varbinary(900)",
                nullable: false,
                computedColumnSql: "CONVERT(varbinary(32), HASHBYTES('SHA2_256', [FilePath]))",
                stored: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "PathKeyHash",
                table: "content_mediaFile",
                type: "varbinary(900)",
                nullable: false,
                computedColumnSql: "CONVERT(varbinary(32), HASHBYTES('SHA2_256', [PathKey]))",
                stored: true);

            migrationBuilder.CreateTable(
                name: "system_clusterEvent",
                columns: table => new
                {
                    Seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_clusterEvent", x => x.Seq);
                });

            migrationBuilder.CreateIndex(
                name: "IX_system_audit_JobExecutionId",
                table: "system_audit",
                column: "JobExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_CreatedAt_Id",
                table: "job_execution",
                columns: new[] { "CreatedAt", "Id" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_JobType_CreatedAt_Id",
                table: "job_execution",
                columns: new[] { "JobType", "CreatedAt", "Id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_JobType_Status_CreatedAt_Id",
                table: "job_execution",
                columns: new[] { "JobType", "Status", "CreatedAt", "Id" },
                descending: new[] { false, false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_LockPartition_All",
                table: "job_execution",
                column: "LockPartition");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_ParameterContentHierarchyItemId",
                table: "job_execution",
                column: "ParameterContentHierarchyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_ParameterContentItemId",
                table: "job_execution",
                column: "ParameterContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_ParameterDownloadId",
                table: "job_execution",
                column: "ParameterDownloadId");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_Status_CreatedAt_Id",
                table: "job_execution",
                columns: new[] { "Status", "CreatedAt", "Id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_TriggerContextContentHierarchyItemId",
                table: "job_execution",
                column: "TriggerContextContentHierarchyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_TriggerContextContentItemId",
                table: "job_execution",
                column: "TriggerContextContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_TriggerContextDownloadId",
                table: "job_execution",
                column: "TriggerContextDownloadId");

            migrationBuilder.CreateIndex(
                name: "IX_content_mediaFile_FilePathHash",
                table: "content_mediaFile",
                column: "FilePathHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_mediaFile_PathKeyHash",
                table: "content_mediaFile",
                column: "PathKeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_clusterEvent_PublishedAt",
                table: "system_clusterEvent",
                column: "PublishedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_download_download_indexer_releaseSource_ReleaseSourceId",
                table: "download_download",
                column: "ReleaseSourceId",
                principalTable: "indexer_releaseSource",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_download_download_indexer_releaseSource_ReleaseSourceId",
                table: "download_download");

            migrationBuilder.DropTable(
                name: "system_clusterEvent");

            migrationBuilder.DropIndex(
                name: "IX_system_audit_JobExecutionId",
                table: "system_audit");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_CreatedAt_Id",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_JobType_CreatedAt_Id",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_JobType_Status_CreatedAt_Id",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_LockPartition_All",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_ParameterContentHierarchyItemId",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_ParameterContentItemId",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_ParameterDownloadId",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_Status_CreatedAt_Id",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_TriggerContextContentHierarchyItemId",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_TriggerContextContentItemId",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_TriggerContextDownloadId",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_content_mediaFile_FilePathHash",
                table: "content_mediaFile");

            migrationBuilder.DropIndex(
                name: "IX_content_mediaFile_PathKeyHash",
                table: "content_mediaFile");

            migrationBuilder.DropColumn(
                name: "ParameterContentHierarchyItemId",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "ParameterContentItemId",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "ParameterDownloadId",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "TriggerContextContentHierarchyItemId",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "TriggerContextContentItemId",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "TriggerContextDownloadId",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "FilePathHash",
                table: "content_mediaFile");

            migrationBuilder.DropColumn(
                name: "PathKeyHash",
                table: "content_mediaFile");

            migrationBuilder.DropColumn(
                name: "Monitoring",
                table: "wishlist_wishlist");

            migrationBuilder.DropColumn(
                name: "JobExecutionId",
                table: "system_audit");

            migrationBuilder.DropColumn(
                name: "GroupKey",
                table: "content_libraryImportCandidate");

            migrationBuilder.DropColumn(
                name: "LayoutKey",
                table: "content_libraryImportCandidate");

            migrationBuilder.DropColumn(
                name: "FulfilledAt",
                table: "content_item");

            migrationBuilder.AlterColumn<string>(
                name: "JobType",
                table: "job_execution",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "PathKey",
                table: "content_mediaFile",
                type: "nvarchar(450)",
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AlterColumn<string>(
                name: "FilePath",
                table: "content_mediaFile",
                type: "nvarchar(450)",
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_Status",
                table: "job_execution",
                column: "Status");

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

            migrationBuilder.AddForeignKey(
                name: "FK_download_download_indexer_releaseSource_ReleaseSourceId",
                table: "download_download",
                column: "ReleaseSourceId",
                principalTable: "indexer_releaseSource",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
