using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Magpie.Data.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class activty_viewer_indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_system_audit_Category",
                table: "system_audit");

            migrationBuilder.DropIndex(
                name: "IX_system_audit_Timestamp",
                table: "system_audit");

            migrationBuilder.DropIndex(
                name: "IX_system_audit_UserId",
                table: "system_audit");

            migrationBuilder.DropIndex(
                name: "IX_content_hierarchyItem_ParentId",
                table: "content_hierarchyItem");

            migrationBuilder.AddColumn<Guid>(
                name: "IndexerConnectionId",
                table: "indexer_releaseSource",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_audit_Timestamp_Id_Category_UserId",
                table: "system_audit",
                columns: new[] { "Timestamp", "Id", "Category", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_CompletedAt_Id_CreatedByUserId",
                table: "job_execution",
                columns: new[] { "CompletedAt", "Id", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_indexer_releaseSource_IndexerConnectionId",
                table: "indexer_releaseSource",
                column: "IndexerConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyItem_ParentId_EffectiveMonitoring",
                table: "content_hierarchyItem",
                columns: new[] { "ParentId", "EffectiveMonitoring" });

            migrationBuilder.AddForeignKey(
                name: "FK_indexer_releaseSource_indexer_connection_IndexerConnectionId",
                table: "indexer_releaseSource",
                column: "IndexerConnectionId",
                principalTable: "indexer_connection",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_indexer_releaseSource_indexer_connection_IndexerConnectionId",
                table: "indexer_releaseSource");

            migrationBuilder.DropIndex(
                name: "IX_system_audit_Timestamp_Id_Category_UserId",
                table: "system_audit");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_CompletedAt_Id_CreatedByUserId",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_indexer_releaseSource_IndexerConnectionId",
                table: "indexer_releaseSource");

            migrationBuilder.DropIndex(
                name: "IX_content_hierarchyItem_ParentId_EffectiveMonitoring",
                table: "content_hierarchyItem");

            migrationBuilder.DropColumn(
                name: "IndexerConnectionId",
                table: "indexer_releaseSource");

            migrationBuilder.CreateIndex(
                name: "IX_system_audit_Category",
                table: "system_audit",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_system_audit_Timestamp",
                table: "system_audit",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_system_audit_UserId",
                table: "system_audit",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyItem_ParentId",
                table: "content_hierarchyItem",
                column: "ParentId");
        }
    }
}
