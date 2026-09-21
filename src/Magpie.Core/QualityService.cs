using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

namespace Magpie.Core;

internal sealed class QualityService(
    IQualityTierListRepository tierListRepository,
    IQualityTierRepository tierRepository,
    IRootFolderRepository rootFolderRepository,
    IContentTypeRegistry contentTypeRegistry,
    IFileSystem fileSystem) : IQualityService
{
    private static bool IsMimeToken(string value) =>
        value.Length > 0 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '!' or '#' or '$' or '%' or '&' or '\'' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or
                '~');

    private static bool TryNormalizeMimePattern(string? pattern, out string normalized)
    {
        normalized = pattern?.Trim().ToLowerInvariant() ?? string.Empty;
        var separator = normalized.IndexOf('/');
        if (separator <= 0 || separator != normalized.LastIndexOf('/') || separator == normalized.Length - 1)
        {
            return false;
        }

        var type = normalized[..separator];
        var subtype = normalized[(separator + 1)..];
        return IsMimeToken(type) && (subtype == "*" || IsMimeToken(subtype));
    }

    private static (List<string>? Patterns, string? Error) NormalizeMimePatterns(IEnumerable<string>? patterns)
    {
        if (patterns is null)
        {
            return (null, "MIME patterns are required.");
        }

        var normalizedPatterns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pattern in patterns)
        {
            if (!TryNormalizeMimePattern(pattern, out var normalized))
            {
                return (null, $"{pattern} is not a valid MIME pattern.");
            }

            if (seen.Add(normalized))
            {
                normalizedPatterns.Add(normalized);
            }
        }

        return (normalizedPatterns, null);
    }

    private bool MimePatternsIntersect(IReadOnlyList<string> first, IReadOnlyList<string> second) =>
        second.Any(mimeType => fileSystem.MatchesAnyFileType(mimeType, mimeType, first)) ||
        first.Any(mimeType => fileSystem.MatchesAnyFileType(mimeType, mimeType, second));

    private async Task<IReadOnlyList<string>> ResolveLevelMimeTypesAsync(
        ContentHierarchyLevelDefinition level,
        CancellationToken cancellationToken)
    {
        var mimeTypes = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var acceptedFileType in level.AcceptedFileTypes)
        {
            if (TryNormalizeMimePattern(acceptedFileType, out var pattern))
            {
                if (seen.Add(pattern))
                {
                    mimeTypes.Add(pattern);
                }

                continue;
            }

            var resolved = await fileSystem.LookupMimeTypeAsync(acceptedFileType, cancellationToken)
                .ConfigureAwait(false);
            if (TryNormalizeMimePattern(resolved, out pattern) && seen.Add(pattern))
            {
                mimeTypes.Add(pattern);
            }
        }

        return mimeTypes;
    }

    private async Task<IReadOnlyList<string>> ResolveTargetMimeTypesAsync(
        string contentType,
        string? levelSlug,
        CancellationToken cancellationToken)
    {
        var definition = contentTypeRegistry.GetDefinitionFor(contentType);
        if (definition is null)
        {
            throw new ArgumentException($"Content type {contentType} is not registered.", nameof(contentType));
        }

        var levels = definition.LevelDefinitions;
        if (levelSlug is not null)
        {
            var target = definition.LevelDefinitions.FirstOrDefault(level =>
                string.Equals(level.Slug, levelSlug, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                throw new ArgumentException(
                    $"Level {levelSlug} is not defined for content type {contentType}.",
                    nameof(levelSlug));
            }

            var descendantSlugs = new HashSet<string>(StringComparer.Ordinal) { target.Slug };
            var descendants = new List<ContentHierarchyLevelDefinition>();
            foreach (var level in definition.LevelDefinitions)
            {
                if (!descendantSlugs.Contains(level.Slug) &&
                    (level.ParentSlug is null || !descendantSlugs.Contains(level.ParentSlug)))
                {
                    continue;
                }

                descendantSlugs.Add(level.Slug);
                descendants.Add(level);
            }

            levels = descendants;
        }

        var targetMimeTypes = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var level in levels)
        {
            foreach (var mimeType in await ResolveLevelMimeTypesAsync(level, cancellationToken).ConfigureAwait(false))
            {
                if (seen.Add(mimeType))
                {
                    targetMimeTypes.Add(mimeType);
                }
            }
        }

        return targetMimeTypes;
    }

    private async Task<string?> ValidateReferencedDefaultsAsync(
        Guid qualityTierListId,
        IReadOnlyList<string> mimeTypes,
        CancellationToken cancellationToken)
    {
        var rootFolders = await rootFolderRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var rootFolder in rootFolders)
        {
            foreach (var levelDefault in rootFolder.Defaults
                .Where(value => value.Value.QualityTierListId == qualityTierListId))
            {
                IReadOnlyList<string> acceptedMimeTypes;
                try
                {
                    acceptedMimeTypes = await ResolveTargetMimeTypesAsync(
                        rootFolder.ContentType,
                        levelDefault.Key,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (ArgumentException)
                {
                    return $"Quality tier list {qualityTierListId} is referenced by stale level " +
                        $"{levelDefault.Key} on root folder {rootFolder.Name}.";
                }

                if (mimeTypes.Count == 0 || MimePatternsIntersect(mimeTypes, acceptedMimeTypes))
                {
                    continue;
                }

                var formattedMimeTypes = string.Join(", ", mimeTypes);
                var accepted = acceptedMimeTypes.Count == 0
                    ? "no MIME types"
                    : string.Join(", ", acceptedMimeTypes);
                return $"Quality tier list {qualityTierListId} cannot use MIME patterns {formattedMimeTypes} " +
                    $"because root folder {rootFolder.Name} level {levelDefault.Key} accepts {accepted}.";
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<QualityTierList>> ListTierListsAsync(CancellationToken cancellationToken = default)
    {
        var all = await tierListRepository.ListAllAsync(cancellationToken).ConfigureAwait(false);
        return [.. all];
    }

    public Task<QualityTierList?> GetTierListAsync(Guid tierListId, CancellationToken cancellationToken = default) =>
        tierListRepository.GetByIdAsync(tierListId, cancellationToken);

    public async Task<QualityTierListCreateResult> CreateTierListAsync(
        QualityTierList tierList,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var normalization = NormalizeMimePatterns(tierList.MimeTypes);
        if (normalization.Error is not null)
        {
            return new() { ValidationError = normalization.Error };
        }

        tierList = tierList with
        {
            MimeTypes = normalization.Patterns!
        };
        var created = await tierListRepository.CreateAsync(tierList, actingUserId, cancellationToken)
            .ConfigureAwait(false);
        return new() { TierList = created };
    }

    public async Task<QualityTierListPatchResult> PatchTierListAsync(
        Guid tierListId,
        QualityTierListPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (patch.MimeTypes is not null)
        {
            var normalization = NormalizeMimePatterns(patch.MimeTypes.Value);
            if (normalization.Error is not null)
            {
                return new() { ValidationError = normalization.Error };
            }

            patch = patch with
            {
                MimeTypes = new()
                {
                    Value = normalization.Patterns!
                }
            };

            var conflict = await ValidateReferencedDefaultsAsync(
                tierListId,
                patch.MimeTypes.Value,
                cancellationToken).ConfigureAwait(false);
            if (conflict is not null)
            {
                return new() { ValidationError = conflict };
            }
        }

        var updated = await tierListRepository
            .PatchAsync(patch with { Id = tierListId }, actingUserId, cancellationToken)
            .ConfigureAwait(false);
        return new() { Updated = updated };
    }

    public async Task<bool> DeleteTierListAsync(
        Guid tierListId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return await tierListRepository.DeleteAsync(tierListId, actingUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<QualityTier>> ListTiersAsync(CancellationToken cancellationToken = default) =>
        [.. await tierRepository.ListAllAsync(cancellationToken).ConfigureAwait(false)];

    public Task<QualityTier?> GetTierAsync(Guid id, CancellationToken cancellationToken = default) =>
        tierRepository.GetByIdAsync(id, cancellationToken);

    public Task<QualityTier> CreateTierAsync(
        QualityTier tier,
        Guid? actingUserId,
        CancellationToken cancellationToken = default) =>
        tierRepository.CreateAsync(tier, actingUserId, cancellationToken);

    public Task<bool> PatchTierAsync(
        Guid id,
        QualityTierPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default) =>
        tierRepository.PatchAsync(patch with { Id = id }, actingUserId, cancellationToken);

    public Task<bool> DeleteTierAsync(Guid id, Guid? actingUserId, CancellationToken cancellationToken = default) =>
        tierRepository.DeleteAsync(id, actingUserId, cancellationToken);
}
