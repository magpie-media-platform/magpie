using System;
using System.Net;
using System.Net.Http;

using Magpie.Extensions.Tvmaze.Pipeline;
using Magpie.Sdk.Configuration;
using Magpie.Sdk.Content;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.UI;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.Tvmaze;

/// <summary>
/// The Magpie extension for keyless TVmaze television metadata.
/// </summary>
public sealed class TvmazeExtension : IExtension
{
    /// <inheritdoc />
    public Guid Id => Constants.ExtensionId;

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public string Name => "TVmaze";

    /// <inheritdoc />
    public string? TargetABI => "[1.0.0,)";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        services.AddHttpClient(
            TvmazeClient.HttpClientName,
            client =>
            {
                client.BaseAddress = new Uri(TvmazeClient.DefaultBaseUrl, UriKind.Absolute);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = false
            });

        services.AddScoped<IClient, TvmazeClient>();
        services.AddScoped<ISearchPlanner, SearchPlanner>();
        services.AddScoped<ISearchExecutor, SearchExecutor>();

        services.AddScoped<IContentHierarchyMetadataProvider, ContentMetadataProvider>();
        services.AddScoped<IContentHierarchyMetadataSearcher, ContentMetadataSearcher>();

        services.AddSingleton<IUIExtensionProvider, AdminUiProvider>();
        services.AddSingleton<IAssetProvider, AdminUiProvider>();
        services.AddSingleton<IExtensionSettingsSchemaProvider, AdminUiProvider>();
    }
}
