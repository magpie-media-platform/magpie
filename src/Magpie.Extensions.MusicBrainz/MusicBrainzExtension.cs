using System;
using System.Net;
using System.Net.Http;

using Magpie.Extensions.MusicBrainz.QueryEngine;
using Magpie.Sdk.Configuration;
using Magpie.Sdk.Content;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.UI;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.MusicBrainz;

/// <summary>
/// The Magpie extension for keyless MusicBrainz music metadata and cover art.
/// </summary>
public sealed class MusicBrainzExtension : IExtension
{
    /// <inheritdoc />
    public Guid Id => Constants.ExtensionId;

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public string Name => "MusicBrainz";

    /// <inheritdoc />
    public string? TargetABI => "[1.0.0,)";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        services.AddHttpClient(
            MusicBrainzClient.HttpClientName,
            client =>
            {
                client.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(20);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });

        services.AddHttpClient(
            CoverArtArchiveClient.HttpClientName,
            client =>
            {
                client.BaseAddress = new Uri(CoverArtArchiveClient.DefaultBaseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        services.AddSingleton<IQueryCoordinator>(provider =>
        {
            using var scope = provider.CreateScope();
            return ActivatorUtilities.CreateInstance<QueryCoordinator>(scope.ServiceProvider);
        });
        services.AddSingleton<ICoverArtArchiveClient, CoverArtArchiveClient>();
        services.AddSingleton<IMusicBrainzClient, MusicBrainzClient>();
        services.AddScoped<ISettingsResolver, SettingsResolver>();
        services.AddScoped<IQueryPlanner, QueryPlanner>();
        services.AddScoped<IQueryExecutor, QueryExecutor>();
        services.AddScoped<ISearchEngine, SearchEngine>();

        services.AddScoped<IContentHierarchyMetadataProvider, ContentMetadataProvider>();
        services.AddScoped<IContentHierarchyMetadataSearcher, ContentMetadataSearcher>();
        services.AddScoped<IContentFactProvider, ContentFactProvider>();

        services.AddSingleton<IUIExtensionProvider, AdminUiProvider>();
        services.AddSingleton<IAssetProvider, AdminUiProvider>();
        services.AddSingleton<IExtensionSettingsSchemaProvider, AdminUiProvider>();
    }
}
