using System;
using System.Net;
using System.Net.Http;

using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Sdk.Configuration;
using Magpie.Sdk.Content;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.UI;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.OpenLibrary;

/// <summary>
/// The Magpie extension for keyless Open Library book metadata and covers.
/// </summary>
public sealed class OpenLibraryExtension : IExtension
{
    /// <inheritdoc />
    public Guid Id => Constants.ExtensionId;

    /// <inheritdoc />
    public string Name => "Open Library";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        services.AddHttpClient(OpenLibraryClient.HttpClientName)
            .AddUpstreamCooldown(followRedirects: true, requestTimeout: TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = false
            });

        services.AddSingleton<IOpenLibraryClient, OpenLibraryClient>();
        services.AddScoped<ISettingsResolver, SettingsResolver>();
        services.AddSingleton<IQueryPlanner, QueryPlanner>();
        services.AddScoped<IQueryExecutor, QueryExecutor>();
        services.AddScoped<ISearchEngine, SearchEngine>();
        services.AddScoped<IContentHierarchyMetadataSearcher, ContentMetadataSearcher>();

        services.AddScoped<IContentHierarchyMetadataProvider, ContentMetadataProvider>();
        services.AddScoped<ISubjectMetadataProvider, AuthorSubjectMetadataProvider>();

        services.AddSingleton<AdminUiProvider>();
        services.AddSingleton<IUIExtensionProvider>(provider => provider.GetRequiredService<AdminUiProvider>());
        services.AddSingleton<IAssetProvider>(provider => provider.GetRequiredService<AdminUiProvider>());
        services.AddSingleton<IExtensionSettingsSchemaProvider>(
            provider => provider.GetRequiredService<AdminUiProvider>());
        services.AddScoped<IExtensionConfigurationTester, ConfigurationTester>();
        services.AddScoped<IDiagnosticCheck, OpenLibraryMisPairedSubjectDiagnostic>();
        services.AddScoped<IDiagnosticRepair, OpenLibraryMisPairedSubjectRepair>();
    }
}
