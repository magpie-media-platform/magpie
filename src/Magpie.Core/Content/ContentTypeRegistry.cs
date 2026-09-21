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
    private readonly Dictionary<string, Contribution> _bySlug;
    private readonly ILogger<ContentTypeRegistry> _logger;

    public ContentTypeRegistry(
        IEnumerable<IContentTypeDefinitionProvider> providers,
        ILogger<ContentTypeRegistry> logger)
    {
        _logger = logger;
        var contributions = new List<Contribution>();
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

            var slugs = definition.LevelDefinitions.Select(level => level.Slug).Prepend(definition.Slug);
            if (slugs.FirstOrDefault(slug => slug != slug.ToLowerInvariant()) is { } mixedCase)
            {
                logger.LogError(
                    "The content type registry ignored the definition from provider {Provider} because " +
                    "slug {Slug} is not lowercase",
                    provider.GetType().FullName,
                    mixedCase);
                continue;
            }

            contributions.Add(new Contribution(definition.Slug, definition, provider));
        }

        _bySlug = BuildProviderMap(contributions, logger);

        Definitions = [.. _bySlug.Values.Select(contribution => contribution.Definition)];
    }

    public IReadOnlyList<ContentTypeDefinition> Definitions { get; }

    private static Dictionary<string, Contribution> BuildProviderMap(
        IEnumerable<Contribution> contributions,
        ILogger logger)
    {
        var map = new Dictionary<string, Contribution>(StringComparer.Ordinal);
        foreach (var group in contributions
            .Where(contribution => !string.IsNullOrWhiteSpace(contribution.Slug))
            .GroupBy(contribution => contribution.Slug, StringComparer.Ordinal))
        {
            var members = group.ToList();
            var winner = members[^1];
            if (members.Count > 1)
            {
                logger.LogWarning(
                    "Multiple content type definition providers declared slug {Slug}; the registry used " +
                    "{Winner} and ignored {Displaced}",
                    group.Key,
                    winner.Provider.GetType().FullName,
                    string.Join(
                        ", ",
                        members.Take(members.Count - 1).Select(member => member.Provider.GetType().FullName)));
            }

            map[group.Key] = winner;
        }

        return map;
    }

    public ContentTypeDefinition? GetDefinitionFor(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        return _bySlug.TryGetValue(contentType, out var contribution) ? contribution.Definition : null;
    }

    public IContentTypeDefinitionProvider? FindProviderFor(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        return _bySlug.TryGetValue(contentType, out var contribution) ? contribution.Provider : null;
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
                "Content type {ContentType} has no definition; using slug-only root level {LevelSlug}",
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

    public bool Exists(string contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && _bySlug.ContainsKey(contentType);

    private static void EnsureUniqueSlugs(IReadOnlyList<ContentHierarchyLevelDefinition> levels)
    {
        _ = BuildBySlug(levels);
    }

    private static Dictionary<string, ContentHierarchyLevelDefinition> BuildBySlug(
        IReadOnlyList<ContentHierarchyLevelDefinition> levels)
    {
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

    private readonly record struct Contribution(
        string Slug,
        ContentTypeDefinition Definition,
        IContentTypeDefinitionProvider Provider);
}
