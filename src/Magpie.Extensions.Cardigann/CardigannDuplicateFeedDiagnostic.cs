using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;

namespace Magpie.Extensions.Cardigann;

internal sealed class CardigannDuplicateFeedDiagnostic : IDiagnosticCheck
{
    public const string Id = "cardigann.config.duplicate-feed";

    public string DiagnosticCheckId => Id;

    public string Category => "Indexers";

    public Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var groups = context.IndexerFeeds
            .Where(f => string.Equals(
                f.ProviderType,
                Constants.ProviderTypeName,
                StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                feed => feed.Settings.TryGetValue(Constants.GitUrlSettingKey, out var value) &&
                    !string.IsNullOrWhiteSpace(value)
                        ? value.Trim()
                        : Constants.DefaultGitUrl,
                StringComparer.OrdinalIgnoreCase);

        var findings = new List<DiagnosticFinding>();
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var feedsInGroup = group.ToList();
            if (feedsInGroup.Count < 2)
            {
                continue;
            }

            var names = string.Join(", ", feedsInGroup.Select(f => f.Name));
            var isCommunitySource = string.Equals(
                group.Key,
                Constants.DefaultGitUrl,
                StringComparison.OrdinalIgnoreCase);
            findings.Add(new DiagnosticFinding
            {
                DiagnosticCheckId = Id,
                Severity = DiagnosticFindingSeverity.Warning,
                Title = "Multiple Cardigann feeds point at the same source",
                Detail =
                    $"{feedsInGroup.Count} Cardigann feeds ({names}) all resolve to {group.Key}, so they " +
                    "sync the same indexer definitions repeatedly.",
                RepairId = isCommunitySource ? Constants.DuplicateFeedRepairId : null,
            });
        }

        return Task.FromResult<IReadOnlyList<DiagnosticFinding>>(findings);
    }
}
