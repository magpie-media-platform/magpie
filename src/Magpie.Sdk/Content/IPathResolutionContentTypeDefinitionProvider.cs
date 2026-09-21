using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Models;
using Magpie.Models.Data;

namespace Magpie.Sdk.Content;

/// <summary>
/// A content type definition provider that resolves the save path of a content file.
/// </summary>
public interface IPathResolutionContentTypeDefinitionProvider : IContentTypeDefinitionProvider
{
    /// <summary>
    /// Creates the save path.
    /// </summary>
    /// <param name="ancestors">The ancestor chain, from root to leaf</param>
    /// <param name="relationshipsByNode">The relationship titles keyed by hierarchy item id and kind</param>
    /// <param name="originalFileName">The file name without its path and extension</param>
    /// <param name="extension">The content file extension</param>
    /// <returns>The generated path, or <see langword="null"/> for the default path.</returns>
    string? BuildPath(
        IReadOnlyList<ContentHierarchyItem> ancestors,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> relationshipsByNode,
        string originalFileName,
        string extension);

    /// <summary>
    /// Creates the directories that the hierarchy levels own, and the file name, for one content file.
    /// </summary>
    /// <param name="ancestors">The ancestor chain, from root to leaf</param>
    /// <param name="relationshipsByNode">The relationship titles keyed by hierarchy item id and kind</param>
    /// <param name="originalFileName">The file name without its path and extension</param>
    /// <param name="extension">The content file extension</param>
    /// <returns>The plan, or <see langword="null"/> for the flat default.</returns>
    ContentPathPlan? BuildPathPlan(
        IReadOnlyList<ContentHierarchyItem> ancestors,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> relationshipsByNode,
        string originalFileName,
        string extension)
    {
        var relativePath = BuildPath(ancestors, relationshipsByNode, originalFileName, extension);
        if (string.IsNullOrWhiteSpace(relativePath) || ancestors.Count == 0)
        {
            return null;
        }

        var parts = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var fileName = parts[^1];
        var segments = parts[..^1];
        if (segments.Length == 0)
        {
            return new ContentPathPlan { Directories = [], FileName = fileName };
        }

        var levels = Definition.LevelDefinitions;
        var owners = ancestors
            .Where(item => levels.FirstOrDefault(
                level => string.Equals(level.Slug, item.Level, StringComparison.Ordinal))?.Subfolder is not null)
            .ToArray();
        if (owners.Length == 0)
        {
            return new ContentPathPlan
            {
                Directories =
                [
                    new ContentPathPlanDirectory
                    {
                        LevelSlug = ancestors[^1].Level,
                        Name = string.Join('/', segments)
                    }
                ],
                FileName = fileName
            };
        }

        if (segments.Length < owners.Length)
        {
            owners = owners[^segments.Length..];
        }

        var directories = new List<ContentPathPlanDirectory>(owners.Length);
        for (var index = 0; index < owners.Length; index++)
        {
            directories.Add(new ContentPathPlanDirectory
            {
                LevelSlug = owners[index].Level,
                Name = index == owners.Length - 1
                    ? string.Join('/', segments[index..])
                    : segments[index]
            });
        }

        return new ContentPathPlan { Directories = directories, FileName = fileName };
    }

    /// <summary>
    /// Names the hierarchy levels that own the directories above one leaf level, from the root folder down.
    /// </summary>
    /// <param name="leafLevelSlug">The hierarchy level slug of the file-owning leaf</param>
    /// <returns>The ancestor level slugs, from the outermost directory in, or <see langword="null"/> for an unknown level.</returns>
    IReadOnlyList<string>? ResolveDirectoryLevelChain(string leafLevelSlug)
    {
        var levels = Definition.LevelDefinitions;
        var current = levels.FirstOrDefault(
            level => string.Equals(level.Slug, leafLevelSlug, StringComparison.Ordinal));
        if (current is null)
        {
            return null;
        }

        var chain = new List<string>();

        var visited = new HashSet<string>(StringComparer.Ordinal) { leafLevelSlug };
        while (current.ParentSlug is { } parentSlug)
        {
            if (!visited.Add(parentSlug))
            {
                return null;
            }

            current = levels.FirstOrDefault(level => string.Equals(level.Slug, parentSlug, StringComparison.Ordinal));
            if (current is null)
            {
                break;
            }

            if (current.Subfolder is not null)
            {
                chain.Add(parentSlug);
            }
        }

        chain.Reverse();
        return chain;
    }
}
