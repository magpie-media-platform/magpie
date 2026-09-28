using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Magpie.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class sdk_redesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM content_hierarchyItem WHERE Monitoring NOT IN (0, 1, 2))
                    THROW 50000, 'Cannot migrate an unknown legacy Monitoring value.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_indexer_release_content_item_ContentItemId",
                table: "indexer_release");

            migrationBuilder.DropForeignKey(
                name: "FK_wishlist_wishlist_quality_tierList_QualityTierListId",
                table: "wishlist_wishlist");

            migrationBuilder.DropIndex(
                name: "IX_system_rootFolder_ContentTypeId",
                table: "system_rootFolder");

            migrationBuilder.DropIndex(
                name: "IX_indexer_release_ContentItemId",
                table: "indexer_release");

            migrationBuilder.DropIndex(
                name: "IX_content_hierarchyItem_ContentType_Level_Slug",
                table: "content_hierarchyItem");

            migrationBuilder.DeleteData(
                table: "auth_user",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111112"));

            migrationBuilder.DropColumn(
                name: "ContentTypeId",
                table: "system_rootFolder");

            migrationBuilder.DropColumn(
                name: "Locked",
                table: "metadata_contribution");

            migrationBuilder.DropColumn(
                name: "ExecutionMode",
                table: "job_eventTrigger");

            migrationBuilder.DropColumn(
                name: "ContentItemId",
                table: "indexer_release");

            migrationBuilder.AddColumn<Guid>(
                name: "RootFolderId",
                table: "wishlist_wishlist",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "system_rootFolder",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DefaultImportCollisionMode",
                table: "system_rootFolder",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "DefaultMonitoring",
                table: "system_rootFolder",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Defaults",
                table: "system_rootFolder",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<int>(
                name: "ImportMode",
                table: "system_rootFolder",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "system_rootFolder",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RenameTemplate",
                table: "system_rootFolder",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpgradeFileHandling",
                table: "system_rootFolder",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "system_audit",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MimeTypes",
                table: "quality_tierList",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Rules",
                table: "quality_tierList",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<string>(
                name: "Rules",
                table: "quality_tier",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "'[]'");

            migrationBuilder.AddColumn<string>(
                name: "EmbedKind",
                table: "metadata_preview",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                table: "metadata_image",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<Guid>(
                name: "MetadataContributionId",
                table: "metadata_externalId",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "ReleaseId",
                table: "metadata_externalId",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SequenceNumber",
                table: "metadata_contribution",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                table: "media_server",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ContentTypes",
                table: "media_server",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "media_server",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Attempt",
                table: "job_execution",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancellationRequestedAt",
                table: "job_execution",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "job_execution",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DedupKey",
                table: "job_execution",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExecutionMode",
                table: "job_execution",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LockPartition",
                table: "job_execution",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NotBeforeAt",
                table: "job_execution",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalExecutionId",
                table: "job_execution",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentExecutionId",
                table: "job_execution",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DelaySeconds",
                table: "job_eventTrigger",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ObservedAt",
                table: "indexer_release",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "DefinitionType",
                table: "indexer_indexer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchemaData",
                table: "indexer_indexer",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupportedContentTypes",
                table: "indexer_indexer",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                table: "indexer_connection",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "indexer_connection",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LifecycleVersion",
                table: "extension_extension",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackageId",
                table: "extension_extension",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ClientRemovedAt",
                table: "download_download",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FailedAt",
                table: "download_download",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureReason",
                table: "download_download",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HeldByPolicy",
                table: "download_download",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastProgressAt",
                table: "download_download",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Progress",
                table: "download_download",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReleaseSourceId",
                table: "download_download",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CleanupDeleteFiles",
                table: "download_client",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                table: "download_client",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "KeepFailedOnClient",
                table: "download_client",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "download_client",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "download_client",
                type: "int",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AlterColumn<string>(
                name: "FilePath",
                table: "content_mediaFile",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<int>(
                name: "ActiveAcquisitionStatus",
                table: "content_item",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualityStatus",
                table: "content_item",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Monitoring",
                table: "content_hierarchyItem",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.Sql("""
                UPDATE content_hierarchyItem
                SET Monitoring = CASE Monitoring WHEN 1 THEN 1 WHEN 2 THEN 0 ELSE NULL END;
                """);

            migrationBuilder.AlterColumn<bool>(
                name: "Monitoring",
                table: "content_hierarchyItem",
                type: "bit",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlignmentKey",
                table: "content_hierarchyItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EffectiveMonitoring",
                table: "content_hierarchyItem",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "EffectiveQualityTierListId",
                table: "content_hierarchyItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EffectiveRootFolderId",
                table: "content_hierarchyItem",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "Ordinal",
                table: "content_hierarchyItem",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PendingMetadataRefresh",
                table: "content_hierarchyItem",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "content_libraryImportCandidate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RootFolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScanRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ParsedSignals = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Matches = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MatchCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_libraryImportCandidate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_libraryImportCandidate_system_rootFolder_RootFolderId",
                        column: x => x.RootFolderId,
                        principalTable: "system_rootFolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "diagnostic_finding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiagnosticCheckId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AffectedEntity = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    RepairId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_diagnostic_finding", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "download_downloadScope",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DownloadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentHierarchyItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_downloadScope", x => x.Id);
                    table.ForeignKey(
                        name: "FK_download_downloadScope_content_hierarchyItem_ContentHierarchyItemId",
                        column: x => x.ContentHierarchyItemId,
                        principalTable: "content_hierarchyItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_download_downloadScope_download_download_DownloadId",
                        column: x => x.DownloadId,
                        principalTable: "download_download",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "download_remotePathMapping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Host = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RemotePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LocalPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_remotePathMapping", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "extension_activationState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extension_activationState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "extension_nodeStatus",
                columns: table => new
                {
                    NodeId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AppliedGeneration = table.Column<long>(type: "bigint", nullable: false),
                    ExtensionHealth = table.Column<int>(type: "int", nullable: false),
                    SchedulerEligible = table.Column<bool>(type: "bit", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extension_nodeStatus", x => x.NodeId);
                });

            migrationBuilder.CreateTable(
                name: "extension_packageSource",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    SearchFilterKind = table.Column<int>(type: "int", nullable: false),
                    SearchFilterValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CredentialKind = table.Column<int>(type: "int", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EncryptedSecret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecretReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extension_packageSource", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "indexer_releaseIdentityKey",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DownloadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Authority = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_releaseIdentityKey", x => x.Id);
                    table.ForeignKey(
                        name: "FK_indexer_releaseIdentityKey_download_download_DownloadId",
                        column: x => x.DownloadId,
                        principalTable: "download_download",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_indexer_releaseIdentityKey_indexer_release_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "indexer_release",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_releaseScope",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentHierarchyItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_releaseScope", x => x.Id);
                    table.ForeignKey(
                        name: "FK_indexer_releaseScope_content_hierarchyItem_ContentHierarchyItemId",
                        column: x => x.ContentHierarchyItemId,
                        principalTable: "content_hierarchyItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_indexer_releaseScope_indexer_release_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "indexer_release",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_releaseSource",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IndexerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Protocol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: true),
                    InfoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Categories = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Languages = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_releaseSource", x => x.Id);
                    table.ForeignKey(
                        name: "FK_indexer_releaseSource_indexer_indexer_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "indexer_indexer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_indexer_releaseSource_indexer_release_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "indexer_release",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_enqueue_coalescing_lock",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_enqueue_coalescing_lock", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "notification_rule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventFilter = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProviderType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Settings = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_rule", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "system_blockedFileType",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Pattern = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_blockedFileType", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "system_mimeMapping",
                columns: table => new
                {
                    Extension = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_mimeMapping", x => x.Extension);
                });

            migrationBuilder.CreateTable(
                name: "wishlist_discovered",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WishlistId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalIdSource = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    DiscoveredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wishlist_discovered", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wishlist_discovered_wishlist_wishlist_WishlistId",
                        column: x => x.WishlistId,
                        principalTable: "wishlist_wishlist",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "extension_activationState",
                columns: new[] { "Id", "Generation" },
                values: new object[] { 1, 0L });

            migrationBuilder.InsertData(
                table: "extension_packageSource",
                columns: new[] { "Id", "CreatedAt", "CreatedByUserId", "CredentialKind", "Enabled", "EncryptedSecret", "ModifiedAt", "ModifiedByUserId", "Name", "SearchFilterKind", "SearchFilterValue", "SecretReference", "SourceUrl", "Username" },
                values: new object[] { new Guid("5d9a2b5f-5c59-4a1f-8c70-8011a8b4e0b1"), new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 0, true, null, null, null, "nuget.org", 0, "magpie", null, "https://api.nuget.org/v3/index.json", null });

            migrationBuilder.CreateIndex(
                name: "IX_wishlist_wishlist_RootFolderId",
                table: "wishlist_wishlist",
                column: "RootFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_system_rootFolder_ContentType",
                table: "system_rootFolder",
                column: "ContentType");

            migrationBuilder.CreateIndex(
                name: "IX_system_rootFolder_ContentType_IsDefault",
                table: "system_rootFolder",
                columns: new[] { "ContentType", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_system_audit_CorrelationId",
                table: "system_audit",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_externalId_ReleaseId",
                table: "metadata_externalId",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_CorrelationId",
                table: "job_execution",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_LockPartition",
                table: "job_execution",
                column: "LockPartition",
                filter: "\"Status\" IN (1, 2, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_NotBeforeAt",
                table: "job_execution",
                column: "NotBeforeAt");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_ParentExecutionId_CreatedAt",
                table: "job_execution",
                columns: new[] { "ParentExecutionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_job_execution_DedupKey",
                table: "job_execution",
                column: "DedupKey",
                unique: true,
                filter: "\"DedupKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_job_execution_LockPartition_Running",
                table: "job_execution",
                column: "LockPartition",
                unique: true,
                filter: "\"Status\" IN (2, 6) AND \"LockPartition\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_release_ObservedAt",
                table: "indexer_release",
                column: "ObservedAt");

            migrationBuilder.CreateIndex(
                name: "IX_extension_extension_PackageId",
                table: "extension_extension",
                column: "PackageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_download_download_ReleaseSourceId",
                table: "download_download",
                column: "ReleaseSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_content_mediaFile_FilePath",
                table: "content_mediaFile",
                column: "FilePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_item_ActiveAcquisitionStatus",
                table: "content_item",
                column: "ActiveAcquisitionStatus");

            migrationBuilder.CreateIndex(
                name: "IX_content_item_QualityStatus",
                table: "content_item",
                column: "QualityStatus");

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyItem_ContentType_Level_Slug",
                table: "content_hierarchyItem",
                columns: new[] { "ContentType", "Level", "Slug" },
                unique: true,
                filter: "\"ParentId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyItem_ContentType_ParentId_Level_Slug",
                table: "content_hierarchyItem",
                columns: new[] { "ContentType", "ParentId", "Level", "Slug" },
                unique: true,
                filter: "[ParentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyItem_EffectiveMonitoring",
                table: "content_hierarchyItem",
                column: "EffectiveMonitoring");

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyItem_EffectiveQualityTierListId",
                table: "content_hierarchyItem",
                column: "EffectiveQualityTierListId");

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyItem_EffectiveRootFolderId",
                table: "content_hierarchyItem",
                column: "EffectiveRootFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_content_libraryImportCandidate_RootFolderId_ScanRunId",
                table: "content_libraryImportCandidate",
                columns: new[] { "RootFolderId", "ScanRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_content_libraryImportCandidate_Status_MatchCount",
                table: "content_libraryImportCandidate",
                columns: new[] { "Status", "MatchCount" });

            migrationBuilder.CreateIndex(
                name: "IX_diagnostic_finding_DiagnosticCheckId",
                table: "diagnostic_finding",
                column: "DiagnosticCheckId");

            migrationBuilder.CreateIndex(
                name: "IX_diagnostic_finding_Severity",
                table: "diagnostic_finding",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_download_downloadScope_ContentHierarchyItemId",
                table: "download_downloadScope",
                column: "ContentHierarchyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_download_downloadScope_DownloadId_ContentHierarchyItemId",
                table: "download_downloadScope",
                columns: new[] { "DownloadId", "ContentHierarchyItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_download_remotePathMapping_Host",
                table: "download_remotePathMapping",
                column: "Host");

            migrationBuilder.CreateIndex(
                name: "IX_extension_nodeStatus_LastSeenAt",
                table: "extension_nodeStatus",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_extension_nodeStatus_SchedulerEligible",
                table: "extension_nodeStatus",
                column: "SchedulerEligible");

            migrationBuilder.CreateIndex(
                name: "IX_extension_packageSource_Enabled",
                table: "extension_packageSource",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_extension_packageSource_Name",
                table: "extension_packageSource",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_indexer_releaseIdentityKey_Authority_Value",
                table: "indexer_releaseIdentityKey",
                columns: new[] { "Authority", "Value" },
                unique: true,
                filter: "[ReleaseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_releaseIdentityKey_DownloadId",
                table: "indexer_releaseIdentityKey",
                column: "DownloadId");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_releaseIdentityKey_DownloadId_Authority_Value",
                table: "indexer_releaseIdentityKey",
                columns: new[] { "DownloadId", "Authority", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_indexer_releaseIdentityKey_ReleaseId",
                table: "indexer_releaseIdentityKey",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_releaseIdentityKey_ReleaseId_Authority_Value",
                table: "indexer_releaseIdentityKey",
                columns: new[] { "ReleaseId", "Authority", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_indexer_releaseScope_ContentHierarchyItemId",
                table: "indexer_releaseScope",
                column: "ContentHierarchyItemId");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_releaseScope_ReleaseId_ContentHierarchyItemId",
                table: "indexer_releaseScope",
                columns: new[] { "ReleaseId", "ContentHierarchyItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_indexer_releaseSource_IndexerId",
                table: "indexer_releaseSource",
                column: "IndexerId");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_releaseSource_ReleaseId",
                table: "indexer_releaseSource",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_releaseSource_ReleaseId_Key",
                table: "indexer_releaseSource",
                columns: new[] { "ReleaseId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_rule_Enabled",
                table: "notification_rule",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_notification_rule_OwnerUserId",
                table: "notification_rule",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_notification_rule_ProviderType",
                table: "notification_rule",
                column: "ProviderType");

            migrationBuilder.CreateIndex(
                name: "IX_system_blockedFileType_Pattern",
                table: "system_blockedFileType",
                column: "Pattern",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wishlist_discovered_WishlistId",
                table: "wishlist_discovered",
                column: "WishlistId");

            migrationBuilder.CreateIndex(
                name: "IX_wishlist_discovered_WishlistId_ExternalIdSource_ExternalId",
                table: "wishlist_discovered",
                columns: new[] { "WishlistId", "ExternalIdSource", "ExternalId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_download_download_indexer_releaseSource_ReleaseSourceId",
                table: "download_download",
                column: "ReleaseSourceId",
                principalTable: "indexer_releaseSource",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);

            migrationBuilder.AddForeignKey(
                name: "FK_metadata_externalId_indexer_release_ReleaseId",
                table: "metadata_externalId",
                column: "ReleaseId",
                principalTable: "indexer_release",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_wishlist_wishlist_quality_tierList_QualityTierListId",
                table: "wishlist_wishlist",
                column: "QualityTierListId",
                principalTable: "quality_tierList",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_wishlist_wishlist_system_rootFolder_RootFolderId",
                table: "wishlist_wishlist",
                column: "RootFolderId",
                principalTable: "system_rootFolder",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_download_download_indexer_releaseSource_ReleaseSourceId",
                table: "download_download");

            migrationBuilder.DropForeignKey(
                name: "FK_metadata_externalId_indexer_release_ReleaseId",
                table: "metadata_externalId");

            migrationBuilder.DropForeignKey(
                name: "FK_wishlist_wishlist_quality_tierList_QualityTierListId",
                table: "wishlist_wishlist");

            migrationBuilder.DropForeignKey(
                name: "FK_wishlist_wishlist_system_rootFolder_RootFolderId",
                table: "wishlist_wishlist");

            migrationBuilder.DropTable(
                name: "content_libraryImportCandidate");

            migrationBuilder.DropTable(
                name: "diagnostic_finding");

            migrationBuilder.DropTable(
                name: "download_downloadScope");

            migrationBuilder.DropTable(
                name: "download_remotePathMapping");

            migrationBuilder.DropTable(
                name: "extension_activationState");

            migrationBuilder.DropTable(
                name: "extension_nodeStatus");

            migrationBuilder.DropTable(
                name: "extension_packageSource");

            migrationBuilder.DropTable(
                name: "indexer_releaseIdentityKey");

            migrationBuilder.DropTable(
                name: "indexer_releaseScope");

            migrationBuilder.DropTable(
                name: "indexer_releaseSource");

            migrationBuilder.DropTable(
                name: "job_enqueue_coalescing_lock");

            migrationBuilder.DropTable(
                name: "notification_rule");

            migrationBuilder.DropTable(
                name: "system_blockedFileType");

            migrationBuilder.DropTable(
                name: "system_mimeMapping");

            migrationBuilder.DropTable(
                name: "wishlist_discovered");

            migrationBuilder.DropIndex(
                name: "IX_wishlist_wishlist_RootFolderId",
                table: "wishlist_wishlist");

            migrationBuilder.DropIndex(
                name: "IX_system_rootFolder_ContentType",
                table: "system_rootFolder");

            migrationBuilder.DropIndex(
                name: "IX_system_rootFolder_ContentType_IsDefault",
                table: "system_rootFolder");

            migrationBuilder.DropIndex(
                name: "IX_system_audit_CorrelationId",
                table: "system_audit");

            migrationBuilder.DropIndex(
                name: "IX_metadata_externalId_ReleaseId",
                table: "metadata_externalId");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_CorrelationId",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_LockPartition",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_NotBeforeAt",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_job_execution_ParentExecutionId_CreatedAt",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "UX_job_execution_DedupKey",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "UX_job_execution_LockPartition_Running",
                table: "job_execution");

            migrationBuilder.DropIndex(
                name: "IX_indexer_release_ObservedAt",
                table: "indexer_release");

            migrationBuilder.DropIndex(
                name: "IX_extension_extension_PackageId",
                table: "extension_extension");

            migrationBuilder.DropIndex(
                name: "IX_download_download_ReleaseSourceId",
                table: "download_download");

            migrationBuilder.DropIndex(
                name: "IX_content_mediaFile_FilePath",
                table: "content_mediaFile");

            migrationBuilder.DropIndex(
                name: "IX_content_item_ActiveAcquisitionStatus",
                table: "content_item");

            migrationBuilder.DropIndex(
                name: "IX_content_item_QualityStatus",
                table: "content_item");

            migrationBuilder.DropIndex(
                name: "IX_content_hierarchyItem_ContentType_Level_Slug",
                table: "content_hierarchyItem");

            migrationBuilder.DropIndex(
                name: "IX_content_hierarchyItem_ContentType_ParentId_Level_Slug",
                table: "content_hierarchyItem");

            migrationBuilder.DropIndex(
                name: "IX_content_hierarchyItem_EffectiveMonitoring",
                table: "content_hierarchyItem");

            migrationBuilder.DropIndex(
                name: "IX_content_hierarchyItem_EffectiveQualityTierListId",
                table: "content_hierarchyItem");

            migrationBuilder.DropIndex(
                name: "IX_content_hierarchyItem_EffectiveRootFolderId",
                table: "content_hierarchyItem");

            migrationBuilder.DropColumn(
                name: "RootFolderId",
                table: "wishlist_wishlist");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "system_rootFolder");

            migrationBuilder.DropColumn(
                name: "DefaultImportCollisionMode",
                table: "system_rootFolder");

            migrationBuilder.DropColumn(
                name: "DefaultMonitoring",
                table: "system_rootFolder");

            migrationBuilder.DropColumn(
                name: "Defaults",
                table: "system_rootFolder");

            migrationBuilder.DropColumn(
                name: "ImportMode",
                table: "system_rootFolder");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "system_rootFolder");

            migrationBuilder.DropColumn(
                name: "RenameTemplate",
                table: "system_rootFolder");

            migrationBuilder.DropColumn(
                name: "UpgradeFileHandling",
                table: "system_rootFolder");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "system_audit");

            migrationBuilder.DropColumn(
                name: "MimeTypes",
                table: "quality_tierList");

            migrationBuilder.DropColumn(
                name: "Rules",
                table: "quality_tierList");

            migrationBuilder.DropColumn(
                name: "Rules",
                table: "quality_tier");

            migrationBuilder.DropColumn(
                name: "EmbedKind",
                table: "metadata_preview");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                table: "metadata_image");

            migrationBuilder.DropColumn(
                name: "ReleaseId",
                table: "metadata_externalId");

            migrationBuilder.DropColumn(
                name: "SequenceNumber",
                table: "metadata_contribution");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                table: "media_server");

            migrationBuilder.DropColumn(
                name: "ContentTypes",
                table: "media_server");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "media_server");

            migrationBuilder.DropColumn(
                name: "Attempt",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "CancellationRequestedAt",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "DedupKey",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "ExecutionMode",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "LockPartition",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "NotBeforeAt",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "OriginalExecutionId",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "ParentExecutionId",
                table: "job_execution");

            migrationBuilder.DropColumn(
                name: "DelaySeconds",
                table: "job_eventTrigger");

            migrationBuilder.DropColumn(
                name: "ObservedAt",
                table: "indexer_release");

            migrationBuilder.DropColumn(
                name: "DefinitionType",
                table: "indexer_indexer");

            migrationBuilder.DropColumn(
                name: "SchemaData",
                table: "indexer_indexer");

            migrationBuilder.DropColumn(
                name: "SupportedContentTypes",
                table: "indexer_indexer");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                table: "indexer_connection");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "indexer_connection");

            migrationBuilder.DropColumn(
                name: "LifecycleVersion",
                table: "extension_extension");

            migrationBuilder.DropColumn(
                name: "PackageId",
                table: "extension_extension");

            migrationBuilder.DropColumn(
                name: "ClientRemovedAt",
                table: "download_download");

            migrationBuilder.DropColumn(
                name: "FailedAt",
                table: "download_download");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "download_download");

            migrationBuilder.DropColumn(
                name: "HeldByPolicy",
                table: "download_download");

            migrationBuilder.DropColumn(
                name: "LastProgressAt",
                table: "download_download");

            migrationBuilder.DropColumn(
                name: "Progress",
                table: "download_download");

            migrationBuilder.DropColumn(
                name: "ReleaseSourceId",
                table: "download_download");

            migrationBuilder.DropColumn(
                name: "CleanupDeleteFiles",
                table: "download_client");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                table: "download_client");

            migrationBuilder.DropColumn(
                name: "KeepFailedOnClient",
                table: "download_client");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "download_client");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "download_client");

            migrationBuilder.DropColumn(
                name: "ActiveAcquisitionStatus",
                table: "content_item");

            migrationBuilder.DropColumn(
                name: "QualityStatus",
                table: "content_item");

            migrationBuilder.DropColumn(
                name: "AlignmentKey",
                table: "content_hierarchyItem");

            migrationBuilder.DropColumn(
                name: "EffectiveMonitoring",
                table: "content_hierarchyItem");

            migrationBuilder.DropColumn(
                name: "EffectiveQualityTierListId",
                table: "content_hierarchyItem");

            migrationBuilder.DropColumn(
                name: "EffectiveRootFolderId",
                table: "content_hierarchyItem");

            migrationBuilder.DropColumn(
                name: "Ordinal",
                table: "content_hierarchyItem");

            migrationBuilder.DropColumn(
                name: "PendingMetadataRefresh",
                table: "content_hierarchyItem");

            migrationBuilder.AddColumn<Guid>(
                name: "ContentTypeId",
                table: "system_rootFolder",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<Guid>(
                name: "MetadataContributionId",
                table: "metadata_externalId",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Locked",
                table: "metadata_contribution",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ExecutionMode",
                table: "job_eventTrigger",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ContentItemId",
                table: "indexer_release",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FilePath",
                table: "content_mediaFile",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<int>(
                name: "Monitoring",
                table: "content_hierarchyItem",
                type: "int",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldNullable: true);

            migrationBuilder.Sql("""
                UPDATE content_hierarchyItem
                SET Monitoring = CASE Monitoring WHEN 1 THEN 1 WHEN 0 THEN 2 ELSE 0 END;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "Monitoring",
                table: "content_hierarchyItem",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.InsertData(
                table: "auth_user",
                columns: new[] { "Id", "CreatedAt", "CreatedByUserId", "Email", "Enabled", "ExtensionId", "ExternalId", "FailedLoginAttempts", "LastLoginAt", "LockedUntil", "ModifiedAt", "ModifiedByUserId", "PasswordHash", "PasswordSalt", "ProviderType", "Role", "Settings", "Username" },
                values: new object[] { new Guid("11111111-1111-1111-1111-111111111112"), new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, false, null, null, 0, null, null, null, null, null, null, "local", 3, null, "root" });

            migrationBuilder.CreateIndex(
                name: "IX_system_rootFolder_ContentTypeId",
                table: "system_rootFolder",
                column: "ContentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_release_ContentItemId",
                table: "indexer_release",
                column: "ContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyItem_ContentType_Level_Slug",
                table: "content_hierarchyItem",
                columns: new[] { "ContentType", "Level", "Slug" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_indexer_release_content_item_ContentItemId",
                table: "indexer_release",
                column: "ContentItemId",
                principalTable: "content_item",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_wishlist_wishlist_quality_tierList_QualityTierListId",
                table: "wishlist_wishlist",
                column: "QualityTierListId",
                principalTable: "quality_tierList",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
