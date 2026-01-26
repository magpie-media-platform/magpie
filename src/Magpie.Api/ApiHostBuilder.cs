using Magpie.Abstractions.Api;
using Magpie.Api.EndpointGroups;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Api;

/// <summary>
/// Provides methods for configuring and building the Magpie API host.
/// </summary>
public static class ApiHostBuilder
{
    /// <summary>
    /// Configures the API services on the provided service collection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    public static IServiceCollection AddMagpieApi(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options => options.SwaggerDoc("v1", new() { Title = "Magpie API", Version = "v1" }));
        services.AddEndpointGroup<SystemEndpointGroup>();
        services.AddEndpointGroup<UsersEndpointGroup>();
        services.AddEndpointGroup<ApiKeysEndpointGroup>();
        services.AddEndpointGroup<AuthProvidersEndpointGroup>();
        services.AddEndpointGroup<SettingsEndpointGroup>();
        services.AddEndpointGroup<RootFoldersEndpointGroup>();
        services.AddEndpointGroup<TagsEndpointGroup>();
        services.AddEndpointGroup<MediaServersEndpointGroup>();
        services.AddEndpointGroup<DownloadClientsEndpointGroup>();
        services.AddEndpointGroup<IndexersEndpointGroup>();
        services.AddEndpointGroup<IndexerFeedsEndpointGroup>();
        services.AddEndpointGroup<QualityTierListsEndpointGroup>();
        services.AddEndpointGroup<ContentFiltersEndpointGroup>();
        services.AddEndpointGroup<MetadataFiltersEndpointGroup>();
        services.AddEndpointGroup<WishlistsEndpointGroup>();
        services.AddEndpointGroup<SubjectsEndpointGroup>();
        services.AddEndpointGroup<ReleasesEndpointGroup>();
        services.AddEndpointGroup<DownloadsEndpointGroup>();
        services.AddEndpointGroup<JobsEndpointGroup>();
        services.AddEndpointGroup<ExtensionsEndpointGroup>();
        services.AddEndpointGroup<AuthEndpointGroup>();

        return services;
    }

    /// <summary>
    /// Maps all registered API endpoint groups to the application.
    /// </summary>
    /// <param name="app">The web application to configure.</param>
    public static WebApplication UseMagpieApi(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Magpie API"));

        var endpointGroups = app.Services.GetServices<IApiEndpointGroup>();
        var apiGroup = app.MapGroup("/api");

        foreach (var group in endpointGroups)
        {
            var groupRoute = apiGroup.MapGroup(group.Name);
            group.MapEndpoints(groupRoute);
        }

        return app;
    }

    /// <summary>
    /// Registers an endpoint group with the service collection.
    /// </summary>
    /// <typeparam name="TEndpointGroup">The endpoint group type to register.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    public static IServiceCollection AddEndpointGroup<TEndpointGroup>(this IServiceCollection services)
        where TEndpointGroup : class, IApiEndpointGroup
    {
        services.AddSingleton<IApiEndpointGroup, TEndpointGroup>();
        return services;
    }
}
