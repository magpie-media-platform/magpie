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
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public string Name => "Jellyfin";

    /// <inheritdoc />
    public string? TargetABI => "[1.0.0,)";

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
        services.AddScoped<IJellyfinLibraryService, JellyfinLibraryService>();

        services.AddScoped<IDiagnosticCheck, JellyfinLibraryMappingDiagnostic>();

        services.AddSingleton<IApiEndpointGroup, JellyfinLibrariesEndpointGroup>();

        services.AddSingleton<IAssetProvider, JellyfinAssetProvider>();
    }
}
