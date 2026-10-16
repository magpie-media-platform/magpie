using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Core.Caching;
using Magpie.Core.Content;
using Magpie.Core.Diagnostics;
using Magpie.Core.Events;
using Magpie.Core.Extensions;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Downloads;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Core.Jobs.Metadata;
using Magpie.Core.Jobs.Runtime;
using Magpie.Core.Jobs.Runtime.Scheduling;
using Magpie.Core.Notifications;
using Magpie.Core.Releases;
using Magpie.Core.Repositories;
using Magpie.Core.Security;
using Magpie.Core.Subjects;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Caching;
using Magpie.Sdk.Content;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Http;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Notifications;
using Magpie.Sdk.Security;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>
/// The dependency-injection registration extensions for the Magpie core services.
/// </summary>
public static class MagpieCoreServiceCollectionExtensions
{
    /// <summary>
    /// Loads the given extensions and registers their runtime state in the service collection.
    /// </summary>
    /// <param name="services">The service collection to register into</param>
    /// <param name="extensions">The installed extensions to load</param>
    /// <param name="builtIns">The built-in extension instances to load</param>
    /// <param name="loggerFactory">The logger factory for the extension loader</param>
    /// <returns>The loaded extension instances.</returns>
    public static IReadOnlyList<IExtension> AddMagpieExtensions(
        this IServiceCollection services,
        IEnumerable<Extension> extensions,
        IEnumerable<IExtension> builtIns,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(builtIns);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var loader = new ExtensionLoader(loggerFactory.CreateLogger<ExtensionLoader>());
        var failures = new List<Extension>();
        var loaded = loader.LoadBuiltInExtensions(builtIns, services)
            .Concat(loader.LoadExtensions(extensions, services, failures))
            .ToArray();

        var context = new ExtensionLoadContext(loaded, failures);

        services.AddSingleton(context);

        return context.Loaded;
    }

    /// <summary>
    /// Registers the Magpie core services in the container.
    /// </summary>
    /// <param name="services">The service collection to register into</param>
    /// <param name="hosted">Whether Magpie runs as a service host</param>
    /// <param name="jobRunner">Whether the host runs jobs</param>
    public static IServiceCollection AddMagpieCore(this IServiceCollection services, bool hosted, bool jobRunner)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IEventBus, EventBus>();

        services.AddRepositories();

        services.AddScoped<IFileSystem, FileSystem>();
        services.AddScoped<IContentFileService, ContentFileService>();
        services.AddScoped<IPasswordHasher, LocalPasswordHasher>();
        services.AddScoped<IDataProtectionService, DataProtectionService>();

        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IFilterService, FilterService>();
        services.AddScoped<ISubjectService, SubjectService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<IWishlistService, WishlistService>();
        services.AddScoped<IDatabaseManagementService, DatabaseManagementService>();
        services.AddScoped<IDiagnosticsService, DiagnosticsService>();
        services.AddScoped<IDownloadService, DownloadService>();
        services.AddScoped<ReleaseBlockList>();
        services.AddScoped<IReleaseService, ReleaseService>();
        services.AddScoped<IReleaseEvaluationService, ReleaseEvaluationService>();
        services.AddScoped<IReleaseSearchService, ReleaseSearchService>();
        services.AddScoped<IReleaseSearchQueryExecutor, ReleaseSearchQueryExecutor>();
        services.AddScoped<IQualityService, QualityService>();
        services.AddScoped<IRootFolderService, RootFolderService>();
        services.AddScoped<IIndexerService, IndexerService>();
        services.AddScoped<IDownloadClientService, DownloadClientService>();
        services.AddScoped<IRemotePathMappingService, RemotePathMappingService>();
        services.AddScoped<IMediaServerService, MediaServerService>();
        services.AddScoped<IMetadataSearchService, MetadataSearchService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IContentService, ContentService>();
        services.AddScoped<IMetadataService, MetadataService>();
        services.AddScoped<ILibraryImportService, LibraryImportService>();
        services.AddScoped<IAuditHistoryService, AuditHistoryService>();
        services.AddScoped<IProgressReporter, ProgressReporter>();
        services.AddScoped<IJobManager, JobManager>();
        services.AddScoped<INotificationService, NotificationService>();

