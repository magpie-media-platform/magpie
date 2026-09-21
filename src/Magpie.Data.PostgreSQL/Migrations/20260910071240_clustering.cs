using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Magpie.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class clustering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_job_execution_Status",
                table: "job_execution");

            migrationBuilder.AddColumn<bool>(
                name: "Monitoring",
                table: "wishlist_wishlist",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "JobExecutionId",
                table: "system_audit",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupKey",
                table: "content_libraryImportCandidate",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LayoutKey",
                table: "content_libraryImportCandidate",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FulfilledAt",
                table: "content_item",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParameterContentHierarchyItemId",
                table: "job_execution",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                computedColumnSql: "lower(left((\"Parameters\"::jsonb ->> 'contentHierarchyItemId'), 256))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "ParameterContentItemId",
                table: "job_execution",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                computedColumnSql: "lower(left((\"Parameters\"::jsonb ->> 'contentItemId'), 256))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "ParameterDownloadId",
                table: "job_execution",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                computedColumnSql: "lower(left((\"Parameters\"::jsonb ->> 'downloadId'), 256))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "TriggerContextContentHierarchyItemId",
                table: "job_execution",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                computedColumnSql: "lower(left((\"TriggerContext\"::jsonb ->> 'event.context.content.hierarchyItemId'), 256))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "TriggerContextContentItemId",
                table: "job_execution",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                computedColumnSql: "lower(left((\"TriggerContext\"::jsonb ->> 'event.context.content.itemId'), 256))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "TriggerContextDownloadId",
                table: "job_execution",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                computedColumnSql: "lower(left((\"TriggerContext\"::jsonb ->> 'event.context.download.id'), 256))",
                stored: true);

            migrationBuilder.CreateTable(
                name: "system_clusterEvent",
                columns: table => new
                {
                    Seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                name: "IX_content_libraryImportCandidate_GroupKey",
                table: "content_libraryImportCandidate",
                column: "GroupKey");

            migrationBuilder.CreateIndex(
                name: "IX_system_clusterEvent_PublishedAt",
                table: "system_clusterEvent",
                column: "PublishedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "IX_content_libraryImportCandidate_GroupKey",
                table: "content_libraryImportCandidate");

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

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_Status",
                table: "job_execution",
                column: "Status");
        }
    }
}
