using System;
using System.Collections.Generic;

using Magpie.Abstractions;
using Magpie.Abstractions.Api;
using Magpie.Api.EndpointGroups;
using Magpie.Api.Logging;
using Magpie.Api.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Magpie.Api;

/// <summary>
/// DI extensions that register the Magpie API layer (endpoint groups, NuGet service,
/// log buffer, and Swashbuckle).
/// </summary>
public static class MagpieApiServiceCollectionExtensions
{
    /// <summary>
    /// Concrete endpoint group types that ship with the Magpie API. Tracked separately so
    /// <c>MapMagpieEndpoints</c> can resolve each one individually and tolerate per-group
    /// resolution failures without aborting the whole startup path.
    /// </summary>
    internal static readonly IReadOnlyList<Type> EndpointGroupTypes =
    [
        typeof(ApiKeysEndpointGroup),
        typeof(AuthEndpointGroup),
        typeof(BackupsEndpointGroup),
        typeof(ContentEndpointGroup),
        typeof(ContentFiltersEndpointGroup),
        typeof(ContentTypeEndpointGroup),
        typeof(DownloadClientsEndpointGroup),
        typeof(DownloadsEndpointGroup),
        typeof(EventTriggersEndpointGroup),
        typeof(ExtensionsEndpointGroup),
        typeof(FileBrowserEndpointGroup),
        typeof(FilterQueryEndpointGroup),
        typeof(IndexerFeedsEndpointGroup),
        typeof(IndexersEndpointGroup),
        typeof(JobsEndpointGroup),
        typeof(MediaServersEndpointGroup),
        typeof(QualityTierListsEndpointGroup),
        typeof(QualityTiersEndpointGroup),
        typeof(ReleasesEndpointGroup),
        typeof(RootFoldersEndpointGroup),
        typeof(ScheduleTriggersEndpointGroup),
        typeof(SettingsEndpointGroup),
        typeof(SubjectFiltersEndpointGroup),
        typeof(SubjectTypeEndpointGroup),
        typeof(SubjectsEndpointGroup),
        typeof(SystemEndpointGroup),
        typeof(TagsEndpointGroup),
        typeof(UIEndpointGroup),
        typeof(UsersEndpointGroup),
        typeof(WishlistsEndpointGroup),
    ];

    /// <summary>
    /// Register every <see cref="IApiEndpointGroup"/> in this assembly along with the
    /// supporting services they need (NuGet client, log buffer, Swashbuckle).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMagpieApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<LogBuffer>();
        services.AddSingleton<ILoggerProvider>(sp => new LogBufferLoggerProvider(sp.GetRequiredService<LogBuffer>()));

        services.AddCors();

        services.AddHttpClient<INuGetService, NuGetSearchService>();

        // Registered both as concrete type (per-group resolution in MapMagpieEndpoints) and
        // as IApiEndpointGroup contribution.
        foreach (var groupType in EndpointGroupTypes)
        {
            services.TryAddSingleton(groupType);
            services.AddSingleton(typeof(IApiEndpointGroup), sp => sp.GetRequiredService(groupType));
        }

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        return services;
    }
}
