using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Patch;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Content;

internal sealed class ContentFileService(
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IMetadataContainerRepository metadataContainerRepository,
    IRootFolderRepository rootFolderRepository,
    IContentTypeRegistry contentTypeRegistry,
    IFileSystem fileSystem,
    IMediaFileRepository mediaFileRepository,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    TimeProvider timeProvider,
    ILogger<ContentFileService> logger) : IContentFileService
{
    private static readonly char[] InvalidPathChars =
    [
        '<', '>', ':', '"', '|', '?', '*',
        '\0',
        '\x01', '\x02', '\x03', '\x04', '\x05', '\x06', '\x07',
        '\x08', '\x09', '\x0A', '\x0B', '\x0C', '\x0D', '\x0E', '\x0F',
        '\x10', '\x11', '\x12', '\x13', '\x14', '\x15', '\x16', '\x17',
        '\x18', '\x19', '\x1A', '\x1B', '\x1C', '\x1D', '\x1E', '\x1F'
    ];

    private const int MaxSegmentLength = 255;
    private const char SanitizationReplacement = '_';

    public async Task<ContentRename> PreviewRenameAsync(
        Guid contentHierarchyItemId,
        FileInfo original,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);

        var lineage = await contentHierarchyItemRepository
            .GetLineageAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (lineage.Count == 0)
        {
            throw new InvalidOperationException("The content hierarchy item does not exist.");
        }

        var leaf = lineage[0];
        var relationshipsByNode = await metadataContainerRepository
            .GetRelationshipTitlesByContentHierarchyItemAsync([.. lineage.Select(item => item.Id)], cancellationToken)
            .ConfigureAwait(false);
        var pathProvider = contentTypeRegistry.FindProviderFor(leaf.ContentType) as
            IPathResolutionContentTypeDefinitionProvider;
        var rootFolder = await rootFolderRepository
            .GetByIdAsync(leaf.EffectiveRootFolderId, cancellationToken)
            .ConfigureAwait(false);
        if (rootFolder is null)
        {
            throw new InvalidOperationException("The root folder does not exist.");
        }

        var pathResult = BuildRelativePaths(
            lineage,
            relationshipsByNode,
            pathProvider,
            rootFolder.RenameTemplate,
            [original.Name],
            logger)[0];
        var pathUpdates = BuildPathUpdates(lineage, pathResult, out var hasOwnedDirectory);
        var root = lineage[^1];
        var subfolder = contentTypeRegistry.GetDefinitionFor(root.ContentType)?.LevelDefinitions
            .FirstOrDefault(level => level.Slug == root.Level)?.Subfolder;
        var effectivePath = fileSystem.GetFullPath(string.IsNullOrWhiteSpace(subfolder)
            ? rootFolder.Path.Trim()
            : Path.Combine(rootFolder.Path.Trim(), subfolder));
        for (var index = lineage.Count - 1; index >= 0; index--)
        {
            var item = lineage[index];
            var rawPath = pathUpdates.TryGetValue(item.Id, out var updatedPath) ? updatedPath : item.Path;
            if (rawPath is null)
            {
                continue;
            }

            var normalizedPath = rawPath.Trim().Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            effectivePath = Path.IsPathFullyQualified(normalizedPath)
                ? fileSystem.GetFullPath(normalizedPath)
                : fileSystem.GetFullPath(Path.Combine(effectivePath, normalizedPath));
        }

        if (string.IsNullOrWhiteSpace(effectivePath))
        {
            throw new ArgumentException("The content path could not be resolved.");
        }

        var relativeFilePath = hasOwnedDirectory
            ? Path.GetFileName(pathResult)
            : pathResult;
        var targetPath = Path.Combine(effectivePath, relativeFilePath);
        if (!lineage.Any(item => item.Path is not null && Path.IsPathFullyQualified(
            item.Path.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar))))
        {
            EnsureTargetWithinRoot(targetPath, rootFolder);
        }

        return new ContentRename
        {
            Original = original,
            Rename = new FileInfo(targetPath),
            Item = leaf
        };
    }

    public async Task<MediaFile> ExecuteRenameAsync(
        ContentRename rename,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rename);

        if (!fileSystem.FileExists(rename.Original.FullName))
        {
            throw new InvalidOperationException("Rename source file does not exist");
        }

        if (!fileSystem.PathsEqual(rename.Rename.FullName, rename.Original.FullName) &&
            fileSystem.FileExists(rename.Rename.FullName))
        {
            throw new InvalidOperationException("Rename target file already exists");
        }

        var freshRename = await PreviewRenameAsync(rename.Item.Id, rename.Original, cancellationToken)
            .ConfigureAwait(false);
        if (!fileSystem.PathsEqual(freshRename.Rename.FullName, rename.Rename.FullName))
        {
            throw new InvalidOperationException("Rename preview is stale");
        }

        var samePath = fileSystem.PathsEqual(freshRename.Rename.FullName, rename.Original.FullName);

        if (!samePath && fileSystem.FileExists(rename.Rename.FullName))
        {
            throw new InvalidOperationException("Rename target file already exists");
        }

        if (!samePath)
        {
            var destinationDirectory = Path.GetDirectoryName(rename.Rename.FullName);
            if (!string.IsNullOrEmpty(destinationDirectory) && !fileSystem.DirectoryExists(destinationDirectory))
            {
                fileSystem.CreateDirectory(destinationDirectory);
            }

            fileSystem.Move(rename.Original.FullName, rename.Rename.FullName);
        }

        try
        {
            return await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var mediaFile = await GetMediaFileByPathAsync(rename.Original.FullName, ct)
                        .ConfigureAwait(false);
                    var now = timeProvider.GetUtcNow().UtcDateTime;
                    if (!samePath)
                    {
                        await mediaFileRepository
                            .UpdateAsync(
                                mediaFile with { FilePath = rename.Rename.FullName, ModifiedAt = now },
                                userId,
                                ct)
                            .ConfigureAwait(false);
                        await auditRecordRepository.CreateAsync(
                            new()
                            {
                                Category = AuditCategories.Content,
                                Operation = "Rename",
                                EntityId = mediaFile.Id.ToString(),
                                EntityType = nameof(MediaFile),
                                Detail = $"Renamed {rename.Original.FullName} -> {rename.Rename.FullName}",
                                Timestamp = now,
                                UserId = userId,
                                CorrelationId = correlationId
                            },
                            ct).ConfigureAwait(false);
                    }

                    return mediaFile with
                    {
                        FilePath = samePath ? mediaFile.FilePath : rename.Rename.FullName,
                        ModifiedAt = samePath ? mediaFile.ModifiedAt : now,
                        ModifiedByUserId = samePath ? mediaFile.ModifiedByUserId : userId
                    };
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The rename engine failed to persist the rename of media file {OriginalPath}. " +
                "The engine will revert {NewPath} to {OldPath}",
                rename.Original.FullName,
                rename.Rename.FullName,
                rename.Original.FullName);

            try
            {
                fileSystem.Move(rename.Rename.FullName, rename.Original.FullName);
            }
            catch (Exception revertEx)
            {
                logger.LogError(
                    revertEx,
                    "The revert of the media file rename from {NewPath} to {OldPath} failed. " +
                    "An operator must reconcile the files manually",
                    rename.Rename.FullName,
                    rename.Original.FullName);
            }

            throw;
        }
    }

    public async Task<bool> ChangePathAsync(
        Guid contentHierarchyItemId,
        ContentHierarchyItemPatch patch,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var requestedPath = patch.Path!.Value;
        var plan = await BuildPathChangePlanAsync(
            contentHierarchyItemId,
            requestedPath,
            cancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            return false;
        }

        if (!plan.CanMove)
        {
            var reasons = plan.Conflicts.Concat(plan.InaccessiblePaths);
            throw new ArgumentException(
                "The path change cannot be applied: " + string.Join("; ", reasons),
                nameof(patch));
        }

        var completedMoves = MovePathFiles(plan.FileMoves, cancellationToken);

        try
        {
            await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    var currentItem = await contentHierarchyItemRepository
                        .GetByIdAsync(contentHierarchyItemId, ct)
                        .ConfigureAwait(false);
                    if (currentItem is null)
                    {
                        throw new KeyNotFoundException("The content hierarchy item no longer exists.");
                    }

                    if (!string.Equals(currentItem.Path, plan.OriginalPath, StringComparison.Ordinal) ||
                        currentItem.ModifiedAt != plan.OriginalModifiedAt)
                    {
                        throw new InvalidOperationException("The content path changed after it was previewed.");
                    }

                    foreach (var version in plan.DescendantVersions)
                    {
                        var currentDescendant = await contentHierarchyItemRepository
                            .GetByIdAsync(version.ContentHierarchyItemId, ct)
                            .ConfigureAwait(false);
                        if (currentDescendant is null)
                        {
                            throw new KeyNotFoundException(
                                $"Descendant content hierarchy item {version.ContentHierarchyItemId} " +
                                "no longer exists.");
                        }

                        if (!string.Equals(
                            currentDescendant.Path,
                            version.OriginalPath,
                            StringComparison.Ordinal) ||
                            currentDescendant.ModifiedAt != version.OriginalModifiedAt)
                        {
                            throw new InvalidOperationException(
                                $"The path of descendant content hierarchy item {version.ContentHierarchyItemId} " +
                                "changed after it was previewed.");
                        }
                    }

                    var applied = await contentHierarchyItemRepository
                        .PatchAsync(
                            patch with
                            {
                                Id = contentHierarchyItemId,
                                Path = new ScalarPatch<string?> { Value = plan.NormalizedPath }
                            },
                            actingUserId,
                            ct)
                        .ConfigureAwait(false);
                    if (!applied)
                    {
                        throw new KeyNotFoundException("The content hierarchy item no longer exists.");
                    }

                    var now = timeProvider.GetUtcNow().UtcDateTime;
                    foreach (var move in plan.FileMoves)
                    {
                        var currentMediaFile = await mediaFileRepository
                            .GetByIdAsync(move.MediaFileId, ct)
                            .ConfigureAwait(false);
                        if (currentMediaFile is null ||
                            !fileSystem.PathsEqual(currentMediaFile.FilePath, move.SourcePath))
                        {
                            throw new InvalidOperationException(
                                $"Media file {move.MediaFileId} changed after the path move was previewed.");
                        }

                        await mediaFileRepository
                            .UpdateAsync(
                                currentMediaFile with { FilePath = move.DestinationPath, ModifiedAt = now },
                                actingUserId,
                                ct)
                            .ConfigureAwait(false);
                    }

                    await auditRecordRepository.CreateAsync(
                        new AuditRecord
                        {
                            Category = AuditCategories.Content,
                            Operation = "PathChange",
                            EntityId = contentHierarchyItemId.ToString(),
                            EntityType = nameof(ContentHierarchyItem),
                            Detail = $"Changed the content path and moved {plan.FileMoves.Count} media files.",
                            Timestamp = now,
                            UserId = actingUserId
                        },
                        ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The content path change for {ContentHierarchyItemId} failed after moving {FileCount} files. " +
                "The service will restore the files",
                contentHierarchyItemId,
                completedMoves.Count);
            RevertPathMoves(completedMoves);
            throw;
        }

        return true;
    }

    public Task DeleteFilesAsync(
        IReadOnlyCollection<ContentHierarchyItem> items,
        CancellationToken cancellationToken = default)
    {
        void DeleteFiles(IEnumerable<MediaFile> files)
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!fileSystem.FileExists(file.FilePath))
                {
                    logger.LogWarning(
                        "Media file {MediaFileId} is missing from {FilePath}",
                        file.Id,
                        file.FilePath);
                    continue;
                }

                try
                {
                    fileSystem.DeleteFile(file.FilePath);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Could not delete media file {MediaFileId} from {FilePath}",
                        file.Id,
                        file.FilePath);
                    throw new ContentRemovalException(
                        "Could not delete the media file. The library did not change.",
                        ex);
                }
            }
        }

        DeleteFiles(items.SelectMany(item => item.ContentItem?.MediaFiles ?? []));
        return Task.CompletedTask;
    }

    private static string ApplyModifier(string value, string modifier)
    {
        if (modifier.Length == 0)
        {
            return value;
        }

        return modifier switch
        {
            "lower" => value.ToLowerInvariant(),
            "upper" => value.ToUpperInvariant(),
            _ => value
        };
    }

    private static bool IsInvalid(char ch)
    {
        if (ch is '/' or '\\')
        {
            return true;
        }

        for (var i = 0; i < InvalidPathChars.Length; i++)
        {
            if (ch == InvalidPathChars[i])
            {
                return true;
            }
        }

        return false;
    }

    private static string TrimSegment(string segment)
    {
        if (segment.Length == 0)
        {
            return segment;
        }

        var end = segment.Length;
        while (end > 0)
        {
            var ch = segment[end - 1];
            if (ch is not '.' and not ' ')
            {
                break;
            }

            end--;
        }

        return end == segment.Length ? segment : segment[..end];
    }

    private static string? ResolveDisplayTitle(bool pendingMetadataRefresh, string? name, string? metadataTitle)
    {
        return !pendingMetadataRefresh && !string.IsNullOrWhiteSpace(name) ? name.Trim() : metadataTitle;
    }

    private static string ApplyTemplate(string template, RenameTokenSet tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(template.Length);
        var segment = new StringBuilder(template.Length);

        for (var i = 0; i < template.Length; i++)
        {
            var ch = template[i];

            if (ch is '/' or '\\')
            {
                builder.Append(TrimSegment(segment.ToString()));
                builder.Append(ch);
                segment.Clear();
                continue;
            }

            if (ch == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                segment.Append('{');
                i++;
                continue;
            }

            if (ch == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                segment.Append('}');
                i++;
                continue;
            }

            if (ch == '{')
            {
                var close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    segment.Append(ch);
                    continue;
                }

                var token = template[(i + 1)..close];
                segment.Append(RenderToken(token, tokens));
                i = close;
                continue;
            }

            segment.Append(ch);
        }

        builder.Append(TrimSegment(segment.ToString()));
        return builder.ToString();
    }

    private static IReadOnlyList<string> BuildRelativePaths(
        IReadOnlyList<ContentHierarchyItem> leafToRoot,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> relationshipsByNode,
        IPathResolutionContentTypeDefinitionProvider? pathProvider,
        string? renameTemplate,
        IReadOnlyList<string> fileNames,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(leafToRoot);
        ArgumentNullException.ThrowIfNull(relationshipsByNode);
        ArgumentNullException.ThrowIfNull(fileNames);
        ArgumentNullException.ThrowIfNull(logger);

        if (leafToRoot.Count == 0)
        {
            return [];
        }

        var ancestors = new List<ContentHierarchyItem>(leafToRoot.Count);
        string? leafTitle = null;
        string? leafOriginalTitle = null;
        string? leafSortTitle = null;
        int? leafYear = null;
        var leafSlug = leafToRoot[0].Slug;

        for (var i = leafToRoot.Count - 1; i >= 0; i--)
        {
            var item = leafToRoot[i];
            string? metadataTitle = null;
            string? originalTitle = null;
            string? sortTitle = null;
            int? year = null;

            if (item.MetadataContainer is not null)
            {
                foreach (var contribution in item.MetadataContainer.Contributions.OrderBy(c => c.Ordinal))
                {
                    metadataTitle ??= contribution.Title.NullIfBlank();
                    originalTitle ??= contribution.OriginalTitle.NullIfBlank();
                    sortTitle ??= contribution.SortTitle.NullIfBlank();
                    if (year is null && contribution.PublishDate is { } publishDate)
                    {
                        year = publishDate.Year;
                    }
                }
            }

            var displayTitle = ResolveDisplayTitle(item.PendingMetadataRefresh, item.Name, metadataTitle);
            ancestors.Add(item with { Name = displayTitle ?? item.Slug });

            if (i == 0)
            {
                leafTitle = displayTitle;
                leafOriginalTitle = originalTitle;
                leafSortTitle = sortTitle;
                leafYear = year;
            }
        }

        var leafFolderName = leafTitle ?? leafSlug;
        var flatLeafFolder = leafYear is { } yearValue
            ? $"{leafFolderName} ({yearValue.ToString(CultureInfo.InvariantCulture)})"
            : leafFolderName;
        var leafTokens = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = leafFolderName,
            ["OriginalTitle"] = leafOriginalTitle,
            ["SortTitle"] = leafSortTitle,
            ["Year"] = leafYear?.ToString(CultureInfo.InvariantCulture),
            ["Slug"] = leafSlug
        };

        var paths = new List<string>(fileNames.Count);
        foreach (var fileName in fileNames)
        {
            paths.Add(BuildRelativePath(
                ancestors,
                relationshipsByNode,
                flatLeafFolder,
                leafTokens,
                pathProvider,
                renameTemplate,
                fileName,
                logger));
        }

        return paths;
    }

    private static string RenderToken(string spec, RenameTokenSet tokens)
    {
        var colon = spec.IndexOf(':', StringComparison.Ordinal);
        var name = (colon < 0 ? spec : spec[..colon]).Trim();
        var modifier = colon < 0 ? string.Empty : spec[(colon + 1)..].Trim();

        if (name.Length == 0)
        {
            return string.Empty;
        }

        if (!tokens.Tokens.TryGetValue(name, out var raw))
        {
            return string.Empty;
        }

        var value = raw ?? string.Empty;
        value = ApplyModifier(value, modifier);
        return Sanitize(value);
    }

    private static string Sanitize(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var result = value;

        var needsRewrite = false;
        for (var i = 0; i < result.Length; i++)
        {
            if (IsInvalid(result[i]))
            {
                needsRewrite = true;
                break;
            }
        }

        if (needsRewrite)
        {
            var buffer = new StringBuilder(result.Length);
            foreach (var ch in result)
            {
                buffer.Append(IsInvalid(ch) ? SanitizationReplacement : ch);
            }

            result = buffer.ToString();
        }

        if (result.Contains("..", StringComparison.Ordinal))
        {
            result = result.Replace("..", SanitizationReplacement.ToString(), StringComparison.Ordinal);
        }

        return result;
    }

    private static string BuildRelativePath(
        IReadOnlyList<ContentHierarchyItem> ancestors,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> relationshipsByNode,
        string flatLeafFolder,
        IReadOnlyDictionary<string, string?> leafTemplateTokens,
        IPathResolutionContentTypeDefinitionProvider? pathProvider,
        string? renameTemplate,
        string fileName,
        ILogger logger)
    {
        var leafFileName = Path.GetFileName(fileName);
        var rawExtension = Path.GetExtension(leafFileName);
        var extension = string.IsNullOrEmpty(rawExtension) ? string.Empty : rawExtension.TrimStart('.');
        var originalFileName = Path.GetFileNameWithoutExtension(leafFileName);

        if (pathProvider is not null)
        {
            string? generated = null;
            try
            {
                generated = pathProvider.BuildPath(ancestors, relationshipsByNode, originalFileName, extension);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The content path provider {Provider} failed. The rename engine will use the fallback path",
                    pathProvider.GetType().FullName);
            }

            if (!string.IsNullOrWhiteSpace(generated))
            {
                return NormalizeContentPath(generated);
            }
        }

        if (!string.IsNullOrWhiteSpace(renameTemplate))
        {
            var tokens = new Dictionary<string, string?>(leafTemplateTokens, StringComparer.OrdinalIgnoreCase)
            {
                ["Ext"] = extension,
                ["OriginalFilename"] = originalFileName
            };
            var rendered = ApplyTemplate(renameTemplate, new RenameTokenSet { Tokens = tokens });
            if (!string.IsNullOrWhiteSpace(rendered))
            {
                return BuildFallbackPathResult(rendered);
            }
        }

        return BuildFallbackPathResult(flatLeafFolder + "/" + fileName);
    }

    private static string BuildFallbackPathResult(string relativeFilePath)
    {
        return NormalizeContentPath(relativeFilePath);
    }

    private static string NormalizeContentPath(string logicalPath)
    {
        if (string.IsNullOrWhiteSpace(logicalPath))
        {
            return string.Empty;
        }

        var segments = new List<string>();
        foreach (var raw in logicalPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            var sanitizedCharacters = raw.Select(character =>
                character is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ||
                    char.IsControl(character)
                    ? ' '
                    : character).ToArray();
            var sanitized = string.Join(' ', new string(sanitizedCharacters)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
            if (sanitized.Length > MaxSegmentLength)
            {
                sanitized = sanitized[..MaxSegmentLength].TrimEnd(' ', '.');
            }

            if (sanitized.Length > 0)
            {
                segments.Add(sanitized);
            }
        }

        return segments.Count == 0 ? string.Empty : Path.Combine([.. segments]);
    }

    private Dictionary<Guid, string?> BuildPathUpdates(
        IReadOnlyList<ContentHierarchyItem> leafToRoot,
        string relativeFilePath,
        out bool hasOwnedDirectory)
    {
        hasOwnedDirectory = false;
        var directory = Path.GetDirectoryName(relativeFilePath);
        if (string.IsNullOrEmpty(directory))
        {
            return [];
        }

        var levels = contentTypeRegistry.GetDefinitionFor(leafToRoot[0].ContentType)?.LevelDefinitions ?? [];
        var owners = leafToRoot
            .Where(item => levels.FirstOrDefault(level => level.Slug == item.Level)?.Subfolder is not null)
            .Reverse()
            .ToArray();
        if (owners.Length == 0)
        {
            return [];
        }

        var segments = directory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < owners.Length)
        {
            owners = owners[^segments.Length..];
        }

        hasOwnedDirectory = true;
        var updates = new Dictionary<Guid, string?>();
        for (var index = 0; index < owners.Length; index++)
        {
            var item = owners[index];
            var origin = item.Settings.TryGetValue("content.path.origin", out var value)
                ? value
                : null;
            if (!(string.Equals(origin, "Generated", StringComparison.OrdinalIgnoreCase) ||
                    origin is null && item.Path is null))
            {
                continue;
            }

            updates[item.Id] = index == owners.Length - 1
                ? Path.Combine(segments[index..])
                : segments[index];
        }

        return updates;
    }

    private async Task<MediaFile> GetMediaFileByPathAsync(string filePath, CancellationToken cancellationToken)
    {
        var mediaFile = (await mediaFileRepository
            .QueryAsync(
                new() { FilePaths = [filePath] },
                new() { PageNumber = 1, PageSize = 2 },
                null,
                cancellationToken)
            .ConfigureAwait(false)).Items.SingleOrDefault();
        return mediaFile ?? throw new InvalidOperationException("The media file does not exist.");
    }

    private void EnsureTargetWithinRoot(string targetPath, RootFolder rootFolder)
    {
        if (fileSystem.IsPathWithin(rootFolder.Path, targetPath))
        {
            return;
        }

        logger.LogError(
            "The rename target {TargetPath} is not in root {RootPath}. The rename engine refused the move",
            targetPath,
            rootFolder.Path);
        throw new InvalidOperationException("Rename target escapes root folder");
    }

    private async Task<PathChangePlan?> BuildPathChangePlanAsync(
        Guid contentHierarchyItemId,
        string? requestedPath,
        CancellationToken cancellationToken)
    {
        if (requestedPath is not null && string.IsNullOrWhiteSpace(requestedPath))
        {
            throw new ArgumentException(
                "A content path cannot be empty. Use the inherited-path action to clear it.",
                nameof(requestedPath));
        }

        var targetLineage = await contentHierarchyItemRepository
            .GetLineageAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (targetLineage.Count == 0)
        {
            return null;
        }

        var roots = new Dictionary<Guid, RootFolder?>();
        async Task<RootFolder?> GetRootFolderAsync(IReadOnlyList<ContentHierarchyItem> lineage)
        {
            var rootFolderId = lineage[0].EffectiveRootFolderId;
            if (!roots.TryGetValue(rootFolderId, out var rootFolder))
            {
                rootFolder = await rootFolderRepository
                    .GetByIdAsync(rootFolderId, cancellationToken)
                    .ConfigureAwait(false);
                roots[rootFolderId] = rootFolder;
            }

            return rootFolder;
        }

        var targetRootFolder = await GetRootFolderAsync(targetLineage).ConfigureAwait(false);
        var currentTargetEffectivePath = ResolvePath(
            targetLineage,
            targetRootFolder,
            contentTypeRegistry,
            pathOverrides: null,
            out var currentTargetRawPath,
            out var currentTargetError);
        var requestedOverrides = new Dictionary<Guid, string?> { [contentHierarchyItemId] = requestedPath };
        var proposedTargetEffectivePath = ResolvePath(
            targetLineage,
            targetRootFolder,
            contentTypeRegistry,
            requestedOverrides,
            out var proposedTargetRawPath,
            out var proposedTargetError);
        var normalizedPath = requestedPath;
        if (proposedTargetError is null && requestedPath is not null)
        {
            var normalizedRequestedPath = requestedPath.Trim().Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            normalizedPath = Path.IsPathFullyQualified(normalizedRequestedPath)
                ? proposedTargetEffectivePath
                : normalizedRequestedPath;
        }

        var overrides = new Dictionary<Guid, string?> { [contentHierarchyItemId] = normalizedPath };

        const int pageSize = 500;
        var descendants = new List<ContentHierarchyItem>();
        var descendantsPageNumber = 1;
        while (true)
        {
            var page = await contentHierarchyItemRepository.QueryAsync(
                new ContentHierarchyItemCriteria { AncestorId = contentHierarchyItemId },
                new() { PageNumber = descendantsPageNumber, PageSize = pageSize },
                sort: null,
                cancellationToken).ConfigureAwait(false);
            var pageItems = page.Items.ToList();
            descendants.AddRange(pageItems);
            if (pageItems.Count < pageSize)
            {
                break;
            }

            descendantsPageNumber++;
        }

        var hierarchyItems = new List<ContentHierarchyItem>(descendants.Count + 1) { targetLineage[0] };
        hierarchyItems.AddRange(descendants.Where(item => item.Id != contentHierarchyItemId));

        var filesByHierarchyItemId = hierarchyItems
            .Where(item => item.ContentItem?.MediaFiles.Count > 0)
            .ToDictionary(
                item => item.Id,
                item => (IReadOnlyList<MediaFile>)[.. item.ContentItem!.MediaFiles]);

        var affectedIds = new HashSet<Guid>();
        var fileMoves = new List<PathFileMove>();
        var inaccessiblePaths = new HashSet<string>(StringComparer.Ordinal);

        if (currentTargetError is not null)
        {
            inaccessiblePaths.Add(currentTargetError);
        }

        if (proposedTargetError is not null)
        {
            inaccessiblePaths.Add(proposedTargetError);
        }

        if (proposedTargetEffectivePath is not null &&
            !HasExistingDirectoryAncestor(proposedTargetEffectivePath))
        {
            inaccessiblePaths.Add(proposedTargetEffectivePath);
        }

        foreach (var item in hierarchyItems)
        {
            var lineage = item.Id == contentHierarchyItemId
                ? targetLineage
                : await contentHierarchyItemRepository
                    .GetLineageAsync(item.Id, cancellationToken)
                    .ConfigureAwait(false);
            var rootFolder = item.Id == contentHierarchyItemId
                ? targetRootFolder
                : await GetRootFolderAsync(lineage).ConfigureAwait(false);
            var currentError = currentTargetError;
            var proposedError = proposedTargetError;
            var currentEffectivePath = currentTargetEffectivePath;
            var proposedEffectivePath = proposedTargetEffectivePath;
            if (item.Id != contentHierarchyItemId)
            {
                currentEffectivePath = ResolvePath(
                    lineage,
                    rootFolder,
                    contentTypeRegistry,
                    null,
                    out _,
                    out currentError);
                proposedEffectivePath = ResolvePath(
                    lineage,
                    rootFolder,
                    contentTypeRegistry,
                    overrides,
                    out _,
                    out proposedError);
            }

            var currentRawPath = item.Id == contentHierarchyItemId ? currentTargetRawPath : item.Path;
            var proposedRawPath = item.Id == contentHierarchyItemId ? proposedTargetRawPath : item.Path;
            filesByHierarchyItemId.TryGetValue(item.Id, out var files);
            var locationChanged = currentEffectivePath is null || proposedEffectivePath is null
                ? currentEffectivePath != proposedEffectivePath
                : !fileSystem.PathsEqual(currentEffectivePath, proposedEffectivePath);
            var isTarget = item.Id == contentHierarchyItemId;
            var rawValueChanged = isTarget && !string.Equals(
                currentRawPath,
                proposedRawPath,
                StringComparison.Ordinal);
            if (!locationChanged && !rawValueChanged)
            {
                continue;
            }

            affectedIds.Add(item.Id);

            if (!isTarget && currentError is not null)
            {
                inaccessiblePaths.Add(currentError);
            }

            if (!isTarget && proposedError is not null)
            {
                inaccessiblePaths.Add(proposedError);
            }

            if (!locationChanged ||
                currentEffectivePath is null ||
                proposedEffectivePath is null)
            {
                continue;
            }

            foreach (var file in files ?? [])
            {
                string destination;
                try
                {
                    var relativePath = fileSystem.GetRelativePath(currentEffectivePath, file.FilePath);
                    if (!fileSystem.IsPathWithin(currentEffectivePath, file.FilePath))
                    {
                        relativePath = Path.GetFileName(file.FilePath);
                    }

                    destination = fileSystem.GetFullPath(Path.Combine(proposedEffectivePath, relativePath));
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    inaccessiblePaths.Add(file.FilePath);
                    continue;
                }

                if (!fileSystem.PathsEqual(file.FilePath, destination))
                {
                    fileMoves.Add(new PathFileMove(file.Id, file.FilePath, destination));
                }
            }
        }

        var conflicts = new List<string>();
        if (proposedTargetEffectivePath is { } proposedPath && fileSystem.FileExists(proposedPath))
        {
            conflicts.Add(proposedPath);
        }

        foreach (var move in fileMoves)
        {
            if (fileMoves.Count(candidate =>
                    fileSystem.PathsEqual(candidate.DestinationPath, move.DestinationPath)) <= 1 ||
                conflicts.Any(path => fileSystem.PathsEqual(path, move.DestinationPath)))
            {
                continue;
            }

            conflicts.Add(move.DestinationPath);
        }

        foreach (var move in fileMoves)
        {
            if (!fileSystem.FileExists(move.SourcePath))
            {
                inaccessiblePaths.Add(move.SourcePath);
            }

            var destinationDirectory = Path.GetDirectoryName(move.DestinationPath);
            var collides = fileSystem.FileExists(move.DestinationPath) ||
                fileSystem.DirectoryExists(move.DestinationPath) ||
                (!string.IsNullOrEmpty(destinationDirectory) && fileSystem.FileExists(destinationDirectory));
            if (collides)
            {
                if (!conflicts.Any(path => fileSystem.PathsEqual(path, move.DestinationPath)))
                {
                    conflicts.Add(move.DestinationPath);
                }
            }
        }

        var descendantVersions = hierarchyItems
            .Where(item => item.Id != contentHierarchyItemId && affectedIds.Contains(item.Id))
            .Select(item => new PathNodeVersion(item.Id, item.Path, item.ModifiedAt))
            .ToArray();
        return new PathChangePlan(
            proposedTargetEffectivePath is not null && conflicts.Count == 0 && inaccessiblePaths.Count == 0,
            [.. conflicts],
            [.. inaccessiblePaths],
            targetLineage[0].Path,
            targetLineage[0].ModifiedAt,
            normalizedPath,
            fileMoves,
            descendantVersions);
    }

    private List<PathFileMove> MovePathFiles(
        IReadOnlyList<PathFileMove> moves,
        CancellationToken cancellationToken)
    {
        var completed = new List<PathFileMove>(moves.Count);
        try
        {
            for (var index = 0; index < moves.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var move = moves[index];
                var destinationDirectory = Path.GetDirectoryName(move.DestinationPath);
                if (!string.IsNullOrEmpty(destinationDirectory) &&
                    !fileSystem.DirectoryExists(destinationDirectory))
                {
                    fileSystem.CreateDirectory(destinationDirectory);
                }

                fileSystem.Move(move.SourcePath, move.DestinationPath);
                completed.Add(move);
            }

            return completed;
        }
        catch
        {
            RevertPathMoves(completed);
            throw;
        }
    }

    private void RevertPathMoves(IReadOnlyList<PathFileMove> completedMoves)
    {
        for (var index = completedMoves.Count - 1; index >= 0; index--)
        {
            var move = completedMoves[index];
            try
            {
                fileSystem.Move(move.DestinationPath, move.SourcePath);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "The content path rollback could not restore {DestinationPath} to {SourcePath}. " +
                    "An operator must reconcile the file",
                    move.DestinationPath,
                    move.SourcePath);
            }
        }
    }

    private bool HasExistingDirectoryAncestor(string path)
    {
        try
        {
            var current = fileSystem.GetFullPath(path);
            while (!string.IsNullOrEmpty(current))
            {
                if (fileSystem.DirectoryExists(current))
                {
                    return true;
                }

                if (fileSystem.FileExists(current))
                {
                    return false;
                }

                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || fileSystem.PathsEqual(parent, current))
                {
                    return false;
                }

                current = parent;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return false;
    }

    private string? ResolvePath(
        IReadOnlyList<ContentHierarchyItem> leafToRoot,
        RootFolder? rootFolder,
        IContentTypeRegistry registry,
        IReadOnlyDictionary<Guid, string?>? pathOverrides,
        out string? leafRawPath,
        out string? resolutionError)
    {
        if (leafToRoot.Count == 0)
        {
            leafRawPath = null;
            resolutionError = "The content hierarchy item does not exist.";
            return null;
        }

        var leaf = leafToRoot[0];
        string? RawPath(ContentHierarchyItem item) =>
            pathOverrides is not null && pathOverrides.TryGetValue(item.Id, out var path) ? path : item.Path;
        leafRawPath = RawPath(leaf);
        var effectivePath = (string?)null;
        resolutionError = "The hierarchy item has no effective root folder.";
        if (rootFolder is not null && !string.IsNullOrWhiteSpace(rootFolder.Path))
        {
            try
            {
                var root = leafToRoot[^1];
                var subfolder = registry.GetDefinitionFor(root.ContentType)?.LevelDefinitions
                    .FirstOrDefault(level => level.Slug == root.Level)?.Subfolder;
                var basePath = string.IsNullOrWhiteSpace(subfolder)
                    ? rootFolder.Path.Trim()
                    : Path.Combine(rootFolder.Path.Trim(), subfolder);
                effectivePath = fileSystem.GetFullPath(basePath).Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                resolutionError = null;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                resolutionError = "The effective root folder path is invalid.";
            }
        }

        for (var index = leafToRoot.Count - 1; index >= 0; index--)
        {
            var item = leafToRoot[index];
            var rawPath = RawPath(item);
            if (rawPath is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rawPath))
            {
                effectivePath = null;
                resolutionError = "A content path cannot be empty.";
                continue;
            }

            var normalized = rawPath.Trim().Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            try
            {
                if (Path.IsPathRooted(normalized) && !Path.IsPathFullyQualified(normalized))
                {
                    effectivePath = null;
                    resolutionError = "An absolute content path must be fully qualified.";
                    continue;
                }

                if (Path.IsPathFullyQualified(normalized))
                {
                    effectivePath = fileSystem.GetFullPath(normalized);
                }
                else if (effectivePath is null)
                {
                    resolutionError = $"Relative content path {rawPath} has no effective parent path.";
                    continue;
                }
                else
                {
                    var basePath = effectivePath;
                    effectivePath = fileSystem.GetFullPath(Path.Combine(basePath, normalized));
                    if (!fileSystem.IsPathWithin(basePath, effectivePath))
                    {
                        effectivePath = null;
                        resolutionError = $"Relative content path {rawPath} escapes its effective parent path.";
                        continue;
                    }
                }

                resolutionError = null;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                effectivePath = null;
                resolutionError = $"Content path {rawPath} is invalid.";
            }
        }

        resolutionError ??= effectivePath is null ? "The content path could not be resolved." : null;
        return effectivePath;
    }

    private readonly record struct PathFileMove(
        Guid MediaFileId,
        string SourcePath,
        string DestinationPath);

    private readonly record struct PathNodeVersion(
        Guid ContentHierarchyItemId,
        string? OriginalPath,
        DateTime? OriginalModifiedAt);

    private sealed record PathChangePlan(
        bool CanMove,
        IReadOnlyList<string> Conflicts,
        IReadOnlyList<string> InaccessiblePaths,
        string? OriginalPath,
        DateTime? OriginalModifiedAt,
        string? NormalizedPath,
        IReadOnlyList<PathFileMove> FileMoves,
        IReadOnlyList<PathNodeVersion> DescendantVersions);

    private sealed record RenameTokenSet
    {
        public IReadOnlyDictionary<string, string?> Tokens { get; init; } =
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    }
}
