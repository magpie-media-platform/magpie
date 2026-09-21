using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

namespace Magpie.Core.Diagnostics;

internal sealed class ContentDefaultsRepair(
    IRootFolderService rootFolderService,
    IQualityService qualityService,
    IContentTypeRegistry contentTypeRegistry) : IDiagnosticRepair
{
    public const string Id = "core.config.repair-content-defaults";

    public string RepairId => Id;

    public async Task<DiagnosticRepairResult> RunAsync(
        DiagnosticFinding finding,
        CancellationToken cancellationToken = default)
    {
        var target = finding.AffectedEntity;
        if (target is null ||
            !target.StartsWith(JobDiagnosticAffectedEntityPrefixes.RootFolder, StringComparison.Ordinal) ||
            !Guid.TryParse(target[JobDiagnosticAffectedEntityPrefixes.RootFolder.Length..], out var rootFolderId))
        {
            return DiagnosticRepairResult.Failed("The finding does not identify a root folder.");
        }

        var rootFolder = await rootFolderService.GetAsync(rootFolderId, cancellationToken).ConfigureAwait(false);
        if (rootFolder is null)
        {
            return DiagnosticRepairResult.Failed("The root folder no longer exists; re-run the doctor scan.");
        }

        var definition = contentTypeRegistry.GetDefinitionFor(rootFolder.ContentType);
        if (definition is null)
        {
            return DiagnosticRepairResult.Failed("The root folder content type is not registered.");
        }

        var tierLists = await qualityService.ListTierListsAsync(cancellationToken).ConfigureAwait(false);
        var defaults = new Dictionary<string, RootFolderDefault>(rootFolder.Defaults, StringComparer.Ordinal);
        var stale = defaults.Keys
            .Where(slug => !definition.LevelDefinitions.Any(level => level.Slug == slug))
            .ToList();
        foreach (var slug in stale)
        {
            defaults.Remove(slug);
        }

        var unresolved = defaults
            .Where(value => !tierLists.Any(list => list.Id == value.Value.QualityTierListId))
            .Select(value => value.Key)
            .Concat(definition.LevelDefinitions
                .Where(level => level.ParentSlug is null && !defaults.ContainsKey(level.Slug))
                .Select(level => level.Slug))
            .ToList();
        if (stale.Count == 0 && unresolved.Count == 0)
        {
            return DiagnosticRepairResult.Succeeded("The quality defaults are already valid.");
        }

        if (unresolved.Count > 0)
        {
            var tierList = tierLists
                .Where(list => list.QualityTierRanks.Count > 0)
                .OrderBy(list => list.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (tierList is null)
            {
                var tier = (await qualityService.ListTiersAsync(cancellationToken).ConfigureAwait(false))
                    .FirstOrDefault(value => string.Equals(value.Name, "Any", StringComparison.Ordinal))
                    ?? await qualityService.CreateTierAsync(
                        new QualityTier { Name = "Any", Criteria = new() },
                        actingUserId: null,
                        cancellationToken).ConfigureAwait(false);
                var tierListId = Guid.NewGuid();
                var created = await qualityService.CreateTierListAsync(
                    new QualityTierList
                    {
                        Id = tierListId,
                        Name = "Any",
                        UpgradeAllowed = true,
                        QualityTierRanks =
                        [
                            new QualityTierListRank
                            {
                                QualityTierListId = tierListId,
                                QualityTierId = tier.Id,
                                IsCutoff = true
                            }
                        ]
                    },
                    actingUserId: null,
                    cancellationToken).ConfigureAwait(false);
                if (created.TierList is null)
                {
                    return DiagnosticRepairResult.Failed(
                        created.ValidationError ?? "The Any tier list could not be created.");
                }

                tierList = created.TierList;
            }

            foreach (var slug in unresolved)
            {
                defaults[slug] = new RootFolderDefault { QualityTierListId = tierList.Id };
            }
        }

        var updated = await rootFolderService.PatchAsync(
            rootFolderId,
            new RootFolderPatch { Defaults = new() { Value = defaults } },
            actingUserId: null,
            cancellationToken).ConfigureAwait(false);
        return updated
            ? DiagnosticRepairResult.Succeeded($"Repaired the quality defaults for root folder {rootFolder.Name}.")
            : DiagnosticRepairResult.Failed("The root folder could not be updated.");
    }
}
