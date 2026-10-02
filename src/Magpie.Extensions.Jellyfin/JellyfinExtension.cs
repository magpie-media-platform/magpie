using System;
using System.Net.Http;

using Magpie.Sdk.Api;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.UI;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.Jellyfin;

/// <summary>
/// The Magpie extension for Jellyfin media servers.
/// </summary>
public sealed class JellyfinExtension : IExtension
{
    /// <inheritdoc />
    public Guid Id => Constants.ExtensionId;

    /// <inheritdoc />
    public string Name => Constants.DisplayName;

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient<IJellyfinClient, JellyfinClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        services.AddScoped<IMediaServerIntegration, JellyfinMediaServerIntegration>();
        services.AddScoped<JellyfinLibraryService>();

        services.AddScoped<IDiagnosticCheck, JellyfinLibraryMappingDiagnostic>();

        services.AddSingleton<IApiEndpointGroup, JellyfinLibrariesEndpointGroup>();

        services.AddSingleton<IAssetProvider, JellyfinAssetProvider>();
    }
}
