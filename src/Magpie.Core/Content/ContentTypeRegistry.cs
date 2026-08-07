using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Models;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Content;

internal sealed class ContentTypeRegistry : IContentTypeRegistry
{
    private readonly Dictionary<string, ContentTypeDefinition> _bySlug;
    private readonly ILogger<ContentTypeRegistry> _logger;
    private readonly IReadOnlyList<IContentTypeDefinitionProvider> _providers;

    public ContentTypeRegistry(
        IEnumerable<IContentTypeDefinitionProvider> providers,
        ILogger<ContentTypeRegistry> logger)
    {
        _logger = logger;
        var registeredProviders = new List<IContentTypeDefinitionProvider>();
        var contributions = new List<(string Slug, ContentTypeDefinition Definition, Type ProviderType)>();
        foreach (var provider in providers)
        {
            ContentTypeDefinition definition;
            try
            {
                definition = provider.Definition;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The content type definition provider {Provider} failed to read its definition",
                    provider.GetType().FullName);
                continue;
            }

            if (definition is null || string.IsNullOrWhiteSpace(definition.Slug))
            {
                continue;
            }

            contributions.Add((definition.Slug, definition, provider.GetType()));
            registeredProviders.Add(provider);
        }

        _providers = registeredProviders;
        var winners = BuildProviderMap(contributions, logger);
        _bySlug = winners.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Definition);

        Definitions = [.. _bySlug.Values];
    }

    public IReadOnlyList<ContentTypeDefinition> Definitions { get; }

    private static Dictionary<string, (string Slug, ContentTypeDefinition Definition, Type ProviderType)>
        BuildProviderMap(
            IEnumerable<(string Slug, ContentTypeDefinition Definition, Type ProviderType)> contributions,
            ILogger logger)
    {
        var map = new Dictionary<string, (string Slug, ContentTypeDefinition Definition, Type ProviderType)>();
        foreach (var group in contributions
            .Where(contribution => !string.IsNullOrWhiteSpace(contribution.Slug))
            .GroupBy(contribution => contribution.Slug))
        {
            var members = group.ToList();
            var winner = members[^1];
            if (members.Count > 1)
            {
                logger.LogWarning(
                    "Multiple {ItemLabel} declared {KeyLabel} {Key}. The registry used {Winner} and " +
                    "ignored {Displaced}",
                    "content type definition providers",
                    "slug",
                    group.Key,
                    winner.ProviderType.FullName,
                    string.Join(", ", members.Take(members.Count - 1).Select(member => member.ProviderType.FullName)));
            }

            map[group.Key] = winner;
        }

        return map;
    }

    public ContentTypeDefinition? GetDefinitionFor(string levelSlug)
    {
        if (string.IsNullOrWhiteSpace(levelSlug))
        {
            return null;
        }

        return _bySlug.TryGetValue(levelSlug, out var definition) ? definition : null;
    }

    public IContentTypeDefinitionProvider? FindProviderFor(string contentType)
    {
        foreach (var provider in _providers)
        {
            if (provider.Definition.Slug == contentType)
            {
                return provider;
            }
        }

        return null;
    }

    public string GetRootLevelSlug(string contentType, string? levelSlug)
    {
        var rootLevels = GetDefinedRootLevels(contentType);
        if (rootLevels.Count == 0)
        {
            var fallbackSlug = string.IsNullOrWhiteSpace(levelSlug)
                ? contentType
                : levelSlug;
            _logger.LogWarning(
                "Content type {ContentType} has no definition. The registry will use the slug-only root level " +
                "{LevelSlug}",
                contentType,
                fallbackSlug);
            return fallbackSlug;
        }

        if (!string.IsNullOrWhiteSpace(levelSlug))
        {
            var exact = rootLevels.FirstOrDefault(level => level.Slug == levelSlug);
            if (exact is not null)
            {
                return exact.Slug;
            }

            throw new ArgumentException(
                $"Level {levelSlug} is not a root level of content type {contentType}; " +
                $"valid root levels are: {string.Join(", ", rootLevels.Select(level => level.Slug))}.",
                nameof(levelSlug));
        }

        return rootLevels.Count > 1
            ? throw new ArgumentException(
                $"Content type {contentType} defines multiple root levels " +
                $"({string.Join(", ", rootLevels.Select(level => level.Slug))}); " +
                "a target levelSlug must be supplied.",
                nameof(levelSlug))
            : rootLevels[0].Slug;
    }

    public IReadOnlyList<ContentHierarchyLevelDefinition> GetRootLevels(string contentType)
    {
        var roots = GetDefinedRootLevels(contentType);
        if (roots.Count > 0)
        {
            return roots;
        }

        return
        [
            new ContentHierarchyLevelDefinition
            {
                Slug = contentType,
                ParentSlug = null,
                DisplayName = contentType,
                CustomMetadataFields = []
            }
        ];
    }

    public IReadOnlyList<ContentHierarchyLevelDefinition> GetChildLevels(string contentType, string levelSlug)
    {
        var levels = GetDefinedLevels(contentType);
        EnsureUniqueSlugs(levels);
        return [.. levels.Where(level => level.ParentSlug is { } parent && parent == levelSlug)];
    }

    public bool Exists(string levelSlug) => !string.IsNullOrWhiteSpace(levelSlug) && _bySlug.ContainsKey(levelSlug);

    private static void EnsureUniqueSlugs(IReadOnlyList<ContentHierarchyLevelDefinition> levels)
    {
        _ = BuildBySlug(levels);
    }

    private static (
        Dictionary<string, ContentHierarchyLevelDefinition> BySlug,
        HashSet<string> ReferencedAsParent) BuildLookup(IReadOnlyList<ContentHierarchyLevelDefinition> levels)
    {
        var bySlug = BuildBySlug(levels);
        var referencedAsParent = (
            from level in levels
            where level.ParentSlug is not null
            select level.ParentSlug!).ToHashSet();
        return (bySlug, referencedAsParent);
    }

    private static Dictionary<string, ContentHierarchyLevelDefinition> BuildBySlug(
        IReadOnlyList<ContentHierarchyLevelDefinition> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);

        var bySlug = new Dictionary<string, ContentHierarchyLevelDefinition>();
        foreach (var level in levels)
        {
            if (!bySlug.TryAdd(level.Slug, level))
            {
                throw new InvalidOperationException(
                    $"Duplicate level slug {level.Slug} in content type hierarchy definition.");
            }
        }

        return bySlug;
    }

    private IReadOnlyList<ContentHierarchyLevelDefinition> GetDefinedLevels(string contentType)
    {
        return GetDefinitionFor(contentType)?.LevelDefinitions ?? [];
    }

    private List<ContentHierarchyLevelDefinition> GetDefinedRootLevels(string contentType)
    {
        return [.. GetDefinedLevels(contentType).Where(level => level.ParentSlug is null)];
    }
}
