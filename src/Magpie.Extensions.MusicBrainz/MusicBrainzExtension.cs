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
    public string Name => "MusicBrainz";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        services.AddHttpClient(
            MusicBrainzClient.HttpClientName,
            client =>
            {
                client.BaseAddress = new Uri(MusicBrainzClient.DefaultBaseUrl, UriKind.Absolute);
            })
            .AddUpstreamCooldown(followRedirects: true, requestTimeout: MusicBrainzClient.RequestTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = false
            });

        services.AddHttpClient(
            CoverArtArchiveClient.HttpClientName,
            client =>
            {
                client.BaseAddress = new Uri(CoverArtArchiveClient.DefaultBaseUrl, UriKind.Absolute);
            })
            .AddUpstreamCooldown(requestTimeout: TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        services.AddSingleton<IQueryCoordinator, QueryCoordinator>();
        services.AddSingleton<ICoverArtArchiveClient, CoverArtArchiveClient>();
        services.AddSingleton<IMusicBrainzClient, MusicBrainzClient>();
        services.AddScoped<ISettingsResolver, SettingsResolver>();
        services.AddScoped<IQueryPlanner, QueryPlanner>();
        services.AddScoped<IQueryExecutor, QueryExecutor>();

        services.AddScoped<IContentHierarchyMetadataProvider, ContentMetadataProvider>();
        services.AddScoped<IContentHierarchyMetadataSearcher, ContentMetadataSearcher>();
        services.AddScoped<IContentFactProvider, ContentFactProvider>();

        services.AddSingleton<AdminUiProvider>();
        services.AddSingleton<IUIExtensionProvider>(provider => provider.GetRequiredService<AdminUiProvider>());
        services.AddSingleton<IAssetProvider>(provider => provider.GetRequiredService<AdminUiProvider>());
        services.AddSingleton<IExtensionSettingsSchemaProvider>(
            provider => provider.GetRequiredService<AdminUiProvider>());
    }
}
