using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Magpie.Data.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_user",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    ExtensionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderType = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: true),
                    Settings = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    PasswordSalt = table.Column<string>(type: "text", nullable: true),
                    FailedLoginAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_user", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "download_client",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtensionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderType = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastChecked = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Settings = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_client", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "extension_extension",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Author = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    TargetABI = table.Column<string>(type: "text", nullable: true),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    AssemblyName = table.Column<string>(type: "text", nullable: false),
                    Installed = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DisabledReason = table.Column<string>(type: "text", nullable: true),
                    LoadOrder = table.Column<int>(type: "integer", nullable: false),
                    Managed = table.Column<bool>(type: "boolean", nullable: false),
                    NuGetId = table.Column<string>(type: "text", nullable: true),
                    PackageRepository = table.Column<string>(type: "text", nullable: true),
                    AutoUpgrade = table.Column<bool>(type: "boolean", nullable: false),
                    LastChecked = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extension_extension", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "filter_content",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Criteria = table.Column<string>(type: "text", nullable: false),
                    OwnerLock = table.Column<bool>(type: "boolean", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_filter_content", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "filter_subject",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Criteria = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_filter_subject", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "indexer_feed",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtensionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderType = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Settings = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastSync = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_feed", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "job_eventTrigger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtensionId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobType = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    ExecutionMode = table.Column<int>(type: "integer", nullable: false),
                    EventFilter = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Parameters = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_eventTrigger", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "job_scheduleTrigger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtensionId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobType = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Schedule = table.Column<string>(type: "text", nullable: false),
                    ExecutionMode = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Parameters = table.Column<string>(type: "text", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_scheduleTrigger", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "media_server",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ExtensionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderType = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastChecked = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Settings = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_server", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "metadata_container",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateLastRefreshed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_container", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quality_tier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Criteria = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_tier", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quality_tierList",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    UpgradeAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_tierList", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "system_audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: true),
                    ParentEntityId = table.Column<string>(type: "text", nullable: true),
                    EntityType = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Operation = table.Column<string>(type: "text", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "system_rootFolder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    ContentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_rootFolder", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "system_tag",
                columns: table => new
                {
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_tag", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "auth_apiKey",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    KeyHash = table.Column<string>(type: "text", nullable: true),
                    KeyPrefix = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_apiKey", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_apiKey_auth_user_UserId",
                        column: x => x.UserId,
                        principalTable: "auth_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "auth_refreshToken",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_refreshToken", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_refreshToken_auth_user_UserId",
                        column: x => x.UserId,
                        principalTable: "auth_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "configuration_settingsGroup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ExtensionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_settingsGroup", x => x.Id);
                    table.ForeignKey(
                        name: "FK_configuration_settingsGroup_extension_extension_ExtensionId",
                        column: x => x.ExtensionId,
                        principalTable: "extension_extension",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "indexer_indexer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtensionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderType = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Availability = table.Column<int>(type: "integer", nullable: false),
                    Categories = table.Column<List<string>>(type: "text[]", nullable: false),
                    Languages = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_indexer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_indexer_indexer_indexer_feed_FeedId",
                        column: x => x.FeedId,
                        principalTable: "indexer_feed",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "job_execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleTriggerId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventTriggerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtensionId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobType = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    TriggerContext = table.Column<string>(type: "text", nullable: false),
                    Parameters = table.Column<string>(type: "text", nullable: false),
                    Progress = table.Column<double>(type: "double precision", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StatusMessage = table.Column<string>(type: "text", nullable: true),
                    NodeId = table.Column<string>(type: "text", nullable: true),
                    LastHeartbeat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TimeoutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_execution", x => x.Id);
                    table.ForeignKey(
                        name: "FK_job_execution_job_eventTrigger_EventTriggerId",
                        column: x => x.EventTriggerId,
                        principalTable: "job_eventTrigger",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_job_execution_job_scheduleTrigger_ScheduleTriggerId",
                        column: x => x.ScheduleTriggerId,
                        principalTable: "job_scheduleTrigger",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "content_subject",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataContainerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    DateLastRefreshed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_subject", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_subject_metadata_container_MetadataContainerId",
                        column: x => x.MetadataContainerId,
                        principalTable: "metadata_container",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metadata_contribution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataContainerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Locked = table.Column<bool>(type: "boolean", nullable: false),
                    ExtensionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderType = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    OriginalTitle = table.Column<string>(type: "text", nullable: true),
                    SortTitle = table.Column<string>(type: "text", nullable: true),
                    Language = table.Column<string>(type: "text", nullable: true),
                    Overview = table.Column<string>(type: "text", nullable: true),
                    PublishDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    ProductionStatus = table.Column<string>(type: "text", nullable: true),
                    Established = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Dissolved = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BirthDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeathDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DateLastRefreshed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_contribution", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_contribution_metadata_container_MetadataContainerId",
                        column: x => x.MetadataContainerId,
                        principalTable: "metadata_container",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "quality_tierListRank",
                columns: table => new
                {
                    QualityTierListId = table.Column<Guid>(type: "uuid", nullable: false),
                    QualityTierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    IsCutoff = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_tierListRank", x => new { x.QualityTierListId, x.QualityTierId });
                    table.ForeignKey(
                        name: "FK_quality_tierListRank_quality_tierList_QualityTierListId",
                        column: x => x.QualityTierListId,
                        principalTable: "quality_tierList",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_quality_tierListRank_quality_tier_QualityTierId",
                        column: x => x.QualityTierId,
                        principalTable: "quality_tier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wishlist_wishlist",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentFilterId = table.Column<Guid>(type: "uuid", nullable: false),
                    QualityTierListId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    OwnerLock = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wishlist_wishlist", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wishlist_wishlist_filter_content_ContentFilterId",
                        column: x => x.ContentFilterId,
                        principalTable: "filter_content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wishlist_wishlist_quality_tierList_QualityTierListId",
                        column: x => x.QualityTierListId,
                        principalTable: "quality_tierList",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "content_hierarchyItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataContainerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    QualityTierListId = table.Column<Guid>(type: "uuid", nullable: true),
                    RootFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    Level = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Monitoring = table.Column<int>(type: "integer", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: true),
                    LanguagePreferences = table.Column<List<string>>(type: "text[]", nullable: false),
                    Settings = table.Column<string>(type: "text", nullable: false),
                    DateLastRefreshed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_hierarchyItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_hierarchyItem_content_hierarchyItem_ParentId",
                        column: x => x.ParentId,
                        principalTable: "content_hierarchyItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_content_hierarchyItem_metadata_container_MetadataContainerId",
                        column: x => x.MetadataContainerId,
                        principalTable: "metadata_container",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_content_hierarchyItem_quality_tierList_QualityTierListId",
                        column: x => x.QualityTierListId,
                        principalTable: "quality_tierList",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_content_hierarchyItem_system_rootFolder_RootFolderId",
                        column: x => x.RootFolderId,
                        principalTable: "system_rootFolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "download_clientTag",
                columns: table => new
                {
                    DownloadClientsId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_clientTag", x => new { x.DownloadClientsId, x.TagName });
                    table.ForeignKey(
                        name: "FK_download_clientTag_download_client_DownloadClientsId",
                        column: x => x.DownloadClientsId,
                        principalTable: "download_client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_download_clientTag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "filter_contentTag",
                columns: table => new
                {
                    ContentFiltersId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_filter_contentTag", x => new { x.ContentFiltersId, x.TagName });
                    table.ForeignKey(
                        name: "FK_filter_contentTag_filter_content_ContentFiltersId",
                        column: x => x.ContentFiltersId,
                        principalTable: "filter_content",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_filter_contentTag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "filter_subjectTag",
                columns: table => new
                {
                    SubjectFiltersId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_filter_subjectTag", x => new { x.SubjectFiltersId, x.TagName });
                    table.ForeignKey(
                        name: "FK_filter_subjectTag_filter_subject_SubjectFiltersId",
                        column: x => x.SubjectFiltersId,
                        principalTable: "filter_subject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_filter_subjectTag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "media_serverTag",
                columns: table => new
                {
                    MediaServersId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_serverTag", x => new { x.MediaServersId, x.TagName });
                    table.ForeignKey(
                        name: "FK_media_serverTag_media_server_MediaServersId",
                        column: x => x.MediaServersId,
                        principalTable: "media_server",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_media_serverTag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metadata_containerTag",
                columns: table => new
                {
                    MetadataContainersId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_containerTag", x => new { x.MetadataContainersId, x.TagName });
                    table.ForeignKey(
                        name: "FK_metadata_containerTag_metadata_container_MetadataContainers~",
                        column: x => x.MetadataContainersId,
                        principalTable: "metadata_container",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_metadata_containerTag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quality_tierListTag",
                columns: table => new
                {
                    QualityTierListsId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_tierListTag", x => new { x.QualityTierListsId, x.TagName });
                    table.ForeignKey(
                        name: "FK_quality_tierListTag_quality_tierList_QualityTierListsId",
                        column: x => x.QualityTierListsId,
                        principalTable: "quality_tierList",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_quality_tierListTag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "configuration_setting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingsGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_setting", x => x.Id);
                    table.ForeignKey(
                        name: "FK_configuration_setting_configuration_settingsGroup_SettingsG~",
                        column: x => x.SettingsGroupId,
                        principalTable: "configuration_settingsGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "indexer_connection",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IndexerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Settings = table.Column<string>(type: "text", nullable: false),
                    LastChecked = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_connection", x => x.Id);
                    table.ForeignKey(
                        name: "FK_indexer_connection_indexer_indexer_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "indexer_indexer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metadata_alternateText",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: true),
                    Text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_alternateText", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_alternateText_metadata_contribution_MetadataContri~",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metadata_award",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    Language = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_award", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_award_metadata_contribution_MetadataContributionId",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metadata_contentRating",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Rating = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_contentRating", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_contentRating_metadata_contribution_MetadataContri~",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metadata_customValue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_customValue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_customValue_metadata_contribution_MetadataContribu~",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metadata_externalId",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_externalId", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_externalId_metadata_contribution_MetadataContribut~",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metadata_genre",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_genre", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_genre_metadata_contribution_MetadataContributionId",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metadata_image",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    MimeType = table.Column<string>(type: "text", nullable: true),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: true),
                    Language = table.Column<string>(type: "text", nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_image", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_image_metadata_contribution_MetadataContributionId",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metadata_preview",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    MimeType = table.Column<string>(type: "text", nullable: true),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Language = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_preview", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_preview_metadata_contribution_MetadataContribution~",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metadata_reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    Language = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_reference", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_reference_metadata_contribution_MetadataContributi~",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metadata_relationship",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelatedMetadataContainerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Qualifier = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_relationship", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_relationship_metadata_container_RelatedMetadataCon~",
                        column: x => x.RelatedMetadataContainerId,
                        principalTable: "metadata_container",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_metadata_relationship_metadata_contribution_MetadataContrib~",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wishlist_tag",
                columns: table => new
                {
                    TagName = table.Column<string>(type: "text", nullable: false),
                    WishlistsId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wishlist_tag", x => new { x.TagName, x.WishlistsId });
                    table.ForeignKey(
                        name: "FK_wishlist_tag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wishlist_tag_wishlist_wishlist_WishlistsId",
                        column: x => x.WishlistsId,
                        principalTable: "wishlist_wishlist",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentHierarchyItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fulfillment = table.Column<int>(type: "integer", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedViaWishlistId = table.Column<Guid>(type: "uuid", nullable: true),
                    DateLastSearched = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_item_content_hierarchyItem_ContentHierarchyItemId",
                        column: x => x.ContentHierarchyItemId,
                        principalTable: "content_hierarchyItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "indexer_connectionTag",
                columns: table => new
                {
                    IndexerConnectionsId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_connectionTag", x => new { x.IndexerConnectionsId, x.TagName });
                    table.ForeignKey(
                        name: "FK_indexer_connectionTag_indexer_connection_IndexerConnections~",
                        column: x => x.IndexerConnectionsId,
                        principalTable: "indexer_connection",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_indexer_connectionTag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_mediaFile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileKind = table.Column<string>(type: "text", nullable: false),
                    MimeType = table.Column<string>(type: "text", nullable: true),
                    FilePath = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    MediaInfo = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_mediaFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_mediaFile_content_item_ContentItemId",
                        column: x => x.ContentItemId,
                        principalTable: "content_item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "indexer_release",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IndexerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Protocol = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    PublishDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: true),
                    InfoUrl = table.Column<string>(type: "text", nullable: true),
                    Categories = table.Column<List<string>>(type: "text[]", nullable: false),
                    Languages = table.Column<List<string>>(type: "text[]", nullable: false),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_release", x => x.Id);
                    table.ForeignKey(
                        name: "FK_indexer_release_content_item_ContentItemId",
                        column: x => x.ContentItemId,
                        principalTable: "content_item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_indexer_release_indexer_indexer_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "indexer_indexer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "download_download",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    DownloadClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OutputPath = table.Column<string>(type: "text", nullable: true),
                    StatusReason = table.Column<string>(type: "text", nullable: true),
                    DateCompleted = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_download", x => x.Id);
                    table.ForeignKey(
                        name: "FK_download_download_download_client_DownloadClientId",
                        column: x => x.DownloadClientId,
                        principalTable: "download_client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_download_download_indexer_release_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "indexer_release",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "indexer_releaseTag",
                columns: table => new
                {
                    ReleasesId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_releaseTag", x => new { x.ReleasesId, x.TagName });
                    table.ForeignKey(
                        name: "FK_indexer_releaseTag_indexer_release_ReleasesId",
                        column: x => x.ReleasesId,
                        principalTable: "indexer_release",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_indexer_releaseTag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "auth_user",
                columns: new[] { "Id", "CreatedAt", "CreatedByUserId", "Email", "Enabled", "ExtensionId", "ExternalId", "FailedLoginAttempts", "LastLoginAt", "LockedUntil", "ModifiedAt", "ModifiedByUserId", "PasswordHash", "PasswordSalt", "ProviderType", "Role", "Settings", "Username" },
                values: new object[] { new Guid("11111111-1111-1111-1111-111111111112"), new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, false, null, null, 0, null, null, null, null, null, null, "local", 3, null, "root" });

            migrationBuilder.CreateIndex(
                name: "IX_auth_apiKey_Enabled",
                table: "auth_apiKey",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_auth_apiKey_KeyPrefix",
                table: "auth_apiKey",
                column: "KeyPrefix");

            migrationBuilder.CreateIndex(
                name: "IX_auth_apiKey_UserId",
                table: "auth_apiKey",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_auth_refreshToken_ExpiresAt",
                table: "auth_refreshToken",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_auth_refreshToken_UserId",
                table: "auth_refreshToken",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_auth_user_Email",
                table: "auth_user",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_auth_user_Username",
                table: "auth_user",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_configuration_setting_SettingsGroupId_Key",
                table: "configuration_setting",
                columns: new[] { "SettingsGroupId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_configuration_settingsGroup_ExtensionId_Name",
                table: "configuration_settingsGroup",
                columns: new[] { "ExtensionId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyItem_ContentType_Level",
                table: "content_hierarchyItem",
                columns: new[] { "ContentType", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyItem_ContentType_Level_Slug",
                table: "content_hierarchyItem",
                columns: new[] { "ContentType", "Level", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyItem_MetadataContainerId",
                table: "content_hierarchyItem",
                column: "MetadataContainerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyItem_ParentId",
                table: "content_hierarchyItem",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyItem_QualityTierListId",
                table: "content_hierarchyItem",
                column: "QualityTierListId");

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyItem_RootFolderId",
                table: "content_hierarchyItem",
                column: "RootFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_content_item_ContentHierarchyItemId",
                table: "content_item",
                column: "ContentHierarchyItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_item_Fulfillment",
                table: "content_item",
                column: "Fulfillment");

            migrationBuilder.CreateIndex(
                name: "IX_content_mediaFile_ContentItemId",
                table: "content_mediaFile",
                column: "ContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_content_subject_Kind",
                table: "content_subject",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_content_subject_MetadataContainerId",
                table: "content_subject",
                column: "MetadataContainerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_download_clientTag_TagName",
                table: "download_clientTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_download_download_DownloadClientId",
                table: "download_download",
                column: "DownloadClientId");

            migrationBuilder.CreateIndex(
                name: "IX_download_download_ReleaseId",
                table: "download_download",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_download_download_Status",
                table: "download_download",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_extension_extension_AssemblyName",
                table: "extension_extension",
                column: "AssemblyName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_filter_contentTag_TagName",
                table: "filter_contentTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_filter_subjectTag_TagName",
                table: "filter_subjectTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_connection_IndexerId",
                table: "indexer_connection",
                column: "IndexerId");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_connectionTag_TagName",
                table: "indexer_connectionTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_indexer_FeedId",
                table: "indexer_indexer",
                column: "FeedId");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_indexer_ProviderType",
                table: "indexer_indexer",
                column: "ProviderType");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_release_ContentItemId",
                table: "indexer_release",
                column: "ContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_release_IndexerId",
                table: "indexer_release",
                column: "IndexerId");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_release_PublishDate",
                table: "indexer_release",
                column: "PublishDate");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_releaseTag_TagName",
                table: "indexer_releaseTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_job_eventTrigger_Enabled",
                table: "job_eventTrigger",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_EventTriggerId",
                table: "job_execution",
                column: "EventTriggerId");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_LastHeartbeat",
                table: "job_execution",
                column: "LastHeartbeat");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_ScheduleTriggerId",
                table: "job_execution",
                column: "ScheduleTriggerId");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_StartedAt",
                table: "job_execution",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_Status",
                table: "job_execution",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_job_scheduleTrigger_Enabled",
                table: "job_scheduleTrigger",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_job_scheduleTrigger_NextRunAt",
                table: "job_scheduleTrigger",
                column: "NextRunAt");

            migrationBuilder.CreateIndex(
                name: "IX_media_serverTag_TagName",
                table: "media_serverTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_alternateText_MetadataContributionId",
                table: "metadata_alternateText",
                column: "MetadataContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_award_MetadataContributionId",
                table: "metadata_award",
                column: "MetadataContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_containerTag_TagName",
                table: "metadata_containerTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_contentRating_MetadataContributionId",
                table: "metadata_contentRating",
                column: "MetadataContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_contribution_MetadataContainerId",
                table: "metadata_contribution",
                column: "MetadataContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_contribution_ProviderType",
                table: "metadata_contribution",
                column: "ProviderType");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_customValue_MetadataContributionId_Key",
                table: "metadata_customValue",
                columns: new[] { "MetadataContributionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_metadata_externalId_MetadataContributionId",
                table: "metadata_externalId",
                column: "MetadataContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_genre_MetadataContributionId",
                table: "metadata_genre",
                column: "MetadataContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_image_MetadataContributionId",
                table: "metadata_image",
                column: "MetadataContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_preview_MetadataContributionId",
                table: "metadata_preview",
                column: "MetadataContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_reference_MetadataContributionId",
                table: "metadata_reference",
                column: "MetadataContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_relationship_Kind",
                table: "metadata_relationship",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_relationship_MetadataContributionId",
                table: "metadata_relationship",
                column: "MetadataContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_relationship_RelatedMetadataContainerId",
                table: "metadata_relationship",
                column: "RelatedMetadataContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_tierListRank_QualityTierId",
                table: "quality_tierListRank",
                column: "QualityTierId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_tierListRank_QualityTierListId_Ordinal",
                table: "quality_tierListRank",
                columns: new[] { "QualityTierListId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_quality_tierListTag_TagName",
                table: "quality_tierListTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_system_audit_Category",
                table: "system_audit",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_system_audit_EntityId",
                table: "system_audit",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_system_audit_EntityType",
                table: "system_audit",
                column: "EntityType");

            migrationBuilder.CreateIndex(
                name: "IX_system_audit_Timestamp",
                table: "system_audit",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_system_audit_UserId",
                table: "system_audit",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_system_rootFolder_ContentTypeId",
                table: "system_rootFolder",
                column: "ContentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_system_rootFolder_Path",
                table: "system_rootFolder",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wishlist_tag_WishlistsId",
                table: "wishlist_tag",
                column: "WishlistsId");

            migrationBuilder.CreateIndex(
                name: "IX_wishlist_wishlist_ContentFilterId",
                table: "wishlist_wishlist",
                column: "ContentFilterId");

            migrationBuilder.CreateIndex(
                name: "IX_wishlist_wishlist_OwnerUserId",
                table: "wishlist_wishlist",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_wishlist_wishlist_QualityTierListId",
                table: "wishlist_wishlist",
                column: "QualityTierListId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_apiKey");

            migrationBuilder.DropTable(
                name: "auth_refreshToken");

            migrationBuilder.DropTable(
                name: "configuration_setting");

            migrationBuilder.DropTable(
                name: "content_mediaFile");

            migrationBuilder.DropTable(
                name: "content_subject");

            migrationBuilder.DropTable(
                name: "download_clientTag");

            migrationBuilder.DropTable(
                name: "download_download");

            migrationBuilder.DropTable(
                name: "filter_contentTag");

            migrationBuilder.DropTable(
                name: "filter_subjectTag");

            migrationBuilder.DropTable(
                name: "indexer_connectionTag");

            migrationBuilder.DropTable(
                name: "indexer_releaseTag");

            migrationBuilder.DropTable(
                name: "job_execution");

            migrationBuilder.DropTable(
                name: "media_serverTag");

            migrationBuilder.DropTable(
                name: "metadata_alternateText");

            migrationBuilder.DropTable(
                name: "metadata_award");

            migrationBuilder.DropTable(
                name: "metadata_containerTag");

            migrationBuilder.DropTable(
                name: "metadata_contentRating");

            migrationBuilder.DropTable(
                name: "metadata_customValue");

            migrationBuilder.DropTable(
                name: "metadata_externalId");

            migrationBuilder.DropTable(
                name: "metadata_genre");

            migrationBuilder.DropTable(
                name: "metadata_image");

            migrationBuilder.DropTable(
                name: "metadata_preview");

            migrationBuilder.DropTable(
                name: "metadata_reference");

            migrationBuilder.DropTable(
                name: "metadata_relationship");

            migrationBuilder.DropTable(
                name: "quality_tierListRank");

            migrationBuilder.DropTable(
                name: "quality_tierListTag");

            migrationBuilder.DropTable(
                name: "system_audit");

            migrationBuilder.DropTable(
                name: "wishlist_tag");

            migrationBuilder.DropTable(
                name: "auth_user");

            migrationBuilder.DropTable(
                name: "configuration_settingsGroup");

            migrationBuilder.DropTable(
                name: "download_client");

            migrationBuilder.DropTable(
                name: "filter_subject");

            migrationBuilder.DropTable(
                name: "indexer_connection");

            migrationBuilder.DropTable(
                name: "indexer_release");

            migrationBuilder.DropTable(
                name: "job_eventTrigger");

            migrationBuilder.DropTable(
                name: "job_scheduleTrigger");

            migrationBuilder.DropTable(
                name: "media_server");

            migrationBuilder.DropTable(
                name: "metadata_contribution");

            migrationBuilder.DropTable(
                name: "quality_tier");

            migrationBuilder.DropTable(
                name: "system_tag");

            migrationBuilder.DropTable(
                name: "wishlist_wishlist");

            migrationBuilder.DropTable(
                name: "extension_extension");

            migrationBuilder.DropTable(
                name: "content_item");

            migrationBuilder.DropTable(
                name: "indexer_indexer");

            migrationBuilder.DropTable(
                name: "filter_content");

            migrationBuilder.DropTable(
                name: "content_hierarchyItem");

            migrationBuilder.DropTable(
                name: "indexer_feed");

            migrationBuilder.DropTable(
                name: "metadata_container");

            migrationBuilder.DropTable(
                name: "quality_tierList");

            migrationBuilder.DropTable(
                name: "system_rootFolder");
        }
    }
}
