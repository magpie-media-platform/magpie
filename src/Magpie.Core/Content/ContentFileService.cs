using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Patch;
using Magpie.Models.Storage;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Magpie.Core.Content;

internal sealed class ContentFileService(
    IContentHierarchyItemRepository contentHierarchyItemRepository,
    IMetadataContainerRepository metadataContainerRepository,
    IRootFolderRepository rootFolderRepository,
    IContentTypeRegistry contentTypeRegistry,
    ISubjectKindRegistry subjectKindRegistry,
    IFileSystem fileSystem,
    IMediaFileRepository mediaFileRepository,
    IContentRepository contentRepository,
    IAuditRecordRepository auditRecordRepository,
    ITransactionManager transactionManager,
    IJobManager jobManager,
    TimeProvider timeProvider,
    ILogger<ContentFileService> logger) : IContentFileService
{
    private static readonly char[] InvalidPathChars =
    [
        '<',
        '>',
        ':',
        '"',
        '|',
        '?',
        '*',
        '\0',
        '\x01',
        '\x02',
        '\x03',
        '\x04',
        '\x05',
        '\x06',
        '\x07',
        '\x08',
        '\x09',
        '\x0A',
        '\x0B',
        '\x0C',
        '\x0D',
        '\x0E',
        '\x0F',
        '\x10',
        '\x11',
        '\x12',
        '\x13',
        '\x14',
        '\x15',
        '\x16',
        '\x17',
        '\x18',
        '\x19',
        '\x1A',
        '\x1B',
        '\x1C',
        '\x1D',
        '\x1E',
        '\x1F'
    ];

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "CONIN$",
        "CONOUT$",
        "COM0",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "COM¹",
        "COM²",
        "COM³",
        "LPT0",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9",
        "LPT¹",
        "LPT²",
        "LPT³"
    };

    private static readonly Regex EmptyBracketPattern = new(
        @"\(\s*\)|\[\s*\]|\{\s*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RepeatedDashPattern = new(
        @"(?:\s+-)+\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RepeatedSeparatorPattern = new(
        @"([._])\1+|\s{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const int MaxSegmentLength = 255;
    private const char SanitizationReplacement = '_';

    private const string CollectionSeparator = " ";
    private const string LevelNumberToken = "Number";
    private const string LanguageSeparator = "+";

    private static readonly RenameTokenDefinition[] FixedRenameTokens =
    [
        new(
            "Ext",
            RenameTokenFamily.File,
            "The extension of the source file, without the dot.",
            "mkv",
            source => source.FileName is null ? null : Path.GetExtension(source.FileName).TrimStart('.')),
        new(
            "OriginalFilename",
            RenameTokenFamily.File,
            "The name of the source file, without the extension.",
            "Example.Title.2024.1080p",
            source => source.FileName is null
                ? null
                : Path.GetFileNameWithoutExtension(source.FileName)),
        new(
            "Title",
            RenameTokenFamily.Metadata,
            "The title of the content that owns the file. The slug replaces a missing title.",
            "Example Title",
            source => source.Leaf.Name),
        new(
            "OriginalTitle",
            RenameTokenFamily.Metadata,
            "The title in the original language of the content.",
            "Titre Exemple",
            source => FirstContribution(source.Leaf, contribution => contribution.OriginalTitle)),
        new(
            "SortTitle",
            RenameTokenFamily.Metadata,
            "The title that sorts the content.",
            "Example Title, The",
            source => FirstContribution(source.Leaf, contribution => contribution.SortTitle)),
        new(
            "Year",
            RenameTokenFamily.Metadata,
            "The publish year of the content.",
            "2024",
            source => YearOf(source.Leaf)),
        new(
            "Slug",
            RenameTokenFamily.Metadata,
            "The stable slug of the content.",
            "example-title",
            source => source.Leaf.Slug),
        new(
            "Language",
            RenameTokenFamily.Metadata,
            "The language of the content metadata, as a code.",
            "EN",
            source => LanguageCode(FirstContribution(source.Leaf, contribution => contribution.Language))),
        new(
            "Genres",
            RenameTokenFamily.Metadata,
            "The genres of the content, separated by spaces.",
            "Drama Thriller",
            source => JoinCollection(
                source.Leaf.MetadataContainer?.Contributions
                    .OrderBy(contribution => contribution.Ordinal)
                    .SelectMany(contribution => contribution.Genres.OrderBy(genre => genre.Ordinal))
                    .Select(genre => genre.Name) ?? [],
                CollectionSeparator)),
        new(
            "MediaInfo.Container",
            RenameTokenFamily.MediaInfo,
            "The container format of the file.",
            "matroska",
            source => source.MediaInfo?.Container),
        new(
            "MediaInfo.VideoCodec",
            RenameTokenFamily.MediaInfo,
            "The codec of the first video track.",
            "h264",
            source => FirstTrack(source, "Video")?.Codec),
        new(
            "MediaInfo.VideoResolution",
            RenameTokenFamily.MediaInfo,
            "The frame height of the first video track, with a p suffix.",
            "1080p",
            source => FirstTrack(source, "Video")?.Height is { } height
                ? height.ToString(CultureInfo.InvariantCulture) + "p"
                : null),
        new(
            "MediaInfo.VideoDynamicRange",
            RenameTokenFamily.MediaInfo,
            "The dynamic range of the first video track.",
            "HDR10",
            source => FirstTrack(source, "Video")?.VideoRange),
        new(
            "MediaInfo.AudioCodec",
            RenameTokenFamily.MediaInfo,
            "The codec of the first audio track.",
            "aac",
            source => FirstTrack(source, "Audio")?.Codec),
        new(
            "MediaInfo.AudioChannels",
            RenameTokenFamily.MediaInfo,
            "The channel layout of the first audio track.",
            "5.1",
            source => FirstTrack(source, "Audio")?.Channels is { } channels and > 0
                ? channels >= 6
                    ? (channels - 1).ToString(CultureInfo.InvariantCulture) + ".1"
                    : channels.ToString(CultureInfo.InvariantCulture) + ".0"
                : null),
        new(
            "MediaInfo.AudioLanguages",
            RenameTokenFamily.MediaInfo,
            "The languages of the audio tracks, joined with a plus sign.",
            "EN+FR",
            source => TrackLanguages(source, "Audio")),
        new(
            "MediaInfo.SubtitleLanguages",
            RenameTokenFamily.MediaInfo,
            "The languages of the subtitle tracks, joined with a plus sign.",
            "EN+DE",
            source => TrackLanguages(source, "Subtitle"))
    ];

    private readonly Dictionary<string, IReadOnlyList<RenameTokenDefinition>> _renameTokenDefinitions =
        new(StringComparer.Ordinal);

    public async Task<ContentFileBrowserLocation?> GetBrowserLocationAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        var lineage = await contentHierarchyItemRepository
            .GetLineageAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (lineage.Count == 0)
        {
            return null;
        }

        var rootFolder = await rootFolderRepository
            .GetByIdAsync(lineage[0].EffectiveRootFolderId, cancellationToken)
            .ConfigureAwait(false);
        if (rootFolder is null)
        {
            return null;
        }

        try
        {
            var rootPath = fileSystem.GetFullPath(rootFolder.Path);
            var effectivePath = ResolvePath(lineage, rootFolder, pathOverrides: null, out _, out _);
            var initialPath = effectivePath is not null &&
                fileSystem.IsPathWithin(rootPath, effectivePath) &&
                fileSystem.DirectoryExists(effectivePath) &&
                !fileSystem.PathContainsLink(rootPath, effectivePath)
                    ? effectivePath
                    : rootPath;
            return new() { RootPath = rootPath, InitialPath = initialPath };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("The configured content path is not accessible.", ex);
        }
    }

    public async Task<string?> GetEffectivePathAsync(
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken = default)
    {
        var lineage = await contentHierarchyItemRepository
            .GetLineageAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (lineage.Count == 0)
        {
            return null;
        }

        var rootFolder = await rootFolderRepository
            .GetByIdAsync(lineage[0].EffectiveRootFolderId, cancellationToken)
            .ConfigureAwait(false);

        var effectivePath = ResolvePath(lineage, rootFolder, pathOverrides: null, out var leafRawPath, out _);
        return effectivePath ?? leafRawPath;
    }

    public async Task<IReadOnlyList<FileSystemEntry>?> BrowseAsync(
        Guid contentHierarchyItemId,
        string path,
        CancellationToken cancellationToken = default)
    {
        var location = await GetBrowserLocationAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (location is null)
        {
            return null;
        }

        try
        {
            var browsePath = fileSystem.GetFullPath(path);
            if (!fileSystem.IsPathWithin(location.RootPath, browsePath))
            {
                throw new ArgumentException("The browse path is outside the configured root folder.");
            }

            if (fileSystem.PathContainsLink(location.RootPath, browsePath))
            {
                throw new ArgumentException("The browse path contains a link and cannot be opened.");
            }

            return await fileSystem.BrowseAsync(browsePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("The selected folder is not accessible.", ex);
        }
    }

    public Task<AttachContentFileResult> AttachAsync(
        Guid contentHierarchyItemId,
        string filePath,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return AttachCoreAsync(
            contentHierarchyItemId,
            filePath,
            actingUserId,
            allowContainerFallback: false,
            acceptExisting: false,
            queueMediaInfoScan: true,
            materializeFulfillmentAsync: null,
            cancellationToken);
    }

    public Task<AttachContentFileResult> AttachImportedAsync(
        Guid contentHierarchyItemId,
        string filePath,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        return AttachCoreAsync(
            contentHierarchyItemId,
            filePath,
            actingUserId,
            allowContainerFallback: true,
            acceptExisting: true,
            queueMediaInfoScan: false,
            materializeFulfillmentAsync: null,
            cancellationToken);
    }

    public Task<AttachContentFileResult> AttachAsync(
        Guid contentHierarchyItemId,
        string filePath,
        Guid? actingUserId,
        Func<CancellationToken, Task>? materializeFulfillmentAsync,
        CancellationToken cancellationToken = default)
    {
        return AttachCoreAsync(
            contentHierarchyItemId,
            filePath,
            actingUserId,
            allowContainerFallback: false,
            acceptExisting: false,
            queueMediaInfoScan: true,
            materializeFulfillmentAsync,
            cancellationToken);
    }

    private async Task<AttachContentFileResult> AttachCoreAsync(
        Guid contentHierarchyItemId,
        string filePath,
        Guid? actingUserId,
        bool allowContainerFallback,
        bool acceptExisting,
        bool queueMediaInfoScan,
        Func<CancellationToken, Task>? materializeFulfillmentAsync,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A file path is required.");
        }

        var hierarchyItem = await contentHierarchyItemRepository
            .GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (hierarchyItem is null)
        {
            return new AttachContentFileResult();
        }

        var level = contentTypeRegistry.GetDefinitionFor(hierarchyItem.ContentType)?.LevelDefinitions
            .FirstOrDefault(candidate => candidate.Slug == hierarchyItem.Level)
            ?? throw new ArgumentException("The content level has no file contract.");
        var rootFolder = await rootFolderRepository
            .GetByIdAsync(hierarchyItem.EffectiveRootFolderId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ArgumentException("The content item has no configured root folder.");

        string fullPath;
        try
        {
            fullPath = fileSystem.GetFullPath(filePath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("The selected file path is invalid.", ex);
        }

        var rootPath = fileSystem.GetFullPath(rootFolder.Path);
        if (!fileSystem.IsPathWithin(rootPath, fullPath))
        {
            throw new ArgumentException("The selected file is outside the configured root folder.");
        }

        if (!fileSystem.FileExists(fullPath))
        {
            throw new ArgumentException("The selected file does not exist.");
        }

        try
        {
            if (fileSystem.PathContainsLink(rootPath, fullPath))
            {
                throw new ArgumentException("The selected file path contains a link and cannot be imported.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("The selected file is not accessible.", ex);
        }

        if (level.AcceptedFileTypes.Count == 0 && !allowContainerFallback)
        {
            throw new ArgumentException("This content level has no configured file types.");
        }

        var mimeType = await fileSystem.LookupMimeTypeAsync(fullPath, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ArgumentException("The selected file type is unknown.");
        var isPrimary = await fileSystem
            .MatchesAnyFileTypeAsync(fullPath, level.AcceptedFileTypes, cancellationToken)
            .ConfigureAwait(false);
        var isSidecar = !isPrimary && await fileSystem
            .MatchesAnyFileTypeAsync(fullPath, level.AcceptedSidecarTypes, cancellationToken)
            .ConfigureAwait(false);
        isPrimary = isPrimary || !isSidecar && allowContainerFallback && level.AcceptedFileTypes.Count == 0;
        if (!isPrimary && !isSidecar)
        {
            throw new ArgumentException("The selected file type is not accepted by this content level.");
        }

        long fileSize;
        FileState? fileState = null;
        try
        {
            fileSize = fileSystem.GetFileSize(fullPath);
            using var readableFile = fileSystem.OpenRead(fullPath);
            try
            {
                fileState = fileSystem.ReadFileState(fullPath);
            }
            catch (NotSupportedException)
            {
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ArgumentException("The selected file is not accessible.", ex);
        }

        return await transactionManager.ExecuteWithTransactionAsync(
                async ct =>
                {
                    await LockFulfillmentLineageAsync(contentHierarchyItemId, ct).ConfigureAwait(false);
                    var existing = await mediaFileRepository.GetByPathAsync(fullPath, ct).ConfigureAwait(false);
                    if (existing is not null)
                    {
                        var hasFile = await ItemHasFileAsync(existing, contentHierarchyItemId, ct)
                            .ConfigureAwait(false);
                        if (acceptExisting && hasFile)
                        {
                            return new AttachContentFileResult
                            {
                                Result = new ContentFileAttachmentResult { MediaFile = existing, IsPrimary = isPrimary }
                            };
                        }

                        if (acceptExisting || hasFile ||
                            !await IsSiblingOfOwnerAsync(hierarchyItem, existing, ct).ConfigureAwait(false))
                        {
                            throw new ArgumentException("The selected file is already attached to content.");
                        }
                    }

                    var now = timeProvider.GetUtcNow().UtcDateTime;
                    var contentItem = await GetOrCreateContentItemAsync(
                        contentHierarchyItemId,
                        now,
                        actingUserId,
                        ct).ConfigureAwait(false);
                    var mediaFile = existing;
                    if (mediaFile is not null)
                    {
                        mediaFile.Coverage.Add(new() { MediaFileId = mediaFile.Id, ContentItemId = contentItem.Id });
                        mediaFile.ModifiedAt = now;
                        if (!await mediaFileRepository.UpdateAsync(mediaFile, actingUserId, ct).ConfigureAwait(false))
                        {
                            throw new InvalidOperationException("The selected file changed during the attach.");
                        }

                        mediaFile = await mediaFileRepository.GetByIdAsync(mediaFile.Id, ct).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("The selected file changed during the attach.");
                    }
                    else
                    {
                        try
                        {
                            mediaFile = await mediaFileRepository.CreateAsync(
                                new MediaFile
                                {
                                    ContentItemId = contentItem.Id,
                                    FileKind = fileSystem.ResolveFileKind(mimeType).ToString(),
                                    MimeType = mimeType,
                                    FilePath = fullPath,
                                    FileSize = fileSize,
                                    FileModifiedAt = fileState?.LastWriteTimeUtc,
                                    CreatedAt = now
                                },
                                actingUserId,
                                ct).ConfigureAwait(false);
                        }
                        catch (DbUpdateException ex) when (IsPathUniqueViolation(ex))
                        {
                            throw new ArgumentException("The selected file is already attached to content.", ex);
                        }
                    }

                    var fulfillmentChanged = await UpdateFulfillmentCoreAsync(
                        hierarchyItem,
                        contentItem,
                        now,
                        actingUserId,
                        ct).ConfigureAwait(false);

                    await auditRecordRepository.CreateAsync(
                        new AuditRecord
                        {
                            Category = AuditCategories.Content,
                            Operation = "FileAttached",
                            EntityType = nameof(MediaFile),
                            EntityId = mediaFile.Id.ToString(),
                            ParentEntityId = contentHierarchyItemId.ToString(),
                            Detail = $"Attached {Path.GetFileName(fullPath)} as " +
                                (isPrimary ? "primary content." : "a sidecar."),
                            Timestamp = now,
                            UserId = actingUserId
                        },
                        ct).ConfigureAwait(false);

                    if (fulfillmentChanged && materializeFulfillmentAsync is not null)
                    {
                        await materializeFulfillmentAsync(ct).ConfigureAwait(false);
                    }

                    if (queueMediaInfoScan && existing is null)
                    {
                        await jobManager.ExecuteAsync(
                            new JobEnqueueRequest
                            {
                                JobType = MagpieJobs.Types.MediaInfoExtractScan,
                                Parameters = new Dictionary<string, string>
                                {
                                    [MagpieJobs.Parameters.ContentHierarchyItemId] =
                                        contentHierarchyItemId.ToString("D"),
                                    [MagpieJobs.Parameters.MediaInfoPolicy] =
                                        MagpieJobs.ParameterValues.AllMediaInfoPolicy
                                },
                                Mode = JobExecutionMode.Queue,
                                UserId = actingUserId
                            },
                            ct).ConfigureAwait(false);
                    }

                    return new AttachContentFileResult
                    {
                        Result = new ContentFileAttachmentResult { MediaFile = mediaFile, IsPrimary = isPrimary },
                        FulfillmentChanged = fulfillmentChanged
                    };
                },
                cancellationToken).ConfigureAwait(false);
    }

    public async Task<UnlinkContentFileResult> UnlinkAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var target = await GetOwnedOrCoveredMediaFileAsync(contentHierarchyItemId, mediaFileId, cancellationToken)
            .ConfigureAwait(false);
        if (target is null)
        {
            return new UnlinkContentFileResult();
        }

        var (removed, fulfillmentChanged) = await RemoveAssociationAsync(
            contentHierarchyItemId,
            target,
            "FileUnlinked",
            $"Unlinked {Path.GetFileName(target.FilePath)} and kept the host file.",
            completeIfAlreadyRemoved: false,
            keepOtherItems: true,
            actingUserId,
            cancellationToken).ConfigureAwait(false);
        return new UnlinkContentFileResult { Removed = removed, FulfillmentChanged = fulfillmentChanged };
    }

    public async Task<DeleteContentFileResult> DeleteAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var target = await GetOwnedOrCoveredMediaFileAsync(contentHierarchyItemId, mediaFileId, cancellationToken)
            .ConfigureAwait(false);
        if (target is null)
        {
            return new DeleteContentFileResult();
        }

        using var paths = fileSystem.AcquireImportPaths([target.FilePath]);
        var fileExists = fileSystem.FileExists(target.FilePath);
        if (!fileExists)
        {
            logger.LogWarning(
                "The content service removes the row for media file {MediaFileId}, missing on disk at " +
                "{FilePath}",
                mediaFileId,
                target.FilePath);
        }

        var (removed, fulfillmentChanged) = await RemoveAssociationAsync(
            contentHierarchyItemId,
            target,
            "FileDeleted",
            fileExists
                ? $"Deleted {Path.GetFileName(target.FilePath)} from the host."
                : $"Removed the missing file {Path.GetFileName(target.FilePath)} from content.",
            completeIfAlreadyRemoved: fileExists,
            keepOtherItems: false,
            actingUserId,
            cancellationToken).ConfigureAwait(false);

        var fileDeleted = false;
        if (fileExists)
        {
            try
            {
                fileSystem.DeleteFile(target.FilePath);
                fileDeleted = true;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "The content service deleted media file {MediaFileId} from the database but failed to " +
                    "delete {FilePath} from disk",
                    mediaFileId,
                    target.FilePath);
                await auditRecordRepository.CreateAsync(
                    new AuditRecord
                    {
                        Category = AuditCategories.Content,
                        Operation = "FileDeleteFailed",
                        EntityType = nameof(MediaFile),
                        EntityId = target.Id.ToString(),
                        ParentEntityId = contentHierarchyItemId.ToString(),
                        Detail = $"Deleted the content row but could not delete {target.FilePath}.",
                        Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                        UserId = actingUserId
                    },
                    cancellationToken).ConfigureAwait(false);
                throw new MediaFileDeletionException($"Failed to delete {target.FilePath} from disk.", ex);
            }
        }

        return new DeleteContentFileResult
        {
            Deleted = fileDeleted || removed,
            FulfillmentChanged = fulfillmentChanged
        };
    }

    public async Task<bool> UpdateFulfillmentAsync(
        Guid contentHierarchyItemId,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var hierarchyItem = await contentHierarchyItemRepository
            .GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (hierarchyItem is null)
        {
            return false;
        }

        var changed = false;
        await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                await LockFulfillmentLineageAsync(contentHierarchyItemId, ct).ConfigureAwait(false);
                var contentItem = (await contentRepository.QueryAsync(
                    new ContentItemQuery { ContentHierarchyItemId = contentHierarchyItemId },
                    new() { PageNumber = 1, PageSize = 2 },
                    null,
                    ct).ConfigureAwait(false)).Items.SingleOrDefault();
                if (contentItem is null)
                {
                    return;
                }

                changed = await UpdateFulfillmentCoreAsync(
                    hierarchyItem,
                    contentItem,
                    timeProvider.GetUtcNow().UtcDateTime,
                    actingUserId,
                    ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return changed;
    }

    public async Task NormalizeMediaFilePathKeysAsync(CancellationToken cancellationToken = default)
    {
        await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                var mediaFiles = await mediaFileRepository.ListAllAsync(ct).ConfigureAwait(false);
                foreach (var group in mediaFiles.GroupBy(
                    file => ToMediaFilePathKey(file.FilePath),
                    StringComparer.Ordinal))
                {
                    var ordered = group.OrderBy(file => file.PathKey.StartsWith("legacy:", StringComparison.Ordinal))
                        .ThenBy(file => file.CreatedAt)
                        .ThenBy(file => file.Id)
                        .ToArray();
                    var survivor = ordered[0];
                    foreach (var duplicate in ordered.Skip(1))
                    {
                        var item = await contentRepository.GetByIdAsync(duplicate.ContentItemId, ct)
                            .ConfigureAwait(false)
                            ?? throw new InvalidOperationException("The media file has no content item.");
                        await UnlinkAsync(item.ContentHierarchyItemId, duplicate.Id, null, ct).ConfigureAwait(false);
                        await contentRepository.ReconcileMediaStateAsync(item.ContentHierarchyItemId, null, ct)
                            .ConfigureAwait(false);
                    }

                    if (survivor.PathKey == group.Key)
                    {
                        continue;
                    }

                    await mediaFileRepository.UpdateAsync(survivor, survivor.ModifiedByUserId, ct)
                        .ConfigureAwait(false);
                    await auditRecordRepository.CreateAsync(
                        new()
                        {
                            Category = AuditCategories.Content,
                            Operation = "FilePathNormalized",
                            EntityType = "MediaFile",
                            EntityId = survivor.Id.ToString(),
                            ParentEntityId = survivor.ContentItemId.ToString(),
                            Detail = $"Normalized the path key for {survivor.FilePath}.",
                            Timestamp = timeProvider.GetUtcNow().UtcDateTime
                        },
                        ct).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    public Task<ContentRename> PreviewRenameAsync(
        Guid contentHierarchyItemId,
        FileInfo original,
        CancellationToken cancellationToken = default) =>
        PreviewRenameAsync(contentHierarchyItemId, original, null, cancellationToken);

    public async Task<ContentRename> PreviewRenameAsync(
        Guid contentHierarchyItemId,
        FileInfo original,
        MediaInfo? mediaInfo,
        CancellationToken cancellationToken = default)
    {
        var lineage = await contentHierarchyItemRepository
            .GetLineageAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (lineage.Count == 0)
        {
            throw new InvalidOperationException("The content hierarchy item does not exist.");
        }

        var leaf = lineage[0];
        var file = await mediaFileRepository.GetByPathAsync(original.FullName, cancellationToken)
            .ConfigureAwait(false);
        var covered = await GetCoveredItemsAsync(file, lineage, cancellationToken).ConfigureAwait(false);
        var planned = covered.Count > 0 ? lineage.Skip(1).ToArray() : lineage;
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

        var tokenDefinitions = GetRenameTokenDefinitions(leaf.ContentType);
        var pathResult = BuildRelativePaths(
            planned,
            relationshipsByNode,
            pathProvider,
            null,
            [original.Name],
            logger,
            tokenDefinitions: tokenDefinitions,
            mediaInfo: mediaInfo)[0];
        var pathUpdates = BuildPathUpdates(planned, pathResult.Path, pathResult.Directories, out var hasOwnedDirectory);
        var mask = string.IsNullOrWhiteSpace(rootFolder.RenameTemplate)
            ? default
            : BuildRelativePaths(
                lineage,
                relationshipsByNode,
                null,
                rootFolder.RenameTemplate,
                [original.Name],
                logger,
                tokenDefinitions: tokenDefinitions,
                mediaInfo: mediaInfo,
                covered: covered)[0];
        string targetPath;
        if (mask.Directories is not null)
        {
            targetPath = Path.Combine(
                ResolveMaskBasePath(lineage, rootFolder, relationshipsByNode, original.Name, pathUpdates),
                mask.Path);
        }
        else
        {
            var effectivePath = ResolvePath(planned, rootFolder, pathUpdates, out _, out var resolutionError);
            if (string.IsNullOrWhiteSpace(effectivePath))
            {
                throw new ArgumentException(resolutionError ?? "The content path could not be resolved.");
            }

            var relativeFilePath = hasOwnedDirectory
                ? Path.GetFileName(pathResult.Path)
                : pathResult.Path;
            targetPath = Path.Combine(effectivePath, relativeFilePath);
        }

        if (!lineage.Any(item => item.Path is not null && Path.IsPathFullyQualified(
            item.Path.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar))))
        {
            EnsureTargetWithinRoot(targetPath, rootFolder);
        }

        return new ContentRename
        {
            Original = original,
            Rename = new(targetPath),
            Item = leaf
        };
    }

    public async Task<MediaFile> ExecuteRenameAsync(
        ContentRename rename,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        using var paths = fileSystem.AcquireImportPaths([rename.Original.FullName, rename.Rename.FullName]);
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

        return await MoveAndRecordRenameAsync(rename, userId, correlationId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MediaFile> MoveAndRecordRenameAsync(
        ContentRename rename,
        Guid? userId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var samePath = fileSystem.PathsEqual(rename.Rename.FullName, rename.Original.FullName);

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
                "The rename engine failed to persist the rename of media file {OriginalPath} and reverts " +
                "{NewPath} to {OldPath}",
                rename.Original.FullName,
                rename.Rename.FullName,
                rename.Original.FullName);

            try
            {
                if (!samePath)
                {
                    fileSystem.Move(rename.Rename.FullName, rename.Original.FullName);
                }
            }
            catch (Exception revertEx)
            {
                logger.LogError(
                    revertEx,
                    "The revert of the media file rename from {NewPath} to {OldPath} failed and needs " +
                    "manual reconciliation",
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

        using var paths = fileSystem.AcquireImportPaths(plan.FileMoves
            .SelectMany(move => new[] { move.SourcePath, move.DestinationPath }).ToArray());
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

                        if (!string.Equals(currentDescendant.Path, version.OriginalPath, StringComparison.Ordinal) ||
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
                "The content path change for {ContentHierarchyItemId} failed after moving {FileCount} " +
                "files and restores them",
                contentHierarchyItemId,
                completedMoves.Count);
            RevertPathMoves(completedMoves);
            throw;
        }

        return true;
    }

    public async Task DeleteFilesAsync(
        IReadOnlyCollection<ContentHierarchyItem> items,
        CancellationToken cancellationToken = default)
    {
        var abandoned = new List<MediaFile>();
        foreach (var file in items.SelectMany(item => item.ContentItem?.MediaFiles ?? []))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.Coverage.Count > 0 &&
                await mediaFileRepository.GetByIdAsync(file.Id, cancellationToken).ConfigureAwait(false) is not null)
            {
                continue;
            }

            try
            {
                using var paths = fileSystem.AcquireImportPaths([file.FilePath]);
                if (!fileSystem.FileExists(file.FilePath))
                {
                    logger.LogWarning("Media file {MediaFileId} is missing from {FilePath}", file.Id, file.FilePath);
                    continue;
                }

                fileSystem.DeleteFile(file.FilePath);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Could not delete media file {MediaFileId} from {FilePath}",
                    file.Id,
                    file.FilePath);
                abandoned.Add(file);
            }
        }

        if (abandoned.Count == 0)
        {
            return;
        }

        await auditRecordRepository.CreateAsync(
            new AuditRecord
            {
                Category = AuditCategories.Content,
                Operation = "FileDeleteFailed",
                EntityType = nameof(MediaFile),
                EntityId = abandoned[0].Id.ToString(),
                Detail = $"Deleted the content rows but could not delete {abandoned.Count} media files: " +
                    string.Join(", ", abandoned.Take(10).Select(file => file.FilePath)),
                Timestamp = timeProvider.GetUtcNow().UtcDateTime
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static string ToMediaFilePathKey(string filePath) =>
        OperatingSystem.IsWindows() ? filePath.ToUpperInvariant() : filePath;

    private static bool IsPathUniqueViolation(Exception exception)
    {
        for (var current = (Exception?)exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("IX_content_mediaFile_FilePath", StringComparison.Ordinal) ||
                current.Message.Contains("IX_content_mediaFile_PathKey", StringComparison.Ordinal) ||
                current.Message.Contains("content_mediaFile.FilePath", StringComparison.Ordinal) ||
                current.Message.Contains("content_mediaFile.PathKey", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Guid> ItemsOf(MediaFile file) =>
        file.Coverage.Select(coverage => coverage.ContentItemId).Prepend(file.ContentItemId);

    private static MediaFile SetItems(MediaFile file, IReadOnlyList<Guid> contentItemIds)
    {
        file.ContentItemId = contentItemIds[0];
        file.Coverage.Clear();
        foreach (var contentItemId in contentItemIds.Skip(1))
        {
            file.Coverage.Add(new() { MediaFileId = file.Id, ContentItemId = contentItemId });
        }

        return file;
    }

    private static string? ApplyModifier(string value, string modifier)
    {
        if (modifier.Length == 0)
        {
            return value;
        }

        return modifier switch
        {
            "lower" => value.ToLowerInvariant(),
            "upper" => value.ToUpperInvariant(),
            _ when modifier.Trim('0').Length == 0 &&
                int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) =>
                number.ToString(modifier, CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static string? ApplyRangeModifier(string value, string modifier) =>
        value.Split('-') is [var first, var last] &&
        ApplyModifier(first, modifier) is { } start &&
        ApplyModifier(last, modifier) is { } end
            ? $"{start}-{end}"
            : null;

    private static bool IsWindowsReservedName(string segment) =>
        WindowsReservedNames.Contains(segment.Split('.')[0].TrimEnd());

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
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(template.Length);
        var segment = new StringBuilder(template.Length);
        var emptied = false;

        for (var i = 0; i < template.Length; i++)
        {
            var ch = template[i];

            if (ch is '/' or '\\')
            {
                builder.Append(FinishSegment(segment.ToString(), emptied, false));
                builder.Append(ch);
                segment.Clear();
                emptied = false;
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
                    if (tokens.Required)
                    {
                        throw new ArgumentException("The rename mask contains an unclosed token.");
                    }

                    segment.Append(ch);
                    continue;
                }

                var rendered = RenderToken(template[(i + 1)..close], tokens);
                emptied |= rendered.Length == 0;
                segment.Append(rendered);
                i = close;
                continue;
            }

            segment.Append(ch);
        }

        builder.Append(FinishSegment(segment.ToString(), emptied, true));
        return builder.ToString();
    }

    private static string FinishSegment(string segment, bool emptied, bool last)
    {
        if (!emptied)
        {
            return TrimSegment(segment);
        }

        var dot = last ? segment.LastIndexOf('.') : -1;
        var extension = dot >= 0 ? segment[dot..] : string.Empty;
        var stem = dot >= 0 ? segment[..dot] : segment;
        string previous;
        do
        {
            previous = stem;
            stem = EmptyBracketPattern.Replace(stem, " ");
        }
        while (stem != previous);

        stem = RepeatedDashPattern.Replace(stem, " - ");
        stem = RepeatedSeparatorPattern.Replace(stem, match => match.Value[0] == ' ' ? " " : match.Groups[1].Value);
        stem = stem.Trim(' ', '-', '_', '.');
        return TrimSegment(stem.Length == 0 ? string.Empty : stem + extension);
    }

    private static IReadOnlyList<(string Path, IReadOnlyList<ContentPathPlanDirectory>? Directories)>
        BuildRelativePaths(
            IReadOnlyList<ContentHierarchyItem> leafToRoot,
            IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> relationshipsByNode,
            IPathResolutionContentTypeDefinitionProvider? pathProvider,
            string? renameTemplate,
            IReadOnlyList<string> fileNames,
            ILogger logger,
            bool requiredTokens = false,
            IReadOnlyList<RenameTokenDefinition>? tokenDefinitions = null,
            MediaInfo? mediaInfo = null,
            IReadOnlyList<ContentHierarchyItem>? covered = null)
    {
        if (leafToRoot.Count == 0)
        {
            return [];
        }

        var ancestors = ToDisplayAncestors(leafToRoot);
        var leafFolderName = ancestors[^1].Name;
        var flatLeafFolder = YearOf(ancestors[^1]) is { } year ? $"{leafFolderName} ({year})" : leafFolderName;
        var paths = new List<(string Path, IReadOnlyList<ContentPathPlanDirectory>? Directories)>(fileNames.Count);
        foreach (var fileName in fileNames)
        {
            var tokens = ResolveRenameTokens(tokenDefinitions ?? FixedRenameTokens,
                new RenameTokenSource(
                    ancestors,
                    relationshipsByNode,
                    mediaInfo,
                    Path.GetFileName(fileName),
                    covered ?? []));
            paths.Add(BuildRelativePath(
                ancestors,
                relationshipsByNode,
                flatLeafFolder,
                tokens,
                pathProvider,
                renameTemplate,
                fileName,
                logger,
                requiredTokens));
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
            if (tokens.Required)
            {
                throw new ArgumentException("The rename mask has an empty token.");
            }

            return string.Empty;
        }

        if (!tokens.Tokens.TryGetValue(name, out var raw) || tokens.Required && string.IsNullOrWhiteSpace(raw))
        {
            if (!tokens.Required && !tokens.Tokens.ContainsKey(name))
            {
                tokens.UnknownTokens?.Add(name);
            }

            if (tokens.Required)
            {
                throw new ArgumentException(tokens.Tokens.ContainsKey(name)
                    ? $"The rename token '{name}' has no value."
                    : $"The rename token '{name}' is not registered.");
            }

            return string.Empty;
        }

        var value = raw ?? string.Empty;
        var modified = ApplyModifier(value, modifier) ??
            (name.EndsWith("." + LevelNumberToken, StringComparison.OrdinalIgnoreCase)
                ? ApplyRangeModifier(value, modifier)
                : null);
        if (modified is null && tokens.Required)
        {
            throw new ArgumentException($"The rename token '{name}' does not support the modifier '{modifier}'.");
        }

        return Sanitize(modified ?? value);
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

    private static (string Path, IReadOnlyList<ContentPathPlanDirectory>? Directories) BuildRelativePath(
        IReadOnlyList<ContentHierarchyItem> ancestors,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> relationshipsByNode,
        string flatLeafFolder,
        IReadOnlyDictionary<string, string?> leafTemplateTokens,
        IPathResolutionContentTypeDefinitionProvider? pathProvider,
        string? renameTemplate,
        string fileName,
        ILogger logger,
        bool requiredTokens = false)
    {
        var leafFileName = Path.GetFileName(fileName);
        var rawExtension = Path.GetExtension(leafFileName);
        var extension = string.IsNullOrEmpty(rawExtension) ? string.Empty : rawExtension.TrimStart('.');
        var originalFileName = Path.GetFileNameWithoutExtension(leafFileName);

        if (pathProvider is not null)
        {
            ContentPathPlan? plan = null;
            try
            {
                plan = pathProvider.BuildPathPlan(ancestors, relationshipsByNode, originalFileName, extension);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "The content path provider {Provider} failed and the rename engine uses the fallback path",
                    pathProvider.GetType().FullName);
            }

            if (plan is not null && !string.IsNullOrWhiteSpace(plan.FileName))
            {
                var directories = plan.Directories
                    .Select(directory => new ContentPathPlanDirectory
                    {
                        LevelSlug = directory.LevelSlug,
                        Name = NormalizeContentPath(directory.Name.NormalizeContentPathSegment())
                    })
                    .Where(directory => directory.Name.Length > 0)
                    .ToArray();
                var joined = string.Join(
                    '/',
                    directories
                        .Select(directory => directory.Name)
                        .Append(plan.FileName.NormalizeContentPathSegment()));
                return (NormalizeContentPath(joined), directories);
            }
        }

        if (!string.IsNullOrWhiteSpace(renameTemplate))
        {
            var unknown = new List<string>();
            var rendered = ApplyTemplate(renameTemplate,
                new RenameTokenSet { Tokens = leafTemplateTokens, Required = requiredTokens, UnknownTokens = unknown });
            foreach (var token in unknown.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "The rename token {Token} in mask {Mask} is not registered, so the placement renders it empty",
                    token,
                    renameTemplate);
            }

            if (!string.IsNullOrWhiteSpace(requiredTokens ? rendered : rendered.Split('/', '\\')[^1]))
            {
                return (requiredTokens ? rendered : BuildFallbackPathResult(rendered), []);
            }
        }

        return (BuildFallbackPathResult(flatLeafFolder.NormalizeContentPathSegment() + "/" + fileName), null);
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
            var sanitized = string.Join(
                ' ',
                new string(sanitizedCharacters)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
            if (sanitized.Length > MaxSegmentLength)
            {
                sanitized = sanitized[..MaxSegmentLength].TrimEnd(' ', '.');
            }

            if (IsWindowsReservedName(sanitized))
            {
                var dot = sanitized.IndexOf('.', StringComparison.Ordinal);
                sanitized = dot < 0
                    ? sanitized + SanitizationReplacement
                    : sanitized.Insert(dot, SanitizationReplacement.ToString());
            }

            if (sanitized.Length > 0)
            {
                segments.Add(sanitized);
            }
        }

        return segments.Count == 0 ? string.Empty : Path.Combine([.. segments]);
    }

    private static Dictionary<Guid, string?> BuildPathUpdatesFromPlan(
        IReadOnlyList<ContentHierarchyItem> leafToRoot,
        IReadOnlyList<ContentPathPlanDirectory> planDirectories,
        out bool hasOwnedDirectory)
    {
        hasOwnedDirectory = false;
        var updates = new Dictionary<Guid, string?>();
        foreach (var item in leafToRoot)
        {
            var directory = planDirectories.FirstOrDefault(
                candidate => string.Equals(candidate.LevelSlug, item.Level, StringComparison.Ordinal));
            if (directory is null)
            {
                continue;
            }

            hasOwnedDirectory = true;
            var origin = item.Settings.TryGetValue("content.path.origin", out var value) ? value : null;
            if (!(string.Equals(origin, "Generated", StringComparison.OrdinalIgnoreCase) ||
                    origin is null && item.Path is null))
            {
                continue;
            }

            updates[item.Id] = directory.Name;
        }

        return updates;
    }

    private Dictionary<Guid, string?> BuildPathUpdates(
        IReadOnlyList<ContentHierarchyItem> leafToRoot,
        string relativeFilePath,
        IReadOnlyList<ContentPathPlanDirectory>? planDirectories,
        out bool hasOwnedDirectory)
    {
        if (planDirectories is not null)
        {
            return BuildPathUpdatesFromPlan(leafToRoot, planDirectories, out hasOwnedDirectory);
        }

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
        return (await mediaFileRepository
            .QueryAsync(
                new() { FilePaths = [filePath] },
                new() { PageNumber = 1, PageSize = 2 },
                null,
                cancellationToken)
            .ConfigureAwait(false)).Items.FirstOrDefault()
            ?? throw new InvalidOperationException("The media file does not exist.");
    }

    private async Task<ContentItem> GetOrCreateContentItemAsync(
        Guid contentHierarchyItemId,
        DateTime now,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var existing = (await contentRepository.QueryAsync(
            new ContentItemQuery { ContentHierarchyItemId = contentHierarchyItemId },
            new() { PageNumber = 1, PageSize = 2 },
            null,
            cancellationToken).ConfigureAwait(false)).Items.SingleOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        if (await contentHierarchyItemRepository
            .GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false) is null)
        {
            throw new InvalidOperationException($"Content hierarchy item {contentHierarchyItemId} does not exist.");
        }

        var contentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = contentHierarchyItemId,
            Fulfillment = FulfillmentStatus.Missing,
            CreatedAt = now,
            CreatedByUserId = userId
        };

        try
        {
            await contentRepository.CreateAsync(contentItem, userId, cancellationToken).ConfigureAwait(false);
            return contentItem;
        }
        catch (DbUpdateException)
        {
            existing = (await contentRepository.QueryAsync(
                new ContentItemQuery { ContentHierarchyItemId = contentHierarchyItemId },
                new() { PageNumber = 1, PageSize = 2 },
                null,
                cancellationToken).ConfigureAwait(false)).Items.SingleOrDefault();

            if (existing is null)
            {
                throw;
            }

            return existing;
        }
    }

    private async Task<MediaFile?> GetOwnedOrCoveredMediaFileAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        CancellationToken cancellationToken)
    {
        var target = await mediaFileRepository.GetByIdAsync(mediaFileId, cancellationToken).ConfigureAwait(false);
        return target is not null &&
            await ItemHasFileAsync(target, contentHierarchyItemId, cancellationToken).ConfigureAwait(false)
                ? target
                : null;
    }

    private async Task<bool> ItemHasFileAsync(
        MediaFile file,
        Guid contentHierarchyItemId,
        CancellationToken cancellationToken)
    {
        foreach (var contentItemId in ItemsOf(file))
        {
            var contentItem = await contentRepository.GetByIdAsync(contentItemId, cancellationToken)
                .ConfigureAwait(false);
            if (contentItem?.ContentHierarchyItemId == contentHierarchyItemId)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<IReadOnlyList<ContentHierarchyItem>> GetCoveredItemsAsync(
        MediaFile? file,
        IReadOnlyList<ContentHierarchyItem> lineage,
        CancellationToken cancellationToken)
    {
        if (file is not { Coverage.Count: > 0 } || lineage.Count < 2 ||
            file.ContentItemId != lineage[0].ContentItem?.Id)
        {
            return [];
        }

        var siblings = await contentHierarchyItemRepository.QueryAllAsync(
            new ContentHierarchyItemCriteria
            {
                ParentId = new() { And = [new() { Operator = CriteriaValueOperator.Equals, Value = lineage[1].Id }] }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return [.. siblings.Where(sibling => sibling.ContentItem is { } item &&
            file.Coverage.Any(coverage => coverage.ContentItemId == item.Id))];
    }

    private async Task<bool> IsSiblingOfOwnerAsync(
        ContentHierarchyItem item,
        MediaFile file,
        CancellationToken cancellationToken) =>
        item.ParentId is { } parentId && (await contentHierarchyItemRepository.QueryIdsAsync(
            new ContentHierarchyItemCriteria
            {
                ContentItemId = file.ContentItemId,
                ParentId = new() { And = [new() { Operator = CriteriaValueOperator.Equals, Value = parentId }] }
            },
            new() { PageNumber = 1, PageSize = 1 },
            null,
            cancellationToken).ConfigureAwait(false)).Count > 0;

    private async Task<(bool Removed, bool FulfillmentChanged)> RemoveAssociationAsync(
        Guid contentHierarchyItemId,
        MediaFile target,
        string? auditOperation,
        string? auditDetail,
        bool completeIfAlreadyRemoved,
        bool keepOtherItems,
        Guid? actingUserId,
        CancellationToken cancellationToken)
    {
        var hierarchyItem = await contentHierarchyItemRepository
            .GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        var removed = false;
        var fulfillmentChanged = false;
        await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                await LockFulfillmentLineageAsync(contentHierarchyItemId, ct).ConfigureAwait(false);
                var page = await contentRepository.QueryAsync(
                    new ContentItemQuery { ContentHierarchyItemId = contentHierarchyItemId },
                    new() { PageNumber = 1, PageSize = 2 },
                    null,
                    ct).ConfigureAwait(false);
                var contentItem = page.Items.SingleOrDefault()
                    ?? throw new InvalidOperationException($"Content item {target.ContentItemId} does not exist.");

                var current = keepOtherItems
                    ? await mediaFileRepository.GetByIdAsync(target.Id, ct).ConfigureAwait(false)
                    : null;
                var others = current is null
                    ? []
                    : ItemsOf(current).Where(contentItemId => contentItemId != contentItem.Id).ToList();
                removed = others.Count > 0
                    ? await mediaFileRepository.UpdateAsync(SetItems(current!, others), actingUserId, ct)
                        .ConfigureAwait(false)
                    : await mediaFileRepository.DeleteAsync(target.Id, actingUserId, ct).ConfigureAwait(false);
                if (!removed && !completeIfAlreadyRemoved)
                {
                    return;
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;
                fulfillmentChanged = await UpdateFulfillmentCoreAsync(
                    hierarchyItem,
                    contentItem,
                    now,
                    actingUserId,
                    ct).ConfigureAwait(false);

                if (auditOperation is not null && auditDetail is not null)
                {
                    await auditRecordRepository.CreateAsync(
                        new AuditRecord
                        {
                            Category = AuditCategories.Content,
                            Operation = auditOperation,
                            EntityType = nameof(MediaFile),
                            EntityId = target.Id.ToString(),
                            ParentEntityId = contentHierarchyItemId.ToString(),
                            Detail = auditDetail,
                            Timestamp = now,
                            UserId = actingUserId
                        },
                        ct).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);

        return (removed, fulfillmentChanged);
    }

    private async Task LockFulfillmentLineageAsync(Guid contentHierarchyItemId, CancellationToken cancellationToken)
    {
        var lineage = await contentHierarchyItemRepository
            .GetLineageAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (lineage.Count > 0)
        {
            await contentHierarchyItemRepository
                .LockAsync(lineage[^1].Id, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> UpdateFulfillmentCoreAsync(
        ContentHierarchyItem? hierarchyItem,
        ContentItem contentItem,
        DateTime now,
        Guid? actingUserId,
        CancellationToken cancellationToken)
    {
        var previousFulfillment = contentItem.Fulfillment;
        await contentRepository.ReconcileMediaStateAsync(
            contentItem.ContentHierarchyItemId,
            actingUserId,
            cancellationToken).ConfigureAwait(false);
        var current = await contentRepository.GetByIdAsync(contentItem.Id, cancellationToken).ConfigureAwait(false);
        return current is not null && current.Fulfillment != previousFulfillment;
    }

    private void EnsureTargetWithinRoot(string targetPath, RootFolder rootFolder)
    {
        if (fileSystem.IsPathWithin(rootFolder.Path, targetPath))
        {
            return;
        }

        logger.LogError(
            "The rename engine refused to move target {TargetPath} outside root {RootPath}",
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
            throw new ArgumentException("A content path cannot be empty.", nameof(requestedPath));
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
            pathOverrides: null,
            out var currentTargetRawPath,
            out var currentTargetError);
        var requestedOverrides = new Dictionary<Guid, string?> { [contentHierarchyItemId] = requestedPath };
        var proposedTargetEffectivePath = ResolvePath(
            targetLineage,
            targetRootFolder,
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

        var descendants = await contentHierarchyItemRepository.QueryAllAsync(
            new ContentHierarchyItemCriteria { AncestorId = contentHierarchyItemId },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var hierarchyItems = new List<ContentHierarchyItem>(descendants.Count + 1) { targetLineage[0] };
        hierarchyItems.AddRange(descendants.Where(item => item.Id != contentHierarchyItemId));

        var filesByHierarchyItemId = hierarchyItems
            .Where(item => item.ContentItem?.MediaFiles.Count > 0)
            .ToDictionary(item => item.Id, item => (IReadOnlyList<MediaFile>)[.. item.ContentItem!.MediaFiles]);

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
                currentEffectivePath = ResolvePath(lineage, rootFolder, null, out _, out currentError);
                proposedEffectivePath = ResolvePath(lineage, rootFolder, overrides, out _, out proposedError);
            }

            var currentRawPath = item.Id == contentHierarchyItemId ? currentTargetRawPath : item.Path;
            var proposedRawPath = item.Id == contentHierarchyItemId ? proposedTargetRawPath : item.Path;
            filesByHierarchyItemId.TryGetValue(item.Id, out var files);
            var locationChanged = currentEffectivePath is null || proposedEffectivePath is null
                ? currentEffectivePath != proposedEffectivePath
                : !fileSystem.PathsEqual(currentEffectivePath, proposedEffectivePath);
            var isTarget = item.Id == contentHierarchyItemId;
            var rawValueChanged = isTarget && !string.Equals(currentRawPath, proposedRawPath, StringComparison.Ordinal);
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

    private List<PathFileMove> MovePathFiles(IReadOnlyList<PathFileMove> moves, CancellationToken cancellationToken)
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
                    "The content path rollback could not restore {DestinationPath} to {SourcePath} and " +
                    "needs manual reconciliation",
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
                var subfolder = contentTypeRegistry.GetDefinitionFor(root.ContentType)?.LevelDefinitions
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

    /// <inheritdoc />
    public async Task<RenameTokenReference> GetRenameTokenReferenceAsync(
        RenameTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ContentHierarchyItemId is not { } itemId)
        {
            var examples = GetRenameTokenDefinitions(request.ContentType);
            var exampleValues = examples.ToDictionary(
                token => token.Name,
                string? (token) => token.Example,
                StringComparer.OrdinalIgnoreCase);
            return new()
            {
                Tokens = [.. examples.Select(token => ToTokenInfo(token, null))],
                Preview = string.IsNullOrWhiteSpace(request.Template) ? null : PreviewRelative(() =>
                    ApplyTemplate(request.Template, new RenameTokenSet { Tokens = exampleValues, Required = true }))
            };
        }

        var lineage = await contentHierarchyItemRepository.GetLineageAsync(itemId, cancellationToken)
            .ConfigureAwait(false);
        if (lineage.Count == 0)
        {
            throw new KeyNotFoundException("The selected content does not exist.");
        }

        MediaFile? file = null;
        if (request.MediaFileId is { } mediaFileId)
        {
            file = await mediaFileRepository.GetByIdAsync(mediaFileId, cancellationToken).ConfigureAwait(false);
            if (file is null || file.ContentItemId != lineage[0].ContentItem?.Id)
            {
                throw new KeyNotFoundException("The selected file does not belong to the selected content.");
            }
        }

        var folder = await rootFolderRepository.GetByIdAsync(lineage[0].EffectiveRootFolderId, cancellationToken)
            .ConfigureAwait(false) ?? throw new KeyNotFoundException("The root folder does not exist.");
        var relationships = await metadataContainerRepository.GetRelationshipTitlesByContentHierarchyItemAsync(
            [.. lineage.Select(node => node.Id)],
            cancellationToken).ConfigureAwait(false);
        var definitions = GetRenameTokenDefinitions(lineage[0].ContentType);
        var covered = await GetCoveredItemsAsync(file, lineage, cancellationToken).ConfigureAwait(false);
        var values = ResolveRenameTokens(definitions,
            new RenameTokenSource(
                ToDisplayAncestors(lineage),
                relationships,
                file?.MediaInfo,
                file is null ? null : Path.GetFileName(file.FilePath),
                covered));
        var template = request.Template ?? folder.RenameTemplate;
        RenameTokenPreview? preview = null;
        if (!string.IsNullOrWhiteSpace(template))
        {
            var masked = folder with { RenameTemplate = template };
            preview = file is null
                ? PreviewRelative(() => ApplyTemplate(template,
                    new RenameTokenSet { Tokens = values, Required = true }))
                : PreviewRelative(() => Path.GetRelativePath(
                    ResolveMaskBasePath(lineage, masked, relationships, file.FilePath),
                    ResolveMaskPath(lineage, masked, relationships, file.FilePath, file.MediaInfo, covered)));
        }

        return new()
        {
            Tokens = [.. definitions.Select(token => ToTokenInfo(token, values))],
            Preview = preview
        };
    }

    private static RenameTokenInfo ToTokenInfo(
        RenameTokenDefinition token,
        IReadOnlyDictionary<string, string?>? values)
    {
        var value = values?.GetValueOrDefault(token.Name);
        return new()
        {
            Name = token.Name,
            Family = token.Family,
            Description = token.Description,
            Example = token.Example,
            Value = string.IsNullOrWhiteSpace(value) ? null : value,
            Available = values is null ? null : !string.IsNullOrWhiteSpace(value)
        };
    }

    private static RenameTokenPreview PreviewRelative(Func<string> render)
    {
        try
        {
            var relative = ValidateRelativeMaskPath(render()).Replace(Path.DirectorySeparatorChar, '/');
            var slash = relative.LastIndexOf('/');
            return new()
            {
                Directory = slash < 0 ? null : relative[..slash],
                FileName = relative[(slash + 1)..]
            };
        }
        catch (ArgumentException ex)
        {
            return new() { Error = ex.Message };
        }
    }

    private static Dictionary<string, string?> ResolveRenameTokens(
        IReadOnlyList<RenameTokenDefinition> definitions,
        RenameTokenSource source) =>
        definitions.ToDictionary(
            token => token.Name,
            token => token.Resolve(source).NullIfBlank(),
            StringComparer.OrdinalIgnoreCase);

    private static List<ContentHierarchyItem> ToDisplayAncestors(IReadOnlyList<ContentHierarchyItem> leafToRoot)
    {
        var ancestors = new List<ContentHierarchyItem>(leafToRoot.Count);
        for (var i = leafToRoot.Count - 1; i >= 0; i--)
        {
            var item = leafToRoot[i];
            var metadataTitle = FirstContribution(item, contribution => contribution.Title);
            ancestors.Add(item with
            {
                Name = ResolveDisplayTitle(item.PendingMetadataRefresh, item.Name, metadataTitle) ?? item.Slug
            });
        }

        return ancestors;
    }

    private static string? FirstContribution(ContentHierarchyItem item, Func<MetadataContribution, string?> field) =>
        item.MetadataContainer?.Contributions.OrderBy(contribution => contribution.Ordinal)
            .Select(field).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? YearOf(ContentHierarchyItem item) =>
        item.MetadataContainer?.Contributions.OrderBy(contribution => contribution.Ordinal)
            .Select(contribution => contribution.PublishDate?.Year)
            .FirstOrDefault(year => year is not null)?.ToString(CultureInfo.InvariantCulture);

    private static ContentHierarchyItem? LevelOf(RenameTokenSource source, string level) =>
        source.RootToLeaf.LastOrDefault(node => string.Equals(node.Level, level, StringComparison.Ordinal));

    private static string? NumberRange(IEnumerable<ContentHierarchyItem> items)
    {
        var numbers = items.Select(SequenceNumberOf).ToArray();
        if (numbers.Length == 0 || numbers.Any(number => number is null))
        {
            return null;
        }

        var minimum = numbers.Min()!.Value;
        var maximum = numbers.Max()!.Value;
        if (numbers.Distinct().Count() != numbers.Length || (long)maximum - minimum + 1 != numbers.Length)
        {
            return null;
        }

        return minimum == maximum ? minimum.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{minimum}-{maximum}");
    }

    private static int? SequenceNumberOf(ContentHierarchyItem item) =>
        item.MetadataContainer?.Contributions.OrderBy(contribution => contribution.Ordinal)
            .Select(contribution => contribution.SequenceNumber).FirstOrDefault(number => number is not null);

    private static MediaInfoTrack? FirstTrack(RenameTokenSource source, string type) =>
        source.MediaInfo?.Tracks.FirstOrDefault(track => track.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    private static string? TrackLanguages(RenameTokenSource source, string type) =>
        JoinCollection(
            source.MediaInfo?.Tracks
                .Where(track => track.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
                .Select(track => LanguageCode(track.Language))
                .OfType<string>() ?? [],
            LanguageSeparator);

    private static string? LanguageCode(string? language) =>
        string.IsNullOrWhiteSpace(language) || language.Trim().Equals("und", StringComparison.OrdinalIgnoreCase)
            ? null
            : (language.NormalizeTwoLetterLanguageCode() ?? language.Trim()).ToUpperInvariant();

    private static string? RelatedTitles(RenameTokenSource source, string role)
    {
        for (var i = source.RootToLeaf.Count - 1; i >= 0; i--)
        {
            if (source.Relationships.TryGetValue(source.RootToLeaf[i].Id, out var byRole) &&
                byRole.FirstOrDefault(pair => string.Equals(pair.Key, role, StringComparison.OrdinalIgnoreCase))
                    .Value is { Count: > 0 } titles)
            {
                return JoinCollection(titles, CollectionSeparator);
            }
        }

        return null;
    }

    private static string? JoinCollection(IEnumerable<string> values, string separator)
    {
        var distinct = values.Select(value => value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return distinct.Length == 0 ? null : string.Join(separator, distinct);
    }

    private IReadOnlyList<RenameTokenDefinition> GetRenameTokenDefinitions(string? contentType)
    {
        var key = contentType ?? string.Empty;
        if (_renameTokenDefinitions.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var definitions = new List<RenameTokenDefinition>(FixedRenameTokens);
        var levels = contentType is null ? null : contentTypeRegistry.GetDefinitionFor(contentType)?.LevelDefinitions;
        foreach (var level in levels ?? [])
        {
            var prefix = char.ToUpperInvariant(level.Slug[0]) + level.Slug[1..] + ".";
            var label = level.DisplayName ?? level.Slug;
            definitions.Add(new(
                prefix + "Title",
                RenameTokenFamily.Hierarchy,
                $"The title of the {label} level that contains the file.",
                $"Example {label}",
                source => LevelOf(source, level.Slug)?.Name));
            definitions.Add(new(
                prefix + "Year",
                RenameTokenFamily.Hierarchy,
                $"The publish year of the {label} level that contains the file.",
                "2024",
                source => LevelOf(source, level.Slug) is { } node ? YearOf(node) : null));
            definitions.Add(new(
                prefix + LevelNumberToken,
                RenameTokenFamily.Hierarchy,
                $"The number of the {label} level, or the range of numbers that the file covers. " +
                "Use :00 to pad the number.",
                "1",
                source => LevelOf(source, level.Slug) is { } node
                    ? node == source.Leaf && source.Covered.Count > 0
                        ? NumberRange(source.Covered.Append(node))
                        : SequenceNumberOf(node)?.ToString(CultureInfo.InvariantCulture)
                    : null));
        }

        var names = new HashSet<string>(definitions.Select(token => token.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var role in GetRelationshipRoles())
        {
            if (!names.Add(role.Slug))
            {
                logger.LogWarning(
                    "The relationship role {Role} has the same name as another rename token; the role has no token",
                    role.Slug);
                continue;
            }

            definitions.Add(new(
                role.Slug,
                RenameTokenFamily.Relationship,
                role.Description ?? $"The related subjects in the {role.DisplayName} role, separated by spaces.",
                role.Examples is { Count: > 0 } examples
                    ? string.Join(CollectionSeparator, examples)
                    : $"{role.DisplayName} One{CollectionSeparator}{role.DisplayName} Two",
                source => RelatedTitles(source, role.Slug)));
        }

        return _renameTokenDefinitions[key] = definitions;
    }

    private IEnumerable<RelationshipRoleDefinition> GetRelationshipRoles()
    {
        var roles = new Dictionary<string, (RelationshipRoleDefinition Role, string Kind)>(StringComparer.Ordinal);
        foreach (var kind in subjectKindRegistry.Definitions)
        {
            foreach (var role in kind.RelationshipRoles ?? [])
            {
                if (string.IsNullOrWhiteSpace(role?.Slug))
                {
                    continue;
                }

                if (roles.TryGetValue(role.Slug, out var existing))
                {
                    logger.LogWarning(
                        "Subject kinds {Displaced} and {Winner} both declared relationship role {Role}; the rename " +
                        "tokens used the last declaration",
                        existing.Kind,
                        kind.Slug,
                        role.Slug);
                }

                roles[role.Slug] = (role, kind.Slug);
            }
        }

        return roles.Values.Select(entry => entry.Role).OrderBy(role => role.Slug, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<bool> HasRenameMaskAsync(Guid rootFolderId, CancellationToken cancellationToken = default)
    {
        var folder = await rootFolderRepository.GetByIdAsync(rootFolderId, cancellationToken).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(folder?.RenameTemplate);
    }

    /// <inheritdoc />
    public async Task<FileRenamePreview> PreviewImportRenameMaskAsync(
        IReadOnlyList<ContentHierarchyItem> lineage,
        IReadOnlyList<ContentHierarchyItem> covered,
        string filePath,
        CancellationToken cancellationToken = default,
        IReadOnlyList<FileRenameEntry>? otherPlannedFiles = null)
    {
        var folder = await rootFolderRepository.GetByIdAsync(lineage[0].EffectiveRootFolderId, cancellationToken)
            .ConfigureAwait(false) ?? throw new ArgumentException("The root folder does not exist.");
        var leafOwnsPath = contentTypeRegistry.GetDefinitionFor(lineage[0].ContentType)?.LevelDefinitions
            .FirstOrDefault(level => level.Slug == lineage[0].Level)?.Subfolder is not null;
        var ownerIndex = leafOwnsPath || lineage.Count == 1 ? 0 : 1;
        var owner = lineage[ownerIndex];
        var origin = owner.Settings.GetValueOrDefault("content.path.origin");
        if (string.Equals(origin, "Generated", StringComparison.OrdinalIgnoreCase) ||
            origin is null && owner.Path is null)
        {
            lineage = lineage.Select((item, index) => index == ownerIndex
                ? item with { Path = Path.GetDirectoryName(fileSystem.GetFullPath(filePath)) }
                : item).ToArray();
        }

        var entry = new FileRenameEntry { OriginalPath = filePath, ContentHierarchyItemId = lineage[0].Id };
        try
        {
            var target = ResolveMaskPath(
                lineage,
                folder,
                new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                filePath,
                null,
                covered);
            entry = entry with { DestinationPath = target, Error = ValidateMove(filePath, target) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            entry = entry with { Error = ex.Message };
        }

        var files = await PreviewAssociatedSidecarsAsync(
            lineage[0],
            entry,
            cancellationToken,
            lineage,
            otherPlannedFiles)
            .ConfigureAwait(false);
        return new() { HasMask = !string.IsNullOrWhiteSpace(folder.RenameTemplate), Files = files };
    }

    /// <inheritdoc />
    public async Task<FileRenamePreview> PreviewRenameMaskAsync(
        Guid contentHierarchyItemId,
        FileRenameSelection selection,
        CancellationToken cancellationToken = default)
    {
        var root = await contentHierarchyItemRepository.GetByIdAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false) ?? throw new KeyNotFoundException("The selected content does not exist.");
        var entries = new List<FileRenameEntry>();
        var hasMask = false;
        if (selection.FilePath is { } filePath)
        {
            var folder = await rootFolderRepository.GetByIdAsync(root.EffectiveRootFolderId, cancellationToken)
                .ConfigureAwait(false);
            hasMask = !string.IsNullOrWhiteSpace(folder?.RenameTemplate);
            var entry = await PreviewMaskFileAsync(root, null, filePath, cancellationToken).ConfigureAwait(false);
            entries.AddRange(await PreviewAssociatedSidecarsAsync(root, entry, cancellationToken)
                .ConfigureAwait(false));
        }
        else
        {
            var descendants = await contentHierarchyItemRepository.QueryAllAsync(
                new ContentHierarchyItemCriteria { AncestorId = contentHierarchyItemId },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var item in descendants.Prepend(root).DistinctBy(item => item.Id))
            {
                var files = item.ContentItem?.MediaFiles ?? [];
                var selected = files.Where(file => selection.MediaFileIds is null ||
                        selection.MediaFileIds.Contains(file.Id))
                    .ToArray();
                if (selected.Length == 0)
                {
                    continue;
                }

                var folder = await rootFolderRepository.GetByIdAsync(item.EffectiveRootFolderId, cancellationToken)
                    .ConfigureAwait(false);
                hasMask |= !string.IsNullOrWhiteSpace(folder?.RenameTemplate);
                var level = contentTypeRegistry.GetDefinitionFor(item.ContentType)?.LevelDefinitions
                    .FirstOrDefault(level => level.Slug == item.Level);
                var primary = new List<MediaFile>();
                var sidecars = new List<MediaFile>();
                foreach (var file in files)
                {
                    var isPrimary = level is not null && await fileSystem.MatchesAnyFileTypeAsync(
                        file.FilePath,
                        level.AcceptedFileTypes,
                        cancellationToken).ConfigureAwait(false);
                    var isSidecar = !isPrimary && level is not null && await fileSystem.MatchesAnyFileTypeAsync(
                        file.FilePath,
                        level.AcceptedSidecarTypes,
                        cancellationToken).ConfigureAwait(false);
                    (isSidecar ? sidecars : primary).Add(file);
                }

                var plans = new List<FileRenameEntry>();
                foreach (var file in primary.Where(file => selected.Contains(file)))
                {
                    plans.Add(await PreviewMaskFileAsync(item, file.Id, file.FilePath, cancellationToken)
                        .ConfigureAwait(false));
                }

                entries.AddRange(plans);
                var destinations = primary.Select(file =>
                    plans.FirstOrDefault(plan => plan.MediaFileId == file.Id) is { } plan
                        ? plan.Error is null ? Path.GetDirectoryName(plan.DestinationPath) : null
                        : Path.GetDirectoryName(file.FilePath)).ToArray();
                var destination = destinations.FirstOrDefault();
                var unambiguous = destination is not null && destinations.All(path =>
                    path is not null && fileSystem.PathsEqual(destination, path));
                foreach (var file in sidecars.Where(file => plans.Count > 0 || selected.Contains(file)))
                {
                    var error = unambiguous ? null :
                        "The associated primary files do not have one unambiguous destination directory.";
                    var target = unambiguous ? Path.Combine(destination!, Path.GetFileName(file.FilePath)) : null;
                    entries.Add(new FileRenameEntry
                    {
                        MediaFileId = file.Id,
                        ContentHierarchyItemId = item.Id,
                        OriginalPath = file.FilePath,
                        DestinationPath = target,
                        IsSidecar = true,
                        Error = error ?? ValidateMove(file.FilePath, target!)
                    });
                }
            }
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.IsSidecar && entry.Error is null && entry.DestinationPath is { } destination)
            {
                entries[index] = entry = entry with
                {
                    Error = await ValidateSidecarBoundaryAsync(
                        entry.ContentHierarchyItemId,
                        entry.OriginalPath,
                        destination,
                        cancellationToken).ConfigureAwait(false)
                };
            }

            if (entry.DestinationPath is not null && entries.Any(other =>
                other != entry && other.DestinationPath is not null &&
                fileSystem.PathsEqual(entry.DestinationPath, other.DestinationPath)))
            {
                entries[index] = entry with { Error = "Multiple selected files have the same destination." };
            }
        }

        return new() { HasMask = hasMask, Files = entries };
    }

    private static string ValidateRelativeMaskPath(string rendered)
    {
        var relative = rendered.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Any(segment =>
            string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
            segment.IndexOfAny(InvalidPathChars) >= 0 || segment.Length > MaxSegmentLength ||
            segment.EndsWith('.') || segment.EndsWith(' ')))
        {
            throw new ArgumentException("The rename mask produces an invalid relative file path.");
        }

        if (relative.Split(Path.DirectorySeparatorChar).FirstOrDefault(IsWindowsReservedName) is { } reserved)
        {
            throw new ArgumentException($"The rename mask produces the reserved Windows name '{reserved}'.");
        }

        return relative;
    }

    private async Task<FileRenameEntry> PreviewMaskFileAsync(
        ContentHierarchyItem item,
        Guid? mediaFileId,
        string path,
        CancellationToken cancellationToken)
    {
        var entry = new FileRenameEntry
        {
            MediaFileId = mediaFileId,
            ContentHierarchyItemId = item.Id,
            OriginalPath = path
        };
        try
        {
            if (!fileSystem.FileExists(path))
            {
                return entry with { Error = "The source file does not exist." };
            }

            var lineage = await contentHierarchyItemRepository.GetLineageAsync(item.Id, cancellationToken)
                .ConfigureAwait(false);
            var folder = await rootFolderRepository.GetByIdAsync(item.EffectiveRootFolderId, cancellationToken)
                .ConfigureAwait(false) ?? throw new ArgumentException("The root folder does not exist.");
            var relationships = await metadataContainerRepository.GetRelationshipTitlesByContentHierarchyItemAsync(
                [.. lineage.Select(node => node.Id)],
                cancellationToken).ConfigureAwait(false);
            var file = item.ContentItem?.MediaFiles.FirstOrDefault(file => file.Id == mediaFileId);
            var target = ResolveMaskPath(
                lineage,
                folder,
                relationships,
                path,
                file?.MediaInfo,
                await GetCoveredItemsAsync(file, lineage, cancellationToken).ConfigureAwait(false));
            return entry with { DestinationPath = target, Error = ValidateMove(path, target) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return entry with { Error = ex.Message };
        }
    }

    private async Task<IReadOnlyList<FileRenameEntry>> PreviewAssociatedSidecarsAsync(
        ContentHierarchyItem item,
        FileRenameEntry proposed,
        CancellationToken cancellationToken,
        IReadOnlyList<ContentHierarchyItem>? proposedLineage = null,
        IReadOnlyList<FileRenameEntry>? otherPlannedFiles = null)
    {
        var level = contentTypeRegistry.GetDefinitionFor(item.ContentType)?.LevelDefinitions
            .FirstOrDefault(level => level.Slug == item.Level);
        if (level is null)
        {
            return [proposed];
        }

        var proposedIsSidecar = await fileSystem.MatchesAnyFileTypeAsync(
            proposed.OriginalPath,
            level.AcceptedSidecarTypes,
            cancellationToken).ConfigureAwait(false) &&
            !await fileSystem.MatchesAnyFileTypeAsync(
                proposed.OriginalPath,
                level.AcceptedFileTypes,
                cancellationToken).ConfigureAwait(false);
        var primaryDirectories = new List<string?>();
        if (!proposedIsSidecar)
        {
            primaryDirectories.Add(proposed.Error is null ? Path.GetDirectoryName(proposed.DestinationPath) : null);
        }

        var sidecars = new List<MediaFile>();
        var remaining = otherPlannedFiles?.Where(file => !file.IsSidecar &&
            !fileSystem.PathsEqual(file.OriginalPath, proposed.OriginalPath)).ToList() ?? [];
        foreach (var file in item.ContentItem?.MediaFiles ?? [])
        {
            if (fileSystem.PathsEqual(file.FilePath, proposed.OriginalPath))
            {
                continue;
            }

            if (await fileSystem.MatchesAnyFileTypeAsync(file.FilePath, level.AcceptedSidecarTypes, cancellationToken)
                .ConfigureAwait(false) && !await fileSystem.MatchesAnyFileTypeAsync(
                    file.FilePath,
                    level.AcceptedFileTypes,
                    cancellationToken).ConfigureAwait(false))
            {
                sidecars.Add(file);
            }
            else
            {
                var planned = remaining.FirstOrDefault(entry =>
                    fileSystem.PathsEqual(entry.OriginalPath, file.FilePath));
                primaryDirectories.Add(planned is null ? Path.GetDirectoryName(file.FilePath)
                    : planned.Error is null ? Path.GetDirectoryName(planned.DestinationPath) : null);
                if (planned is not null)
                {
                    remaining.Remove(planned);
                }
            }
        }

        primaryDirectories.AddRange(remaining.Select(file =>
            file.Error is null ? Path.GetDirectoryName(file.DestinationPath) : null));
        var directory = primaryDirectories.FirstOrDefault();
        var unambiguous = directory is not null && primaryDirectories.All(path =>
            path is not null && fileSystem.PathsEqual(directory, path));
        var entries = new List<FileRenameEntry>();
        if (proposedIsSidecar)
        {
            var target = unambiguous ? Path.Combine(directory!, Path.GetFileName(proposed.OriginalPath)) : null;
            entries.Add(proposed with
            {
                IsSidecar = true,
                DestinationPath = target,
                Error = target is null
                    ? "The associated primary files do not have one unambiguous destination directory."
                    : ValidateMove(proposed.OriginalPath, target)
            });
        }
        else
        {
            entries.Add(proposed);
        }

        entries.AddRange(sidecars.Select(file => new FileRenameEntry
        {
            MediaFileId = file.Id,
            ContentHierarchyItemId = item.Id,
            OriginalPath = file.FilePath,
            IsSidecar = true,
            DestinationPath = unambiguous ? Path.Combine(directory!, Path.GetFileName(file.FilePath)) : null,
            Error = unambiguous ? ValidateMove(file.FilePath, Path.Combine(directory!, Path.GetFileName(file.FilePath)))
                : "The associated primary files do not have one unambiguous destination directory."
        }));
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.IsSidecar && entry.Error is null && entry.DestinationPath is { } destination)
            {
                entries[index] = entry with
                {
                    Error = await ValidateSidecarBoundaryAsync(
                        item.Id,
                        entry.OriginalPath,
                        destination,
                        cancellationToken,
                        proposedLineage).ConfigureAwait(false)
                };
            }
        }

        return entries;
    }

    private async Task<string?> ValidateSidecarBoundaryAsync(
        Guid itemId,
        string path,
        string target,
        CancellationToken cancellationToken,
        IReadOnlyList<ContentHierarchyItem>? proposedLineage = null)
    {
        try
        {
            var lineage = proposedLineage ?? await contentHierarchyItemRepository
                .GetLineageAsync(itemId, cancellationToken).ConfigureAwait(false);
            var folder = await rootFolderRepository.GetByIdAsync(lineage[0].EffectiveRootFolderId, cancellationToken)
                .ConfigureAwait(false) ?? throw new ArgumentException("The root folder does not exist.");
            var relationships = await metadataContainerRepository.GetRelationshipTitlesByContentHierarchyItemAsync(
                [.. lineage.Select(node => node.Id)],
                cancellationToken).ConfigureAwait(false);
            var basePath = ResolveMaskBasePath(lineage, folder, relationships, path);
            if (!fileSystem.IsPathWithin(basePath, target) || fileSystem.PathsEqual(basePath, target))
            {
                return "The sidecar target escapes the folder-providing parent.";
            }

            ValidateRenameLinks(folder.Path, basePath, path, target);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ex.Message;
        }
    }

    private string ResolveMaskBasePath(
        IReadOnlyList<ContentHierarchyItem> lineage,
        RootFolder folder,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> relationships,
        string path,
        IReadOnlyDictionary<Guid, string?>? pathUpdates = null)
    {
        var leaf = lineage[0];
        var provider = contentTypeRegistry.FindProviderFor(leaf.ContentType) as
            IPathResolutionContentTypeDefinitionProvider;
        var placement = BuildRelativePaths(lineage, relationships, provider, null, [Path.GetFileName(path)], logger)[0];
        var owners = placement.Directories?.Select(directory => directory.LevelSlug).ToHashSet(StringComparer.Ordinal)
            ?? contentTypeRegistry.GetDefinitionFor(leaf.ContentType)?.LevelDefinitions
                .Where(level => level.Subfolder is not null).Select(level => level.Slug)
                .ToHashSet(StringComparer.Ordinal)
            ?? [];
        var ownerIndex = Enumerable.Range(0, lineage.Count)
            .FirstOrDefault(index => owners.Contains(lineage[index].Level), lineage.Count - 1);
        var ownerLineage = lineage.Skip(ownerIndex).ToArray();

        return ResolvePath(ownerLineage, folder, pathUpdates, out _, out var error)
            ?? throw new ArgumentException(error ?? "The folder-providing parent has no path.");
    }

    private string ResolveMaskPath(
        IReadOnlyList<ContentHierarchyItem> lineage,
        RootFolder folder,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> relationships,
        string path,
        MediaInfo? mediaInfo,
        IReadOnlyList<ContentHierarchyItem> covered)
    {
        if (string.IsNullOrWhiteSpace(folder.RenameTemplate))
        {
            throw new ArgumentException("No rename mask is configured for this file.");
        }

        var basePath = ResolveMaskBasePath(lineage, folder, relationships, path);
        var relative = ValidateRelativeMaskPath(BuildRelativePaths(
            lineage,
            relationships,
            null,
            folder.RenameTemplate,
            [Path.GetFileName(path)],
            logger,
            requiredTokens: true,
            GetRenameTokenDefinitions(lineage[0].ContentType),
            mediaInfo,
            covered)[0].Path);
        var target = fileSystem.GetFullPath(Path.Combine(basePath, relative));
        if (!fileSystem.IsPathWithin(basePath, target) || fileSystem.PathsEqual(basePath, target))
        {
            throw new ArgumentException("The rename target escapes the folder-providing parent.");
        }

        ValidateRenameLinks(folder.Path, basePath, path, target);
        return target;
    }

    private void ValidateRenameLinks(string rootPath, string basePath, string path, string target)
    {
        var existingTarget = target;
        while (!fileSystem.FileExists(existingTarget) && !fileSystem.DirectoryExists(existingTarget) &&
            !fileSystem.PathsEqual(existingTarget, basePath))
        {
            existingTarget = Path.GetDirectoryName(existingTarget)
                ?? throw new ArgumentException("The destination has no accessible parent.");
        }

        if (fileSystem.PathContainsLink(basePath, existingTarget) ||
            fileSystem.PathContainsLink(rootPath, path) ||
            fileSystem.PathContainsLink(rootPath, basePath))
        {
            throw new ArgumentException("The rename path contains a link.");
        }
    }

    private string? ValidateMove(string original, string destination)
    {
        if (!fileSystem.FileExists(original))
        {
            return "The source file does not exist.";
        }

        return !fileSystem.PathsEqual(original, destination) &&
            (fileSystem.FileExists(destination) || fileSystem.DirectoryExists(destination))
            ? "The destination already exists."
            : null;
    }

    /// <inheritdoc />
    public async Task<FileRenamePreview> ExecuteRenameMaskAsync(
        Guid contentHierarchyItemId,
        FileRenamePreview preview,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var fresh = await PreviewRenameMaskAsync(
            contentHierarchyItemId,
            new()
            {
                MediaFileIds = [.. preview.Files.Where(file => file.MediaFileId.HasValue)
                    .Select(file => file.MediaFileId!.Value)]
            },
            cancellationToken).ConfigureAwait(false);
        var results = new List<FileRenameEntry>();
        var failedOwners = new HashSet<Guid>();
        foreach (var entry in preview.Files.OrderBy(file => file.IsSidecar))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Error is not null)
            {
                results.Add(entry);
                continue;
            }

            var moved = false;
            IDisposable? paths = null;
            try
            {
                if (entry.IsSidecar && failedOwners.Contains(entry.ContentHierarchyItemId))
                {
                    throw new InvalidOperationException("A primary file move failed; its sidecar was not moved.");
                }

                var current = fresh.Files.FirstOrDefault(file => file.MediaFileId == entry.MediaFileId);
                if (current is null || current.Error is not null || entry.DestinationPath is null ||
                    current.DestinationPath is null || current.IsSidecar != entry.IsSidecar ||
                    current.ContentHierarchyItemId != entry.ContentHierarchyItemId ||
                    !fileSystem.PathsEqual(current.OriginalPath, entry.OriginalPath) ||
                    !fileSystem.PathsEqual(current.DestinationPath, entry.DestinationPath))
                {
                    throw new InvalidOperationException(current?.Error ?? "The rename preview is stale.");
                }

                paths = fileSystem.AcquireImportPaths([entry.OriginalPath, entry.DestinationPath]);
                await transactionManager.ExecuteWithTransactionAsync(
                    async ct =>
                    {
                        await contentHierarchyItemRepository.LockAsync(entry.ContentHierarchyItemId, ct)
                            .ConfigureAwait(false);
                        var media = await mediaFileRepository.GetByIdAsync(entry.MediaFileId!.Value, ct)
                            .ConfigureAwait(false);
                        var item = await contentHierarchyItemRepository.GetByIdAsync(entry.ContentHierarchyItemId, ct)
                            .ConfigureAwait(false);
                        if (media is null || item?.ContentItem?.Id != media.ContentItemId ||
                            !fileSystem.PathsEqual(media.FilePath, entry.OriginalPath))
                        {
                            throw new InvalidOperationException("The file association changed after preview.");
                        }

                        var checkedPlan = await PreviewRenameMaskAsync(
                            entry.ContentHierarchyItemId,
                            new()
                            {
                                MediaFileIds = [.. preview.Files.Where(file =>
                                    file.ContentHierarchyItemId == entry.ContentHierarchyItemId &&
                                    file.MediaFileId.HasValue)
                                    .Select(file => file.MediaFileId!.Value)]
                            },
                            ct).ConfigureAwait(false);
                        var checkedEntry = checkedPlan.Files
                            .FirstOrDefault(file => file.MediaFileId == entry.MediaFileId);
                        if (checkedEntry?.Error is not null ||
                            checkedEntry?.DestinationPath is not { } checkedDestination ||
                            !fileSystem.PathsEqual(checkedDestination, entry.DestinationPath))
                        {
                            throw new InvalidOperationException(checkedEntry?.Error ?? "The rename preview is stale.");
                        }

                        var rootFolder = await rootFolderRepository.GetByIdAsync(item.EffectiveRootFolderId, ct)
                            .ConfigureAwait(false)
                            ?? throw new InvalidOperationException("The root folder no longer exists.");
                        var existingTarget = entry.DestinationPath;
                        while (!fileSystem.FileExists(existingTarget) && !fileSystem.DirectoryExists(existingTarget))
                        {
                            existingTarget = Path.GetDirectoryName(existingTarget)
                                ?? throw new InvalidOperationException("The destination has no accessible parent.");
                        }

                        if (!fileSystem.IsPathWithin(rootFolder.Path, entry.OriginalPath) ||
                            !fileSystem.IsPathWithin(rootFolder.Path, entry.DestinationPath) ||
                            fileSystem.PathContainsLink(rootFolder.Path, entry.OriginalPath) ||
                            fileSystem.PathContainsLink(rootFolder.Path, existingTarget))
                        {
                            throw new InvalidOperationException(
                                "The file path is outside its root or contains a link.");
                        }

                        var error = ValidateMove(entry.OriginalPath, entry.DestinationPath);
                        if (error is not null)
                        {
                            throw new InvalidOperationException(error);
                        }

                        await MoveAndRecordRenameAsync(
                            new ContentRename
                            {
                                Item = item,
                                Original = new(entry.OriginalPath),
                                Rename = new(entry.DestinationPath)
                            },
                            userId,
                            null,
                            ct).ConfigureAwait(false);
                        moved = !fileSystem.PathsEqual(entry.OriginalPath, entry.DestinationPath);
                    },
                    cancellationToken).ConfigureAwait(false);
                results.Add(entry with { Completed = true });
            }
            catch (Exception ex)
            {
                if (moved)
                {
                    try
                    {
                        fileSystem.Move(entry.DestinationPath!, entry.OriginalPath);
                    }
                    catch (Exception rollbackException)
                    {
                        logger.LogError(rollbackException,
                            "The file rename rollback failed for {FilePath}; manual reconciliation is required",
                            entry.OriginalPath);
                    }
                }

                if (ex is OperationCanceledException)
                {
                    throw;
                }

                logger.LogWarning(ex, "The explicit rename of {FilePath} failed", entry.OriginalPath);
                failedOwners.Add(entry.ContentHierarchyItemId);
                results.Add(entry with { Error = ex.Message });
            }
            finally
            {
                paths?.Dispose();
            }
        }

        return new() { HasMask = fresh.HasMask, Files = results };
    }

    /// <inheritdoc />
    public async Task<bool> ReassignAsync(
        Guid contentHierarchyItemId,
        Guid mediaFileId,
        ReassignContentFileRequest request,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        var lineage = await contentHierarchyItemRepository.GetLineageAsync(contentHierarchyItemId, cancellationToken)
            .ConfigureAwait(false);
        if (lineage.Count == 0)
        {
            return false;
        }

        return await transactionManager.ExecuteWithTransactionAsync(
            async ct =>
            {
                await contentHierarchyItemRepository.LockAsync(lineage[^1].Id, ct).ConfigureAwait(false);
                var sourceLineage = await contentHierarchyItemRepository.GetLineageAsync(contentHierarchyItemId, ct)
                    .ConfigureAwait(false);
                var targetLineage = await contentHierarchyItemRepository
                    .GetLineageAsync(request.TargetContentHierarchyItemId, ct).ConfigureAwait(false);
                if (sourceLineage.Count == 0 || targetLineage.Count == 0)
                {
                    return false;
                }

                if (sourceLineage[^1].Id != lineage[^1].Id || targetLineage[^1].Id != lineage[^1].Id)
                {
                    throw new ArgumentException("The destination is not in the same content hierarchy.");
                }

                var file = await mediaFileRepository.GetByIdAsync(mediaFileId, ct).ConfigureAwait(false);
                if (file is null)
                {
                    return false;
                }

                var items = ItemsOf(file).ToList();
                var source = sourceLineage[0].ContentItem;
                var sourceHasFile = source is not null && items.Contains(source.Id);
                var targetHasFile = targetLineage[0].ContentItem is { } targetItem && items.Contains(targetItem.Id);
                if (!string.Equals(file.FilePath, request.ExpectedFilePath, StringComparison.Ordinal) ||
                    !sourceHasFile && !targetHasFile)
                {
                    throw new ArgumentException("The file path or association changed.");
                }

                var target = targetLineage[0];
                if (!sourceHasFile || target.Id == sourceLineage[0].Id)
                {
                    return true;
                }

                if (items.Count > 1 && target.ParentId != sourceLineage[0].ParentId)
                {
                    throw new ArgumentException(
                        "The destination is not a sibling of the other items that the file covers.");
                }

                var rootFolder = await rootFolderRepository.GetByIdAsync(target.EffectiveRootFolderId, ct)
                    .ConfigureAwait(false);
                var level = contentTypeRegistry.GetDefinitionFor(target.ContentType)?.LevelDefinitions
                    .FirstOrDefault(definition => definition.Slug == target.Level);
                if (rootFolder is null || level is null ||
                    !fileSystem.IsPathWithin(rootFolder.Path, file.FilePath) ||
                    !fileSystem.FileExists(file.FilePath) ||
                    fileSystem.PathContainsLink(rootFolder.Path, file.FilePath))
                {
                    throw new ArgumentException("The file is not accessible in the destination root folder.");
                }

                if (level.AcceptedFileTypes.Count == 0 ||
                    !await fileSystem.MatchesAnyFileTypeAsync(file.FilePath, level.AcceptedFileTypes, ct)
                        .ConfigureAwait(false) &&
                    !await fileSystem.MatchesAnyFileTypeAsync(file.FilePath, level.AcceptedSidecarTypes, ct)
                        .ConfigureAwait(false))
                {
                    throw new ArgumentException("The destination does not accept this file type.");
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;
                var destination = await GetOrCreateContentItemAsync(target.Id, now, actingUserId, ct)
                    .ConfigureAwait(false);
                file.ModifiedAt = now;
                if (!await mediaFileRepository.UpdateAsync(
                        SetItems(file, [.. items.Where(id => id != source!.Id).Append(destination.Id).Distinct()]),
                        actingUserId,
                        ct)
                    .ConfigureAwait(false))
                {
                    throw new InvalidOperationException("The file changed during correction.");
                }

                await UpdateFulfillmentCoreAsync(sourceLineage[0], source!, now, actingUserId, ct)
                    .ConfigureAwait(false);
                await UpdateFulfillmentCoreAsync(target, destination, now, actingUserId, ct).ConfigureAwait(false);
                await auditRecordRepository.CreateAsync(
                    new AuditRecord
                    {
                        Category = AuditCategories.Content,
                        Operation = "FileReassigned",
                        EntityType = nameof(MediaFile),
                        EntityId = file.Id.ToString(),
                        ParentEntityId = target.Id.ToString(),
                        Detail = $"Corrected {Path.GetFileName(file.FilePath)} from {contentHierarchyItemId} " +
                            $"to {target.Id}. " +
                            "The file and content paths were preserved.",
                        Timestamp = now,
                        UserId = actingUserId
                    },
                    ct).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct PathFileMove(Guid MediaFileId, string SourcePath, string DestinationPath);

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
        public bool Required { get; init; }

        public ICollection<string>? UnknownTokens { get; init; }

        public IReadOnlyDictionary<string, string?> Tokens { get; init; } =
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record RenameTokenDefinition(
        string Name,
        RenameTokenFamily Family,
        string Description,
        string Example,
        Func<RenameTokenSource, string?> Resolve);

    private sealed record RenameTokenSource(
        IReadOnlyList<ContentHierarchyItem> RootToLeaf,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> Relationships,
        MediaInfo? MediaInfo,
        string? FileName,
        IReadOnlyList<ContentHierarchyItem> Covered)
    {
        public ContentHierarchyItem Leaf => RootToLeaf[^1];
    }
}
