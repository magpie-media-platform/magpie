using System;
using System.Net.Http;

using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Integrations;

using Microsoft.Extensions.DependencyInjection;

namespace Magpie.Extensions.Qbittorrent;

/// <summary>
/// The Magpie extension for qBittorrent download clients.
/// </summary>
public sealed class QbittorrentExtension : IExtension
{
    private static readonly Guid ExtensionId = new("F9238BE4-1471-4C6E-859F-D26D472577EC");

    /// <inheritdoc />
    public Guid Id => ExtensionId;

    /// <inheritdoc />
    public string Name => Constants.DisplayName;

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services)
    {
        services.AddHttpClient<IQbittorrentClient, QbittorrentClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                UseCookies = true,
                AllowAutoRedirect = false
            });
        services.AddHttpClient(
            QbittorrentDownloadClientIntegration.TorrentFileHttpClientName,
            client => client.Timeout = TimeSpan.FromSeconds(30));

        services.AddScoped<IDownloadClientIntegration, QbittorrentDownloadClientIntegration>();

        services.AddScoped<IDiagnosticCheck, QbittorrentCategoryDiagnostic>();
    }
}
