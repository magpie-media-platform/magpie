using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

namespace Magpie.Core.Diagnostics;

internal sealed class ContentDefaultsDiagnostic(
    IContentTypeRegistry contentTypeRegistry,
    IRootFolderRepository rootFolders,
    IQualityService qualityService) : IDiagnosticCheck
{
    public const string Id = "core.config.content-defaults";

    public string DiagnosticCheckId => Id;

    public string Category => "Content";

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken = default)
    {
        var folders = (await rootFolders.ListAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var tierLists = (await qualityService.ListTierListsAsync(cancellationToken).ConfigureAwait(false))
            .ToList();
        var contentTypes = contentTypeRegistry.Definitions.Select(definition => definition.Slug)
            .Concat(folders.Select(folder => folder.ContentType))
            .Where(contentType => !string.IsNullOrWhiteSpace(contentType))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(contentType => contentType, StringComparer.Ordinal);
        var findings = new List<DiagnosticFinding>();

        foreach (var contentType in contentTypes)
        {
            var matching = folders.Where(folder => folder.ContentType == contentType).ToList();
            var defaultCount = matching.Count == 1 ? 1 : matching.Count(folder => folder.IsDefault);
            if (defaultCount != 1)
            {
                findings.Add(new DiagnosticFinding
                {
                    DiagnosticCheckId = Id,
                    Severity = DiagnosticFindingSeverity.Error,
                    Title = defaultCount == 0
                        ? $"Content type {contentType} has no default root folder"
                        : $"Content type {contentType} has multiple default root folders",
                    Detail = $"Content type {contentType} requires one effective default root folder.",
                    AffectedEntity = $"ContentType:{contentType}"
                });
            }
        }

        var tierListsById = tierLists.ToDictionary(tierList => tierList.Id);
        foreach (var folder in folders.OrderBy(folder => folder.Name, StringComparer.Ordinal))
        {
            var definition = contentTypeRegistry.GetDefinitionFor(folder.ContentType);
            if (definition is null)
            {
                findings.Add(new DiagnosticFinding
                {
                    DiagnosticCheckId = Id,
                    Severity = DiagnosticFindingSeverity.Error,
                    Title = $"Root folder {folder.Name} uses an unknown content type",
                    Detail = $"Content type {folder.ContentType} is not registered, so quality defaults cannot " +
                        "resolve.",
                    AffectedEntity = $"{JobDiagnosticAffectedEntityPrefixes.RootFolder}{folder.Id}"
                });
                continue;
            }

            var levelsBySlug = definition.LevelDefinitions.ToDictionary(level => level.Slug, StringComparer.Ordinal);
            foreach (var value in folder.Defaults.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                if (!levelsBySlug.TryGetValue(value.Key, out var level))
                {
                    findings.Add(new DiagnosticFinding
                    {
                        DiagnosticCheckId = Id,
                        Severity = DiagnosticFindingSeverity.Error,
                        Title = $"Root folder {folder.Name} has a stale quality default",
                        Detail = $"Level {value.Key} is not defined for content type {folder.ContentType}.",
                        AffectedEntity = $"{JobDiagnosticAffectedEntityPrefixes.RootFolder}{folder.Id}",
                        RepairId = ContentDefaultsRepair.Id
                    });
                    continue;
                }

                if (value.Value.QualityTierListId is not { } qualityTierListId ||
                    !tierListsById.TryGetValue(qualityTierListId, out var tierList))
                {
                    findings.Add(new DiagnosticFinding
                    {
                        DiagnosticCheckId = Id,
                        Severity = DiagnosticFindingSeverity.Error,
                        Title = $"Root folder {folder.Name} references a missing quality tier list",
                        Detail = $"Level {level.DisplayName} ({level.Slug}) references missing quality tier list " +
                            $"{value.Value.QualityTierListId}.",
                        AffectedEntity = $"{JobDiagnosticAffectedEntityPrefixes.RootFolder}{folder.Id}",
                        RepairId = ContentDefaultsRepair.Id
                    });
                    continue;
                }
            }

            foreach (var rootLevel in definition.LevelDefinitions
                .Where(level => level.ParentSlug is null)
                .OrderBy(level => level.Slug, StringComparer.Ordinal))
            {
                if (!folder.Defaults.ContainsKey(rootLevel.Slug))
                {
                    findings.Add(new DiagnosticFinding
                    {
                        DiagnosticCheckId = Id,
                        Severity = DiagnosticFindingSeverity.Error,
                        Title = $"Root folder {folder.Name} has no quality default for {rootLevel.DisplayName}",
                        Detail = $"Root folder {folder.Name} has no default quality tier list for content type " +
                            $"{folder.ContentType} level {rootLevel.Slug}.",
                        AffectedEntity = $"{JobDiagnosticAffectedEntityPrefixes.RootFolder}{folder.Id}",
                        RepairId = ContentDefaultsRepair.Id
                    });
                }
            }
        }

        return findings;
    }
}
