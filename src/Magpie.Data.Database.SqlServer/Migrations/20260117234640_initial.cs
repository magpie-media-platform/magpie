using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Magpie.Data.Database.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auth_provider",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_provider", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_subject",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DateLastRefreshed = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_subject", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "download_client",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LastChecked = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_client", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "extension_extension",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetABI = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssemblyName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    DisabledReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LoadOrder = table.Column<int>(type: "int", nullable: false),
                    Managed = table.Column<bool>(type: "bit", nullable: false),
                    NuGetId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PackageRepository = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AutoUpgrade = table.Column<bool>(type: "bit", nullable: false),
                    LastChecked = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extension_extension", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "filter_content",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Criteria = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_filter_content", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "filter_metadata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Criteria = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_filter_metadata", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "indexer_feed",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    LastSync = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_feed", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "job_eventTrigger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EventName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Filter = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Parameters = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_eventTrigger", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "job_scheduleTrigger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Schedule = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Parameters = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_scheduleTrigger", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "media_server",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LastChecked = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_server", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quality_tierList",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpgradeAllowed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_tierList", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "system_config",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DefaultLanguagePreference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AutoUpdateEnabled = table.Column<bool>(type: "bit", nullable: false),
                    BackupSchedule = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BackupRetentionPeriod = table.Column<TimeSpan>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_config", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "system_rootFolder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ContentTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_rootFolder", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "system_tag",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_tag", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "auth_providerSetting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_providerSetting", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_providerSetting_auth_provider_AuthProviderId",
                        column: x => x.AuthProviderId,
                        principalTable: "auth_provider",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auth_user",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    AuthProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PasswordSalt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailedLoginAttempts = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_user", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_user_auth_provider_AuthProviderId",
                        column: x => x.AuthProviderId,
                        principalTable: "auth_provider",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "download_clientSetting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DownloadClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_clientSetting", x => x.Id);
                    table.ForeignKey(
                        name: "FK_download_clientSetting_download_client_DownloadClientId",
                        column: x => x.DownloadClientId,
                        principalTable: "download_client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "configuration_settingsGroup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExtensionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_settingsGroup", x => x.Id);
                    table.ForeignKey(
                        name: "FK_configuration_settingsGroup_extension_extension_ExtensionId",
                        column: x => x.ExtensionId,
                        principalTable: "extension_extension",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_definition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeedId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Access = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_definition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_indexer_definition_indexer_feed_FeedId",
                        column: x => x.FeedId,
                        principalTable: "indexer_feed",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "indexer_feedSetting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IndexerFeedId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_feedSetting", x => x.Id);
                    table.ForeignKey(
                        name: "FK_indexer_feedSetting_indexer_feed_IndexerFeedId",
                        column: x => x.IndexerFeedId,
                        principalTable: "indexer_feed",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduleTriggerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventTriggerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Progress = table.Column<double>(type: "float", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
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
                name: "media_serverSetting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaServerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_serverSetting", x => x.Id);
                    table.ForeignKey(
                        name: "FK_media_serverSetting_media_server_MediaServerId",
                        column: x => x.MediaServerId,
                        principalTable: "media_server",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quality_tier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QualityTierListId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCutoff = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_tier", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quality_tier_quality_tierList_QualityTierListId",
                        column: x => x.QualityTierListId,
                        principalTable: "quality_tierList",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_hierarchyLevel",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentHierarchyLevelDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RootFolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateLastRefreshed = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_hierarchyLevel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_hierarchyLevel_content_hierarchyLevel_ParentId",
                        column: x => x.ParentId,
                        principalTable: "content_hierarchyLevel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_content_hierarchyLevel_system_rootFolder_RootFolderId",
                        column: x => x.RootFolderId,
                        principalTable: "system_rootFolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "content_subjectTag",
                columns: table => new
                {
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagName = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_subjectTag", x => new { x.SubjectId, x.TagName });
                    table.ForeignKey(
                        name: "FK_content_subjectTag_content_subject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "content_subject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_content_subjectTag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "download_clientTag",
                columns: table => new
                {
                    DownloadClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagName = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_clientTag", x => new { x.DownloadClientId, x.TagName });
                    table.ForeignKey(
                        name: "FK_download_clientTag_download_client_DownloadClientId",
                        column: x => x.DownloadClientId,
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
                    ContentFilterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagName = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_filter_contentTag", x => new { x.ContentFilterId, x.TagName });
                    table.ForeignKey(
                        name: "FK_filter_contentTag_filter_content_ContentFilterId",
                        column: x => x.ContentFilterId,
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
                name: "filter_metadataTag",
                columns: table => new
                {
                    MetadataFilterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagName = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_filter_metadataTag", x => new { x.MetadataFilterId, x.TagName });
                    table.ForeignKey(
                        name: "FK_filter_metadataTag_filter_metadata_MetadataFilterId",
                        column: x => x.MetadataFilterId,
                        principalTable: "filter_metadata",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_filter_metadataTag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quality_tierListTag",
                columns: table => new
                {
                    QualityTierListId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagName = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_tierListTag", x => new { x.QualityTierListId, x.TagName });
                    table.ForeignKey(
                        name: "FK_quality_tierListTag_quality_tierList_QualityTierListId",
                        column: x => x.QualityTierListId,
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
                name: "auth_apiKey",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    KeyHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    KeyPrefix = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_apiKey", x => x.Id);
                    table.ForeignKey(
                        name: "FK_auth_apiKey_auth_user_UserId",
                        column: x => x.UserId,
                        principalTable: "auth_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auth_userProviderData",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_userProviderData", x => new { x.UserId, x.Key });
                    table.ForeignKey(
                        name: "FK_auth_userProviderData_auth_user_UserId",
                        column: x => x.UserId,
                        principalTable: "auth_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "system_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_system_history_auth_user_UserId",
                        column: x => x.UserId,
                        principalTable: "auth_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "wishlist_wishlist",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerLock = table.Column<bool>(type: "bit", nullable: false),
                    ContentFilterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QualityTierListId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Monitored = table.Column<bool>(type: "bit", nullable: false),
                    SyncProvider = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SyncListId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SyncInterval = table.Column<TimeSpan>(type: "time", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wishlist_wishlist", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wishlist_wishlist_auth_user_UserId",
                        column: x => x.UserId,
                        principalTable: "auth_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
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
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "configuration_setting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SettingsGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SettingsGroupId1 = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configuration_setting", x => x.Id);
                    table.ForeignKey(
                        name: "FK_configuration_setting_configuration_settingsGroup_SettingsGroupId",
                        column: x => x.SettingsGroupId,
                        principalTable: "configuration_settingsGroup",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_configuration_setting_configuration_settingsGroup_SettingsGroupId1",
                        column: x => x.SettingsGroupId1,
                        principalTable: "configuration_settingsGroup",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "indexer_definitionCategory",
                columns: table => new
                {
                    IndexerDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_definitionCategory", x => new { x.IndexerDefinitionId, x.Category });
                    table.ForeignKey(
                        name: "FK_indexer_definitionCategory_indexer_definition_IndexerDefinitionId",
                        column: x => x.IndexerDefinitionId,
                        principalTable: "indexer_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_definitionLanguage",
                columns: table => new
                {
                    IndexerDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_definitionLanguage", x => new { x.IndexerDefinitionId, x.Language });
                    table.ForeignKey(
                        name: "FK_indexer_definitionLanguage_indexer_definition_IndexerDefinitionId",
                        column: x => x.IndexerDefinitionId,
                        principalTable: "indexer_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_definitionTag",
                columns: table => new
                {
                    IndexerDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagName = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_definitionTag", x => new { x.IndexerDefinitionId, x.TagName });
                    table.ForeignKey(
                        name: "FK_indexer_definitionTag_indexer_definition_IndexerDefinitionId",
                        column: x => x.IndexerDefinitionId,
                        principalTable: "indexer_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_indexer_definitionTag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_indexer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LastChecked = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_indexer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_indexer_indexer_indexer_definition_DefinitionId",
                        column: x => x.DefinitionId,
                        principalTable: "indexer_definition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_executionParameter",
                columns: table => new
                {
                    JobExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_executionParameter", x => new { x.JobExecutionId, x.Key });
                    table.ForeignKey(
                        name: "FK_job_executionParameter_job_execution_JobExecutionId",
                        column: x => x.JobExecutionId,
                        principalTable: "job_execution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quality_criterion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QualityTierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Field = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Operator = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_criterion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_quality_criterion_quality_tier_QualityTierId",
                        column: x => x.QualityTierId,
                        principalTable: "quality_tier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_languagePreference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentHierarchyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_languagePreference", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_languagePreference_content_hierarchyLevel_ContentHierarchyId",
                        column: x => x.ContentHierarchyId,
                        principalTable: "content_hierarchyLevel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metadata_container",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateAdded = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ContentHierarchyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_container", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_container_content_hierarchyLevel_ContentHierarchyId",
                        column: x => x.ContentHierarchyId,
                        principalTable: "content_hierarchyLevel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_metadata_container_content_subject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "content_subject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentHierarchyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QualityTierListId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Monitoring = table.Column<int>(type: "int", nullable: false),
                    Fulfillment = table.Column<int>(type: "int", nullable: false),
                    DateLastSearched = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedViaWishlistId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ImportedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_item_auth_user_ImportedByUserId",
                        column: x => x.ImportedByUserId,
                        principalTable: "auth_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_content_item_auth_user_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "auth_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_content_item_content_hierarchyLevel_ContentHierarchyId",
                        column: x => x.ContentHierarchyId,
                        principalTable: "content_hierarchyLevel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_content_item_quality_tierList_QualityTierListId",
                        column: x => x.QualityTierListId,
                        principalTable: "quality_tierList",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_content_item_wishlist_wishlist_RequestedViaWishlistId",
                        column: x => x.RequestedViaWishlistId,
                        principalTable: "wishlist_wishlist",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "wishlist_tag",
                columns: table => new
                {
                    WishlistId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagName = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wishlist_tag", x => new { x.WishlistId, x.TagName });
                    table.ForeignKey(
                        name: "FK_wishlist_tag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wishlist_tag_wishlist_wishlist_WishlistId",
                        column: x => x.WishlistId,
                        principalTable: "wishlist_wishlist",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_release",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IndexerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExternalId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: true),
                    InfoHash = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DownloadUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InfoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_release", x => x.Id);
                    table.ForeignKey(
                        name: "FK_indexer_release_indexer_indexer_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "indexer_indexer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "indexer_setting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IndexerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_setting", x => x.Id);
                    table.ForeignKey(
                        name: "FK_indexer_setting_indexer_indexer_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "indexer_indexer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_tag",
                columns: table => new
                {
                    IndexerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagName = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_tag", x => new { x.IndexerId, x.TagName });
                    table.ForeignKey(
                        name: "FK_indexer_tag_indexer_indexer_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "indexer_indexer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_indexer_tag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metadata_contribution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MetadataContainerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Locked = table.Column<bool>(type: "bit", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DateAdded = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DateLastRefreshed = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attribution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OriginalTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Language = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Overview = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublishDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Duration = table.Column<TimeSpan>(type: "time", nullable: true),
                    ProductionStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Established = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Dissolved = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BirthDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeathDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_contribution", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_contribution_metadata_container_MetadataContainerId",
                        column: x => x.MetadataContainerId,
                        principalTable: "metadata_container",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_itemTag",
                columns: table => new
                {
                    ContentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagName = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_itemTag", x => new { x.ContentItemId, x.TagName });
                    table.ForeignKey(
                        name: "FK_content_itemTag_content_item_ContentItemId",
                        column: x => x.ContentItemId,
                        principalTable: "content_item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_content_itemTag_system_tag_TagName",
                        column: x => x.TagName,
                        principalTable: "system_tag",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_mediaFile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RelativePath = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MediaInfo_Container = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MediaInfo_Duration = table.Column<TimeSpan>(type: "time", nullable: true),
                    MediaInfo_VideoCodec = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MediaInfo_VideoWidth = table.Column<int>(type: "int", nullable: true),
                    MediaInfo_VideoHeight = table.Column<int>(type: "int", nullable: true),
                    MediaInfo_VideoBitrate = table.Column<int>(type: "int", nullable: true),
                    MediaInfo_AudioCodec = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MediaInfo_AudioChannels = table.Column<int>(type: "int", nullable: true),
                    MediaInfo_AudioBitrate = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_mediaFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_mediaFile_content_item_ContentItemId",
                        column: x => x.ContentItemId,
                        principalTable: "content_item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "download_download",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DownloadClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    OutputPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateCompleted = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_download_download", x => x.Id);
                    table.ForeignKey(
                        name: "FK_download_download_content_item_ContentItemId",
                        column: x => x.ContentItemId,
                        principalTable: "content_item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
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
                name: "indexer_releaseCategory",
                columns: table => new
                {
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_releaseCategory", x => new { x.ReleaseId, x.Category });
                    table.ForeignKey(
                        name: "FK_indexer_releaseCategory_indexer_release_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "indexer_release",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_releaseLanguage",
                columns: table => new
                {
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_releaseLanguage", x => new { x.ReleaseId, x.Language });
                    table.ForeignKey(
                        name: "FK_indexer_releaseLanguage_indexer_release_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "indexer_release",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_releaseProperty",
                columns: table => new
                {
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_releaseProperty", x => new { x.ReleaseId, x.Key });
                    table.ForeignKey(
                        name: "FK_indexer_releaseProperty_indexer_release_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "indexer_release",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indexer_releaseTag",
                columns: table => new
                {
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagName = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indexer_releaseTag", x => new { x.ReleaseId, x.TagName });
                    table.ForeignKey(
                        name: "FK_indexer_releaseTag_indexer_release_ReleaseId",
                        column: x => x.ReleaseId,
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

            migrationBuilder.CreateTable(
                name: "metadata_alternateText",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_alternateText", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_alternateText_metadata_contribution_MetadataContributionId",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metadata_award",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    AwardBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Year = table.Column<int>(type: "int", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Language = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_award", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_award_metadata_contribution_MetadataContributionId",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metadata_contentRating",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    RatingsBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_contentRating", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_contentRating_metadata_contribution_MetadataContributionId",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metadata_customValue",
                columns: table => new
                {
                    MetadataContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_customValue", x => new { x.MetadataContributionId, x.Key });
                    table.ForeignKey(
                        name: "FK_metadata_customValue_metadata_contribution_MetadataContributionId",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metadata_externalId",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Attribution = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_externalId", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_externalId_metadata_contribution_MetadataContributionId",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metadata_genre",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    GenreName = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_genre", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_genre_metadata_contribution_MetadataContributionId",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metadata_image",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    ImageType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Attribution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Language = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_image", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_image_metadata_contribution_MetadataContributionId",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metadata_preview",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    PreviewType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Attribution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Language = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_preview", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_preview_metadata_contribution_MetadataContributionId",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metadata_reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Language = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_reference", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_reference_metadata_contribution_MetadataContributionId",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metadata_relationship",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MetadataContributionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Relation = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Qualifier = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_relationship", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metadata_relationship_content_subject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "content_subject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_metadata_relationship_metadata_contribution_MetadataContributionId",
                        column: x => x.MetadataContributionId,
                        principalTable: "metadata_contribution",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_auth_provider_IsDefault",
                table: "auth_provider",
                column: "IsDefault");

            migrationBuilder.CreateIndex(
                name: "IX_auth_providerSetting_AuthProviderId_Key",
                table: "auth_providerSetting",
                columns: new[] { "AuthProviderId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auth_user_AuthProviderId",
                table: "auth_user",
                column: "AuthProviderId");

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
                name: "IX_configuration_setting_SettingsGroupId1",
                table: "configuration_setting",
                column: "SettingsGroupId1");

            migrationBuilder.CreateIndex(
                name: "IX_configuration_settingsGroup_ExtensionId",
                table: "configuration_settingsGroup",
                column: "ExtensionId");

            migrationBuilder.CreateIndex(
                name: "IX_configuration_settingsGroup_Name",
                table: "configuration_settingsGroup",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyLevel_ContentHierarchyLevelDefinitionId",
                table: "content_hierarchyLevel",
                column: "ContentHierarchyLevelDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyLevel_ParentId",
                table: "content_hierarchyLevel",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyLevel_RootFolderId",
                table: "content_hierarchyLevel",
                column: "RootFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_content_hierarchyLevel_Slug",
                table: "content_hierarchyLevel",
                column: "Slug");

            migrationBuilder.CreateIndex(
                name: "IX_content_item_ContentHierarchyId",
                table: "content_item",
                column: "ContentHierarchyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_item_Fulfillment",
                table: "content_item",
                column: "Fulfillment");

            migrationBuilder.CreateIndex(
                name: "IX_content_item_ImportedByUserId",
                table: "content_item",
                column: "ImportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_content_item_Monitoring",
                table: "content_item",
                column: "Monitoring");

            migrationBuilder.CreateIndex(
                name: "IX_content_item_QualityTierListId",
                table: "content_item",
                column: "QualityTierListId");

            migrationBuilder.CreateIndex(
                name: "IX_content_item_RequestedByUserId",
                table: "content_item",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_content_item_RequestedViaWishlistId",
                table: "content_item",
                column: "RequestedViaWishlistId");

            migrationBuilder.CreateIndex(
                name: "IX_content_itemTag_TagName",
                table: "content_itemTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_content_languagePreference_ContentHierarchyId_Ordinal",
                table: "content_languagePreference",
                columns: new[] { "ContentHierarchyId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_content_mediaFile_ContentItemId",
                table: "content_mediaFile",
                column: "ContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_content_mediaFile_RelativePath",
                table: "content_mediaFile",
                column: "RelativePath");

            migrationBuilder.CreateIndex(
                name: "IX_content_subject_Kind",
                table: "content_subject",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_content_subjectTag_TagName",
                table: "content_subjectTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_download_clientSetting_DownloadClientId_Key",
                table: "download_clientSetting",
                columns: new[] { "DownloadClientId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_download_clientTag_TagName",
                table: "download_clientTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_download_download_ContentItemId",
                table: "download_download",
                column: "ContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_download_download_DownloadClientId",
                table: "download_download",
                column: "DownloadClientId");

            migrationBuilder.CreateIndex(
                name: "IX_download_download_ReleaseId",
                table: "download_download",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_download_download_State",
                table: "download_download",
                column: "State");

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
                name: "IX_filter_metadataTag_TagName",
                table: "filter_metadataTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_definition_FeedId",
                table: "indexer_definition",
                column: "FeedId");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_definition_Provider",
                table: "indexer_definition",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_definitionTag_TagName",
                table: "indexer_definitionTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_feedSetting_IndexerFeedId_Key",
                table: "indexer_feedSetting",
                columns: new[] { "IndexerFeedId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_indexer_indexer_DefinitionId",
                table: "indexer_indexer",
                column: "DefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_release_IndexerId",
                table: "indexer_release",
                column: "IndexerId");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_release_InfoHash",
                table: "indexer_release",
                column: "InfoHash");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_release_Provider",
                table: "indexer_release",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_release_PublishDate",
                table: "indexer_release",
                column: "PublishDate");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_releaseTag_TagName",
                table: "indexer_releaseTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_indexer_setting_IndexerId_Key",
                table: "indexer_setting",
                columns: new[] { "IndexerId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_indexer_tag_TagName",
                table: "indexer_tag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_job_eventTrigger_Enabled",
                table: "job_eventTrigger",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_job_eventTrigger_EventName",
                table: "job_eventTrigger",
                column: "EventName");

            migrationBuilder.CreateIndex(
                name: "IX_job_eventTrigger_JobTemplateId",
                table: "job_eventTrigger",
                column: "JobTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_EventTriggerId",
                table: "job_execution",
                column: "EventTriggerId");

            migrationBuilder.CreateIndex(
                name: "IX_job_execution_JobTemplateId",
                table: "job_execution",
                column: "JobTemplateId");

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
                name: "IX_job_scheduleTrigger_JobTemplateId",
                table: "job_scheduleTrigger",
                column: "JobTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_job_scheduleTrigger_NextRunAt",
                table: "job_scheduleTrigger",
                column: "NextRunAt");

            migrationBuilder.CreateIndex(
                name: "IX_media_serverSetting_MediaServerId_Key",
                table: "media_serverSetting",
                columns: new[] { "MediaServerId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_metadata_alternateText_MetadataContributionId",
                table: "metadata_alternateText",
                column: "MetadataContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_award_MetadataContributionId",
                table: "metadata_award",
                column: "MetadataContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_container_ContentHierarchyId",
                table: "metadata_container",
                column: "ContentHierarchyId",
                unique: true,
                filter: "[ContentHierarchyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_container_SubjectId",
                table: "metadata_container",
                column: "SubjectId",
                unique: true,
                filter: "[SubjectId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_contentRating_MetadataContributionId",
                table: "metadata_contentRating",
                column: "MetadataContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_contribution_MetadataContainerId",
                table: "metadata_contribution",
                column: "MetadataContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_contribution_Provider",
                table: "metadata_contribution",
                column: "Provider");

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
                name: "IX_metadata_relationship_MetadataContributionId",
                table: "metadata_relationship",
                column: "MetadataContributionId");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_relationship_Relation",
                table: "metadata_relationship",
                column: "Relation");

            migrationBuilder.CreateIndex(
                name: "IX_metadata_relationship_SubjectId",
                table: "metadata_relationship",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_criterion_QualityTierId",
                table: "quality_criterion",
                column: "QualityTierId");

            migrationBuilder.CreateIndex(
                name: "IX_quality_tier_QualityTierListId_Ordinal",
                table: "quality_tier",
                columns: new[] { "QualityTierListId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_quality_tierListTag_TagName",
                table: "quality_tierListTag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_system_history_Category",
                table: "system_history",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_system_history_EntityType_EntityId",
                table: "system_history",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_system_history_Success",
                table: "system_history",
                column: "Success");

            migrationBuilder.CreateIndex(
                name: "IX_system_history_Timestamp",
                table: "system_history",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_system_history_UserId",
                table: "system_history",
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
                name: "IX_wishlist_tag_TagName",
                table: "wishlist_tag",
                column: "TagName");

            migrationBuilder.CreateIndex(
                name: "IX_wishlist_wishlist_ContentFilterId",
                table: "wishlist_wishlist",
                column: "ContentFilterId");

            migrationBuilder.CreateIndex(
                name: "IX_wishlist_wishlist_QualityTierListId",
                table: "wishlist_wishlist",
                column: "QualityTierListId");

            migrationBuilder.CreateIndex(
                name: "IX_wishlist_wishlist_UserId",
                table: "wishlist_wishlist",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_apiKey");

            migrationBuilder.DropTable(
                name: "auth_providerSetting");

            migrationBuilder.DropTable(
                name: "auth_userProviderData");

            migrationBuilder.DropTable(
                name: "configuration_setting");

            migrationBuilder.DropTable(
                name: "content_itemTag");

            migrationBuilder.DropTable(
                name: "content_languagePreference");

            migrationBuilder.DropTable(
                name: "content_mediaFile");

            migrationBuilder.DropTable(
                name: "content_subjectTag");

            migrationBuilder.DropTable(
                name: "download_clientSetting");

            migrationBuilder.DropTable(
                name: "download_clientTag");

            migrationBuilder.DropTable(
                name: "download_download");

            migrationBuilder.DropTable(
                name: "filter_contentTag");

            migrationBuilder.DropTable(
                name: "filter_metadataTag");

            migrationBuilder.DropTable(
                name: "indexer_definitionCategory");

            migrationBuilder.DropTable(
                name: "indexer_definitionLanguage");

            migrationBuilder.DropTable(
                name: "indexer_definitionTag");

            migrationBuilder.DropTable(
                name: "indexer_feedSetting");

            migrationBuilder.DropTable(
                name: "indexer_releaseCategory");

            migrationBuilder.DropTable(
                name: "indexer_releaseLanguage");

            migrationBuilder.DropTable(
                name: "indexer_releaseProperty");

            migrationBuilder.DropTable(
                name: "indexer_releaseTag");

            migrationBuilder.DropTable(
                name: "indexer_setting");

            migrationBuilder.DropTable(
                name: "indexer_tag");

            migrationBuilder.DropTable(
                name: "job_executionParameter");

            migrationBuilder.DropTable(
                name: "media_serverSetting");

            migrationBuilder.DropTable(
                name: "metadata_alternateText");

            migrationBuilder.DropTable(
                name: "metadata_award");

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
                name: "quality_criterion");

            migrationBuilder.DropTable(
                name: "quality_tierListTag");

            migrationBuilder.DropTable(
                name: "system_config");

            migrationBuilder.DropTable(
                name: "system_history");

            migrationBuilder.DropTable(
                name: "wishlist_tag");

            migrationBuilder.DropTable(
                name: "configuration_settingsGroup");

            migrationBuilder.DropTable(
                name: "content_item");

            migrationBuilder.DropTable(
                name: "download_client");

            migrationBuilder.DropTable(
                name: "filter_metadata");

            migrationBuilder.DropTable(
                name: "indexer_release");

            migrationBuilder.DropTable(
                name: "job_execution");

            migrationBuilder.DropTable(
                name: "media_server");

            migrationBuilder.DropTable(
                name: "metadata_contribution");

            migrationBuilder.DropTable(
                name: "quality_tier");

            migrationBuilder.DropTable(
                name: "system_tag");

            migrationBuilder.DropTable(
                name: "extension_extension");

            migrationBuilder.DropTable(
                name: "wishlist_wishlist");

            migrationBuilder.DropTable(
                name: "indexer_indexer");

            migrationBuilder.DropTable(
                name: "job_eventTrigger");

            migrationBuilder.DropTable(
                name: "job_scheduleTrigger");

            migrationBuilder.DropTable(
                name: "metadata_container");

            migrationBuilder.DropTable(
                name: "auth_user");

            migrationBuilder.DropTable(
                name: "filter_content");

            migrationBuilder.DropTable(
                name: "quality_tierList");

            migrationBuilder.DropTable(
                name: "indexer_definition");

            migrationBuilder.DropTable(
                name: "content_hierarchyLevel");

            migrationBuilder.DropTable(
                name: "content_subject");

            migrationBuilder.DropTable(
                name: "auth_provider");

            migrationBuilder.DropTable(
                name: "indexer_feed");

            migrationBuilder.DropTable(
                name: "system_rootFolder");
        }
    }
}
