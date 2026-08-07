using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Cardigann;

internal sealed class CardigannDuplicateFeedRepair(
    IIndexerService indexers,
    ILogger<CardigannDuplicateFeedRepair> logger) : IDiagnosticRepair
{
    public const string Id = "cardigann.config.dedup-feed";

    public string RepairId => Id;

    public async Task<DiagnosticRepairResult> RunAsync(
        DiagnosticFinding finding,
        CancellationToken cancellationToken = default)
    {
        var feeds = await indexers.ListFeedsAsync(cancellationToken).ConfigureAwait(false);
        var communityFeeds = (
            from feed in feeds
            where string.Equals(
                feed.ProviderType,
                CardigannFeedProvider.ProviderTypeName,
                StringComparison.OrdinalIgnoreCase)
            let sourceUrl = feed.Settings.TryGetValue(CardigannFeedProvider.GitUrlSettingKey, out var value) &&
                !string.IsNullOrWhiteSpace(value)
                    ? value.Trim()
                    : CardigannFeedProvider.DefaultGitUrl
            where string.Equals(
                sourceUrl,
                CardigannFeedProvider.DefaultGitUrl,
                StringComparison.OrdinalIgnoreCase)
            select feed).ToList();
        if (communityFeeds.Count < 2)
        {
            return DiagnosticRepairResult.Succeeded("No duplicate Cardigann feeds remain; nothing to collapse.");
        }

        var canonical = (
            from feed in communityFeeds
            orderby
                feed.Enabled descending,
                string.Equals(
                    feed.Name,
                    CardigannFeedProvider.DefaultFeedName,
                    StringComparison.Ordinal) descending,
                feed.CreatedAt,
                feed.Id
            select feed).First();

        var removed = 0;
        foreach (var extra in communityFeeds.Where(feed => feed.Id != canonical.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await indexers.DeleteFeedAsync(extra.Id, actingUserId: null, cancellationToken).ConfigureAwait(false))
            {
                removed++;
            }
        }

        logger.LogInformation(
            "Cardigann repair removed {Removed} duplicate community feed(s) and kept feed {FeedId} {FeedName}",
            removed,
            canonical.Id,
            canonical.Name);

        if (removed == 0)
        {
            return DiagnosticRepairResult.Succeeded("No duplicate Cardigann feeds remain; nothing to collapse.");
        }

        return DiagnosticRepairResult.Succeeded(
            $"Collapsed {removed} duplicate Cardigann community feed(s); kept {canonical.Name}.");
    }
}
