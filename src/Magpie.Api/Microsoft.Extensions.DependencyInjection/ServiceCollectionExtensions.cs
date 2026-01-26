using Magpie.Abstractions.Configuration;
using Magpie.Abstractions.Data;
using Magpie.Abstractions.Security;
using Magpie.Core.Configuration;
using Magpie.Core.Data;
using Magpie.Core.Security;

#pragma warning disable IDE0130
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>
/// Service collection extensions for Magpie API services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add Magpie core services including repositories and configuration providers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddMagpieServices(this IServiceCollection services)
    {
        services.AddScoped<IHistoryRepository, HistoryRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IAuthProviderRepository, AuthProviderRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<IRootFolderRepository, RootFolderRepository>();
        services.AddScoped<IMediaServerRepository, MediaServerRepository>();
        services.AddScoped<IDownloadClientRepository, DownloadClientRepository>();
        services.AddScoped<IIndexerRepository, IndexerRepository>();
        services.AddScoped<IIndexerFeedRepository, IndexerFeedRepository>();
        services.AddScoped<IIndexerDefinitionRepository, IndexerDefinitionRepository>();
        services.AddScoped<IQualityTierListRepository, QualityTierListRepository>();
        services.AddScoped<IContentFilterRepository, ContentFilterRepository>();
        services.AddScoped<IMetadataFilterRepository, MetadataFilterRepository>();
        services.AddScoped<IWishlistRepository, WishlistRepository>();
        services.AddScoped<ISubjectRepository, SubjectRepository>();
        services.AddScoped<ISubjectFilterRepository, SubjectFilterRepository>();
        services.AddScoped<IReleaseRepository, ReleaseRepository>();
        services.AddScoped<IDownloadRepository, DownloadRepository>();
        services.AddScoped<IScheduleTriggerRepository, ScheduleTriggerRepository>();
        services.AddScoped<IEventTriggerRepository, EventTriggerRepository>();
        services.AddScoped<IJobExecutionRepository, JobExecutionRepository>();
        services.AddScoped<IExtensionRepository, ExtensionRepository>();
        services.AddScoped<IServerConfigRepository, ServerConfigRepository>();
        services.AddScoped<IMetadataRepository, MetadataRepository>();
        services.AddScoped<IMaterializedMetadataRepository, MaterializedMetadataRepository>();
        services.AddScoped<IContentHierarchyLevelRepository, ContentHierarchyLevelRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ISystemSettingsProvider, SystemSettingsProvider>();
        services.AddScoped<IKeyStore, InMemoryKeyStore>();
        services.AddSingleton<IPasswordHasher, LocalPasswordHasher>();
        services.AddSingleton<IKeyGenerator, KeyGenerator>();
        services.AddSingleton<IPasswordPolicy, DefaultPasswordPolicy>();
        services.AddScoped<IUserManager, UserManager>();
        services.AddScoped<IApiKeyManager, ApiKeyManager>();

        return services;
    }
}
