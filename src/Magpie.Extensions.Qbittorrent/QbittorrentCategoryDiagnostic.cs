using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

namespace Magpie.Extensions.Qbittorrent;

internal sealed class QbittorrentCategoryDiagnostic(ISettingsService settingsService) : IDiagnosticCheck
{
    public const string Id = "qbittorrent.config.category-missing";

    public string DiagnosticCheckId => Id;

    public string Category => "Download Clients";

    private static bool HasSetting(IReadOnlyDictionary<string, string> settings, string key)
    {
        foreach (var pair in settings)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(pair.Value))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        var instanceName = await settingsService.GetInstanceNameAsync(cancellationToken).ConfigureAwait(false);

        var findings = new List<DiagnosticFinding>();
        foreach (var client in context.DownloadClients)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!client.Enabled ||
                !string.Equals(
                    client.ProviderType,
                    Constants.ProviderTypeId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (HasSetting(client.Settings, QbittorrentSettings.CategoryKey))
            {
                continue;
            }

            findings.Add(new DiagnosticFinding
            {
                DiagnosticCheckId = Id,
                Severity = DiagnosticFindingSeverity.Info,
                Title = $"qBittorrent client {client.Name} has no category configured",
                Detail = $"The qBittorrent client \"{client.Name}\" has no category. " +
                    $"A category keeps {instanceName} downloads separate for filtering and cleanup.",
                AffectedEntity = $"DownloadClient:{client.Id}",
            });
        }

        return findings;
    }
}
