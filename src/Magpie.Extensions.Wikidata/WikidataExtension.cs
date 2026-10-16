using System;
using System.Net;
using System.Net.Http;

using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Sdk.Configuration;
using Magpie.Sdk.Content;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.UI;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.Wikidata;

/// <summary>
/// The Magpie extension for Wikidata movie and television metadata.
/// </summary>
public sealed class WikidataExtension : IExtension
{
    /// <inheritdoc />
    public Guid Id { get; } = new("A70A2CC8-9BC7-4E24-9584-A4A458E4FCFD");

    /// <inheritdoc />
    public string Name => "Wikidata";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        services.AddHttpClient(WikidataClient.HttpClientName)
            .AddUpstreamCooldown(followRedirects: true)
            .AddHttpMessageHandler<WikidataHttpHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = false
            });

        services.AddTransient<WikidataHttpHandler>();
        services.AddSingleton<IWikidataClient, WikidataClient>();
        services.AddSingleton<IQueryCoordinator, QueryCoordinator>();
        services.AddSingleton<IQueryPlanner, QueryPlanner>();
        services.AddSingleton<IEntityResolver, EntityResolver>();
        services.AddSingleton<IQueryExecutor, QueryExecutor>();
        services.AddScoped<ISearchEngine, SearchEngine>();

        services.AddScoped<ISettingsResolver, SettingsResolver>();

        services.AddScoped<IContentHierarchyMetadataProvider, ContentMetadataProvider>();
        services.AddScoped<ISubjectMetadataProvider, SubjectMetadataProvider>();

        services.AddScoped<IContentHierarchyMetadataSearcher, ContentMetadataSearcher>();

        services.AddSingleton<AdminUiProvider>();
        services.AddSingleton<IUIExtensionProvider>(provider => provider.GetRequiredService<AdminUiProvider>());
        services.AddSingleton<IAssetProvider>(provider => provider.GetRequiredService<AdminUiProvider>());
        services.AddSingleton<IExtensionSettingsSchemaProvider>(
            provider => provider.GetRequiredService<AdminUiProvider>());
    }
}
