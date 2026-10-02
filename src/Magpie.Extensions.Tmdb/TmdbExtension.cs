using System;
using System.Linq;
using System.Net.Http;

using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Sdk.Configuration;
using Magpie.Sdk.Content;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.UI;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.Tmdb;

/// <summary>
/// The Magpie extension for TMDb metadata.
/// </summary>
public sealed class TmdbExtension : IExtension
{
    /// <inheritdoc />
    public Guid Id => Constants.ExtensionId;

    /// <inheritdoc />
    public string Name => "TMDb";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        services.AddHttpClient(
            TmdbClient.HttpClientName,
            client =>
            {
                client.BaseAddress = new Uri(TmdbClient.DefaultBaseUrl, UriKind.Absolute);
            })
            .AddUpstreamCooldown(request => request.RequestUri?.Query.TrimStart('?').Split('&')
                .FirstOrDefault(value => value.StartsWith("api_key=", StringComparison.Ordinal)))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        services.AddSingleton<ITmdbClient, TmdbClient>();
        services.AddScoped<ISettingsResolver, SettingsResolver>();

        services.AddSingleton<IQueryCoordinator, QueryCoordinator>();
        services.AddScoped<IQueryPlanner, QueryPlanner>();
        services.AddScoped<IQueryExecutor, QueryExecutor>();
        services.AddScoped<IQueryEngine, QueryEngine.QueryEngine>();

        services.AddScoped<IContentHierarchyMetadataProvider, ContentMetadataProvider>();

        services.AddScoped<IContentHierarchyMetadataSearcher, ContentMetadataSearcher>();

        services.AddScoped<ISubjectMetadataProvider, SubjectMetadataProvider>();

        services.AddSingleton<AdminUiProvider>();
        services.AddSingleton<IUIExtensionProvider>(provider => provider.GetRequiredService<AdminUiProvider>());
        services.AddSingleton<IAssetProvider>(provider => provider.GetRequiredService<AdminUiProvider>());
        services.AddSingleton<IExtensionSettingsSchemaProvider>(
            provider => provider.GetRequiredService<AdminUiProvider>());

        services.AddScoped<IDiagnosticCheck, ApiKeyMissingDiagnostic>();
    }
}
