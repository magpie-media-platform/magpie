using System;

using Magpie.Abstractions;
using Magpie.Abstractions.Configuration;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Abstractions.System;
using Magpie.Core.Configuration;
using Magpie.Core.Extensions;
using Magpie.Core.Jobs;
using Magpie.Core.Repositories;
using Magpie.Core.Security;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Magpie.Core;

/// <summary>
/// DI extensions that register the Magpie core layer (repositories, transaction manager,
/// settings providers, security primitives, job manager, and the extension installer).
/// </summary>
/// <remarks>All collaborators are registered as singletons. The host is expected to use a single-connection database (SQLite default) where this lifetime is safe for repositories sharing a DbContext.</remarks>
public static class MagpieCoreServiceCollectionExtensions
{
    /// <summary>
    /// Register every service the core layer owns. Idempotent: repeated calls do not
    /// re-register services that are already present.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="systemRoot">The absolute path of the Magpie installation root.</param>
    /// <param name="extensionsRoot">The absolute path of the extensions staging directory.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMagpieCore(
        this IServiceCollection services,
        string systemRoot,
        string extensionsRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionsRoot);

        services.TryAddSingleton<IMagpieSystem>(_ => new MagpieSystem(systemRoot));

        services.TryAddSingleton<IApiKeyRepository, ApiKeyRepository>();
        services.TryAddSingleton<IAuditRecordRepository, AuditRecordRepository>();
        services.TryAddSingleton<IContentFilterRepository, ContentFilterRepository>();
        services.TryAddSingleton<IContentHierarchyItemRepository, ContentHierarchyItemRepository>();
        services.TryAddSingleton<IDownloadClientRepository, DownloadClientRepository>();
        services.TryAddSingleton<IDownloadRepository, DownloadRepository>();
        services.TryAddSingleton<IEventTriggerRepository, EventTriggerRepository>();
        services.TryAddSingleton<IExtensionRepository, ExtensionRepository>();
        services.TryAddSingleton<IIndexerConnectionRepository, IndexerConnectionRepository>();
        services.TryAddSingleton<IIndexerFeedRepository, IndexerFeedRepository>();
        services.TryAddSingleton<IIndexerRepository, IndexerRepository>();
        services.TryAddSingleton<IJobExecutionRepository, JobExecutionRepository>();
        services.TryAddSingleton<IMaterializedMetadataRepository, MaterializedMetadataRepository>();
        services.TryAddSingleton<IMediaServerRepository, MediaServerRepository>();
        services.TryAddSingleton<IMetadataContainerRepository, MetadataContainerRepository>();
        services.TryAddSingleton<IQualityTierListRepository, QualityTierListRepository>();
        services.TryAddSingleton<IQualityTierRepository, QualityTierRepository>();
        services.TryAddSingleton<IRefreshTokenRepository, RefreshTokenRepository>();
        services.TryAddSingleton<IReleaseRepository, ReleaseRepository>();
        services.TryAddSingleton<IRootFolderRepository, RootFolderRepository>();
        services.TryAddSingleton<IScheduleTriggerRepository, ScheduleTriggerRepository>();
        services.TryAddSingleton<ISettingsRepository, SettingsRepository>();
        services.TryAddSingleton<ISubjectFilterRepository, SubjectFilterRepository>();
        services.TryAddSingleton<ISubjectRepository, SubjectRepository>();
        services.TryAddSingleton<ITagRepository, TagRepository>();
        services.TryAddSingleton<IUserRepository, UserRepository>();
        services.TryAddSingleton<IWishlistRepository, WishlistRepository>();

        services.TryAddSingleton<ITransactionManager, TransactionManager>();
        services.TryAddSingleton<ISystemSettingsProvider, SystemSettingsProvider>();
        services.TryAddSingleton<ISettingsProvider>(sp => sp.GetRequiredService<ISystemSettingsProvider>());

        services.TryAddSingleton<IKeyGenerator, KeyGenerator>();
        services.TryAddSingleton<IKeyStore, InMemoryKeyStore>();
        services.TryAddSingleton<IPasswordPolicy, DefaultPasswordPolicy>();
        services.TryAddSingleton<IPasswordHasher, LocalPasswordHasher>();
        services.TryAddSingleton<IApiKeyManager, ApiKeyManager>();

        services.TryAddSingleton<IJobRuntime, NoOpJobRuntime>();
        services.TryAddSingleton<IJobManager, JobManager>();
        services.AddHostedService<StaleJobCleanupService>();

        services.TryAddSingleton(new ExtensionInstallerOptions { ExtensionsRoot = extensionsRoot });
        services.TryAddSingleton<IExtensionInstaller, ExtensionInstaller>();

        return services;
    }
}