        services.AddSingleton<IJobExecutionScopeFactory, JobExecutionScope>();
        services.AddSingleton<IContentTypeRegistry, ContentTypeRegistry>();
        services.AddSingleton<ISubjectKindRegistry, SubjectKindRegistry>();
        services.AddSingleton<ISubjectKindDefinitionProvider, BuiltInSubjectKindDefinitionProvider>();
        services.AddScoped<IExtensionLifecycleManager, ExtensionLifecycleManager>();
        services.AddScoped<IMagpieSystem, MagpieSystem>();
        services.AddScoped<ISetupService, SetupService>();

        services.TryAddSingleton<HttpOptions>();
        services.AddHttpClient(WebhookNotificationProvider.HttpClientName)
            .AddStandardResilienceHandler()
            .Configure((options, provider) =>
            {
                options.TotalRequestTimeout.Timeout = provider.GetRequiredService<HttpOptions>().Timeout;
                if (options.AttemptTimeout.Timeout > options.TotalRequestTimeout.Timeout)
                {
                    options.AttemptTimeout.Timeout = options.TotalRequestTimeout.Timeout;
                }
            })
            .SelectPipelineByAuthority();
        services.AddHttpClient(ExtensionService.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            })
            .AddStandardResilienceHandler()
            .Configure((options, provider) =>
            {
                options.TotalRequestTimeout.Timeout = provider.GetRequiredService<HttpOptions>().Timeout;
                if (options.AttemptTimeout.Timeout > options.TotalRequestTimeout.Timeout)
                {
                    options.AttemptTimeout.Timeout = options.TotalRequestTimeout.Timeout;
                }
            })
            .SelectPipelineByAuthority();
        services.AddScoped<IExtensionService, ExtensionService>();

        services.RegisterJobs();
        services.RegisterDiagnostics();
        services.AddSingleton<DatabaseMigrationState>();

        if (hosted)
        {
            services.AddSingleton<ExtensionActivationBackgroundService.DatabaseFailureInterceptor>();
            services.ConfigureDbContext<MagpieDbContext>((provider, options) => options.AddInterceptors(
                provider.GetRequiredService<ExtensionActivationBackgroundService.DatabaseFailureInterceptor>()));
            services.RegisterHostedServices(jobRunner);
        }

        return services;
    }

    /// <summary>
    /// Registers the default implementations of the core services.
    /// </summary>
    /// <param name="services">The service collection to register into</param>
    public static IServiceCollection AddDefaultProviders(this IServiceCollection services)
    {
        services.TryAddSingleton<IKeyStore, AtomicFileKeyStore>();
        services.TryAddSingleton<IDataProtectionProvider, DefaultDataProtectionProvider>();
        services.TryAddSingleton<IPasswordPolicy, DefaultPasswordPolicy>();
        services.TryAddSingleton<IJobRuntime, DefaultJobRuntime>();
        services.AddSingleton<MemoryCacheStore>();
        services.TryAddScoped<ICacheFactory, MemoryCacheFactory>();

        services.TryAddScoped<IJobRetryPolicy, DefaultJobRetryPolicy>();

        services.AddScoped<INotificationProvider, WebhookNotificationProvider>();

        return services;
    }

    private static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IAuditHistoryRepository, AuditHistoryRepository>();
        services.AddScoped<IAuditRecordRepository, AuditRecordRepository>();
        services.AddScoped<IBlockedFileTypeRepository, BlockedFileTypeRepository>();
        services.AddScoped<IContentFilterRepository, ContentFilterRepository>();
        services.AddScoped<IContentRepository, ContentRepository>();
        services.AddScoped<IContentHierarchyItemRepository, ContentHierarchyItemRepository>();
        services.AddScoped<IDiagnosticFindingRepository, DiagnosticFindingRepository>();
        services.AddScoped<IDownloadClientRepository, DownloadClientRepository>();
        services.AddScoped<IDownloadRepository, DownloadRepository>();
        services.AddScoped<IEventTriggerRepository, EventTriggerRepository>();
        services.AddScoped<IExtensionActivationGenerationRepository, ExtensionActivationGenerationRepository>();
        services.AddScoped<IExtensionNodeStatusRepository, ExtensionNodeStatusRepository>();
        services.AddScoped<IExtensionPackageSourceRepository, ExtensionPackageSourceRepository>();
        services.AddScoped<IExtensionRepository, ExtensionRepository>();
        services.AddScoped<IIndexerConnectionRepository, IndexerConnectionRepository>();
        services.AddScoped<IIndexerFeedRepository, IndexerFeedRepository>();
        services.AddScoped<IIndexerRepository, IndexerRepository>();
        services.AddScoped<IJobExecutionRepository, JobExecutionRepository>();
        services.AddScoped<IMaterializedMetadataRepository, MaterializedMetadataRepository>();
        services.AddScoped<IMediaFileRepository, MediaFileRepository>();
        services.AddScoped<IMediaServerRepository, MediaServerRepository>();
        services.AddScoped<IMetadataContainerRepository, MetadataContainerRepository>();
        services.AddScoped<IMimeMappingRepository, MimeMappingRepository>();
        services.AddScoped<INotificationRuleRepository, NotificationRuleRepository>();
        services.AddScoped<ILibraryImportCandidateRepository, LibraryImportCandidateRepository>();
        services.AddScoped<IQualityTierListRepository, QualityTierListRepository>();
        services.AddScoped<IQualityTierRepository, QualityTierRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IReleaseScopeRepository, ReleaseScopeRepository>();
        services.AddScoped<IDownloadScopeRepository, DownloadScopeRepository>();
        services.AddScoped<IReleaseRepository, ReleaseRepository>();
        services.AddScoped<IRemotePathMappingRepository, RemotePathMappingRepository>();
        services.AddScoped<IRootFolderRepository, RootFolderRepository>();
        services.AddScoped<IScheduleTriggerRepository, ScheduleTriggerRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<ISubjectFilterRepository, SubjectFilterRepository>();
        services.AddScoped<ISubjectRepository, SubjectRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IWishlistRepository, WishlistRepository>();
        services.AddScoped<IWishlistDiscoveredRepository, WishlistDiscoveredRepository>();

        return services;
    }

    private static IServiceCollection RegisterDiagnostics(this IServiceCollection services)
    {
        services.AddScoped<IDiagnosticCheck, SetupPrerequisitesDiagnostic>();
        services.AddScoped<IDiagnosticCheck, EmptyQualityProfileDiagnostic>();
        services.AddScoped<IDiagnosticCheck, ContentDefaultsDiagnostic>();
        services.AddScoped<IDiagnosticCheck, PathResolutionDiagnostic>();
        services.AddScoped<IDiagnosticCheck, DefaultJobScheduleDiagnostic>();
        services.AddScoped<IDiagnosticCheck, DuplicateRootDiagnostic>();
        services.AddScoped<IDiagnosticCheck, ExtensionLoadDiagnostic>();
        services.AddScoped<IDiagnosticCheck, NodeStatusDiagnostic>();
        services.AddScoped<IDiagnosticRepair, ContentDefaultsRepair>();
        services.AddScoped<IDiagnosticRepair, MissingDefaultJobRepair>();

        return services;
    }

    private static IServiceCollection RegisterJobs(this IServiceCollection services)
    {
        services.AddSingleton<JobShutdownSignal>();
        services.AddSingleton<IJobConcurrencyScheduler, MlfqJobConcurrencyScheduler>();
        services.AddSingleton<IJobRegistry, JobRegistry>();
        services.AddScoped<DefaultJobRetryPolicy>();

        services.RegisterJob<SendNotificationJob>(SendNotificationJob.Spec);
        services.RegisterJob<ContentMetadataRefreshItemJob>(ContentMetadataRefreshItemJob.Spec);
        services.RegisterJob<SubjectMetadataRefreshItemJob>(SubjectMetadataRefreshItemJob.Spec);
        services.RegisterJob<SubjectMetadataRefreshScanJob>(SubjectMetadataRefreshScanJob.Spec);
        services.RegisterJob<ReleaseSearchItemJob>(ReleaseSearchItemJob.Spec);
        services.RegisterJob<ReleaseDownloadItemJob>(ReleaseDownloadItemJob.Spec);
        services.RegisterJob<DownloadRefreshJob>(DownloadRefreshJob.Spec);
        services.RegisterJob<DownloadImportItemJob>(DownloadImportItemJob.Spec);
        services.RegisterJob<ReleaseAutoGrabScanJob>(ReleaseAutoGrabScanJob.Spec);
        services.RegisterJob<ReleaseAutoGrabItemJob>(ReleaseAutoGrabItemJob.Spec);
        services.RegisterJob<ReleaseSearchScanJob>(ReleaseSearchScanJob.Spec);
        services.RegisterJob<WishlistRefreshItemJob>(WishlistRefreshItemJob.Spec);
        services.RegisterJob<WishlistRefreshScanJob>(WishlistRefreshScanJob.Spec);
        services.RegisterJob<MediaServerRefreshJob>(MediaServerRefreshJob.Spec);
        services.RegisterJob<RootFolderContentDiscoveryJob>(RootFolderContentDiscoveryJob.Spec);
        services.RegisterJob<RootFolderContentImportItemJob>(RootFolderContentImportItemJob.Spec);
        services.RegisterJob<RootFolderContentImportResumeJob>(RootFolderContentImportResumeJob.Spec);
        services.RegisterJob<LibraryImportMatchResolutionJob>(LibraryImportMatchResolutionJob.Spec);
        services.RegisterJob<RetentionCleanupJob>(RetentionCleanupJob.Spec);
        services.RegisterJob<DatabaseMaintenanceJob>(DatabaseMaintenanceJob.Spec);
        services.RegisterJob<DatabaseBackupJob>(DatabaseBackupJob.Spec);
        services.RegisterJob<IndexerFeedSyncScanJob>(IndexerFeedSyncScanJob.Spec);
        services.RegisterJob<IndexerFeedSyncItemJob>(IndexerFeedSyncItemJob.Spec);
        services.RegisterJob<IntegrationHealthCheckJob>(IntegrationHealthCheckJob.Spec);
        services.RegisterJob<MediaFileRenameJob>(MediaFileRenameJob.Spec);
        services.RegisterJob<DownloadImportScanJob>(DownloadImportScanJob.Spec);
        services.RegisterJob<DownloadStuckDetectionJob>(DownloadStuckDetectionJob.Spec);
        services.RegisterJob<DownloadCleanupJob>(DownloadCleanupJob.Spec);
        services.RegisterJob<DownloadFulfilledCancellationJob>(DownloadFulfilledCancellationJob.Spec);
        services.RegisterJob<ContentFulfillmentReconciliationJob>(ContentFulfillmentReconciliationJob.Spec);
        services.RegisterJob<DiagnosticCheckJob>(DiagnosticCheckJob.Spec);
        services.RegisterJob<ContentMetadataRefreshScanJob>(ContentMetadataRefreshScanJob.Spec);
        services.RegisterJob<MediaInfoExtractScanJob>(MediaInfoExtractScanJob.Spec);
        services.RegisterJob<MediaInfoExtractItemJob>(MediaInfoExtractItemJob.Spec);
        services.RegisterJob<MediaFileReconciliationJob>(MediaFileReconciliationJob.Spec);
        services.RegisterJob<MetadataContainerCleanupJob>(MetadataContainerCleanupJob.Spec);
        services.RegisterJob<SubjectMaintenanceJob>(SubjectMaintenanceJob.Spec);

        return services;
    }

    private static IServiceCollection RegisterHostedServices(this IServiceCollection services, bool jobRunner)
    {
        services.AddHostedService<ClusterBackplaneBackgroundService>();
        services.AddHostedService<EventDispatchBackgroundService>();
        services.AddHostedService<ExtensionActivationBackgroundService>();
        if (jobRunner)
        {
            services.AddHostedService<JobRuntimeBackgroundService>();
        }

        return services;
    }
}
