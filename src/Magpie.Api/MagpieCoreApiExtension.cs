using System;

using Magpie.Api.EndpointGroups;
using Magpie.Sdk.Api;
using Magpie.Sdk.Extensions;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Api;

/// <summary>
/// The Magpie extension for the core API endpoints.
/// </summary>
public sealed class MagpieCoreApiExtension : IExtension
{
    private static readonly Guid ExtensionId = new("50b51b2a-537c-4f09-861a-e36934db99b5");

    /// <inheritdoc />
    public Guid Id => ExtensionId;

    /// <inheritdoc />
    public string Name => "Magpie API";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IApiEndpointGroup, ApiKeysEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, AuditHistoryEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, AuthEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, AuthProvidersEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, DatabaseEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, ContentEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, ContentFiltersEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, ContentSearchEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, ContentTypeEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, DiagnosticEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, DownloadClientsEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, DownloadsEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, EventStreamEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, EventTriggersEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, ExtensionsEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, FileTypesEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, IndexerFeedsEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, IndexerSearchEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, IndexersEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, InteractiveSearchEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, JobsEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, LibraryImportEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, MagpieQlEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, MediaServersEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, NotificationRulesEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, ProvidersEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, QualityTierListsEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, QualityTiersEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, RemotePathMappingsEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, RootFoldersEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, ScheduleTriggersEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, SettingsEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, SubjectFiltersEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, SubjectTypeEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, SubjectsEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, SystemEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, TagsEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, UIEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, UsersEndpointGroup>();
        services.AddSingleton<IApiEndpointGroup, WishlistsEndpointGroup>();
    }
}
