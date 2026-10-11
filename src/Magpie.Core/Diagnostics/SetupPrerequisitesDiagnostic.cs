using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

namespace Magpie.Core.Diagnostics;

internal sealed class SetupPrerequisitesDiagnostic(
    IIndexerConnectionRepository indexerConnections,
    IRootFolderRepository rootFolders,
    IEnumerable<IDownloadClientIntegration> downloadClientIntegrations,
    ISettingsService settingsService) : IDiagnosticCheck
{
    public const string Id = "core.config.setup-prerequisites";

    public string DiagnosticCheckId => Id;

    public string Category => "Setup";

    private static DiagnosticFinding Error(string title, string detail)
    {
        return new()
        {
            DiagnosticCheckId = Id,
            Severity = DiagnosticFindingSeverity.Error,
            Title = title,
            Detail = detail,
        };
    }

    private static string NameOf(IDownloadClientIntegration integration)
    {
        return integration.GetType().FullName ?? integration.GetType().Name;
    }

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<DiagnosticFinding>();

        var instanceName = await settingsService.GetInstanceNameAsync(cancellationToken).ConfigureAwait(false);

        var connections = (await indexerConnections.ListAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        if (connections.Count == 0)
        {
            findings.Add(Error(
                "No indexers are configured",
                "No indexer connection exists, so searches return nothing."));
        }
        else if (!connections.Any(c => c.Enabled))
        {
            findings.Add(Error(
                "All indexers are disabled",
                "Every indexer connection is disabled, so searches return nothing."));
        }

        var clients = context.DownloadClients;
        if (clients.Count == 0)
        {
            findings.Add(Error(
                "No download client is configured",
                $"No download client is configured, so {instanceName} cannot send downloads."));
        }
        else if (!clients.Any(c => c.Enabled))
        {
            findings.Add(Error(
                "All download clients are disabled",
                $"Every download client is disabled, so {instanceName} cannot send downloads."));
        }

        AddDuplicateDownloadClientProviderFindings(findings, instanceName);

        var folders = await rootFolders.ListAllAsync(cancellationToken).ConfigureAwait(false);
        if (!folders.Any())
        {
            findings.Add(new DiagnosticFinding
            {
                DiagnosticCheckId = Id,
                Severity = DiagnosticFindingSeverity.Warning,
                Title = "No root folder is configured",
                Detail = "No root folder is configured, so imported files have nowhere to go.",
            });
        }

        return findings;
    }

    private void AddDuplicateDownloadClientProviderFindings(List<DiagnosticFinding> findings, string instanceName)
    {
        foreach (var group in downloadClientIntegrations
            .Where(i => !string.IsNullOrWhiteSpace(i.ProviderType))
            .GroupBy(i => i.ProviderType, StringComparer.OrdinalIgnoreCase))
        {
            var integrations = group.ToList();
            if (integrations.Count <= 1)
            {
                continue;
            }

            var winner = integrations[^1];
            var displaced = integrations.Take(integrations.Count - 1);
            findings.Add(new DiagnosticFinding
            {
                DiagnosticCheckId = Id,
                Severity = DiagnosticFindingSeverity.Warning,
                Title = $"Multiple download client integrations use \"{group.Key}\"",
                Detail =
                    $"{instanceName} will use {NameOf(winner)} for \"{group.Key}\" and ignore " +
                    $"{string.Join(", ", displaced.Select(NameOf))}.",
            });
        }
    }
}
