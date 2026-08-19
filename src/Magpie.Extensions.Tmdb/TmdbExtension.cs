using System;
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
    public Guid Id => new("FE120126-EFD7-4DB1-A3A1-8B8A881E013A");

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public string Name => "TMDb";

    /// <inheritdoc />
    public string? TargetABI => "[1.0.0,)";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        services.AddHttpClient(
            TmdbClient.HttpClientName,
            client =>
            {
                client.BaseAddress = new Uri(TmdbClient.DefaultBaseUrl, UriKind.Absolute);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        services.AddSingleton<ITmdbClient, TmdbClient>();
        services.AddScoped<ISettingsResolver, SettingsResolver>();

        services.AddSingleton<IQueryCoordinator>(provider =>
        {
            using var scope = provider.CreateScope();
            return ActivatorUtilities.CreateInstance<QueryCoordinator>(scope.ServiceProvider);
        });
        services.AddScoped<IQueryPlanner, QueryPlanner>();
        services.AddScoped<IQueryExecutor, QueryExecutor>();
        services.AddScoped<IQueryEngine, QueryEngine.QueryEngine>();

        services.AddScoped<IContentHierarchyMetadataProvider, ContentMetadataProvider>();

        services.AddScoped<IContentHierarchyMetadataSearcher, ContentMetadataSearcher>();

        services.AddScoped<ISubjectMetadataProvider, SubjectMetadataProvider>();

        services.AddSingleton<IUIExtensionProvider, AdminUiProvider>();
        services.AddSingleton<IAssetProvider, AdminUiProvider>();
        services.AddSingleton<IExtensionSettingsSchemaProvider, AdminUiProvider>();

        services.AddScoped<IDiagnosticCheck, ApiKeyMissingDiagnostic>();
    }
}
