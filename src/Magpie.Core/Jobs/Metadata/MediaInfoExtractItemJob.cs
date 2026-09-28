using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Media;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Core.Jobs.Metadata;

internal sealed class MediaInfoExtractItemJob(
    IMediaFileRepository mediaFileRepository,
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    ILogger<MediaInfoExtractItemJob> logger,
    IProgressReporter progressReporter,
    IMediaInfoExtractor? mediaInfoExtractor = null) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.MediaInfoExtractItem,
        DisplayName = "Media info extract item",
        Description = "Reads media information from one file and saves it.",
        Category = "Metadata",
        Scope = JobScope.Administrative,
        Enabled = true,
        OnDemand = false,
        FailurePolicy = JobFailurePolicy.Retry,
        SupportedTriggerModes = [JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTarget,
        LockPartitionFields = [MagpieJobs.Parameters.MediaFileId],
        Parameters =
        [
            new(
                MagpieJobs.Parameters.MediaFileId,
                "The media file id to refresh.",
                DataType.String,
                true,
                null),
            new(
                MagpieJobs.Parameters.FilePath,
                "The media file path to probe.",
                DataType.String,
                true,
                null),
            new(
                MagpieJobs.Parameters.RequireMissingMediaInfo,
                "Whether to only update rows that still have no media info.",
                DataType.Boolean,
                false,
                "true")
        ],
    };

    private const string NoExtractorMessage =
        "The refresh stopped because no media information extractor is registered.";

    private static Guid ReadMediaFileId(JobContext context)
    {
        if (!context.Parameters.TryGetValue(
            MagpieJobs.Parameters.MediaFileId,
            out var rawMediaFileId) || string.IsNullOrWhiteSpace(rawMediaFileId))
        {
            throw new ArgumentException($"Parameter {MagpieJobs.Parameters.MediaFileId} is required");
        }

        if (!Guid.TryParse(rawMediaFileId, out var mediaFileId))
        {
            throw new ArgumentException("The media file ID has an invalid format.");
        }

        return mediaFileId;
    }

    private static string ReadFilePath(JobContext context)
    {
        if (!context.Parameters.TryGetValue(
            MagpieJobs.Parameters.FilePath,
            out var filePath) || string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException($"Parameter {MagpieJobs.Parameters.FilePath} is required");
        }

        return filePath;
    }

    private static bool ReadRequireMissingMediaInfo(JobContext context)
    {
        if (!context.Parameters.TryGetValue(
            MagpieJobs.Parameters.RequireMissingMediaInfo,
            out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        return bool.TryParse(raw, out var parsed)
            ? parsed
            : raw == "1" || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    public async Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        var mediaFileId = ReadMediaFileId(context);
        var filePath = ReadFilePath(context);
        var requireMissingMediaInfo = ReadRequireMissingMediaInfo(context);

        progressReporter.Report(0, "The refresh will get media information for the media file.");

        var result = await ScanAndPersistAsync(mediaFileId, filePath, requireMissingMediaInfo, cancellationToken)
            .ConfigureAwait(false);

        if (result == MediaInfoScanResult.ExtractorUnavailable)
        {
            logger.LogInformation(
                "No media info extractor is registered, so media info extract item skipped media file {MediaFileId}",
                mediaFileId);
            progressReporter.Report(1, NoExtractorMessage);
            return;
        }

        if (result == MediaInfoScanResult.Updated)
        {
            logger.LogInformation("Media info extract item updated media file {MediaFileId}", mediaFileId);
            progressReporter.Report(1, "The media information refresh completed.");
            return;
        }

        logger.LogInformation("Media info extract item skipped media file {MediaFileId}", mediaFileId);
        progressReporter.Report(1, "The media information refresh completed and did not change the media file.");
    }

    private async Task<MediaInfoScanResult> ScanAndPersistAsync(
        Guid mediaFileId,
        string filePath,
        bool requireMissingMediaInfo,
        CancellationToken cancellationToken)
    {
        if (mediaInfoExtractor is null)
        {
            return MediaInfoScanResult.ExtractorUnavailable;
        }

        if (!fileSystem.FileExists(filePath))
        {
            return MediaInfoScanResult.Skipped;
        }

        var mediaFile = await mediaFileRepository.GetByIdAsync(mediaFileId, cancellationToken).ConfigureAwait(false);
        if (mediaFile is null || mediaFile.FilePath != filePath ||
            requireMissingMediaInfo && mediaFile.MediaInfo is not null)
        {
            return MediaInfoScanResult.Skipped;
        }

        MediaInfo? mediaInfo;
        try
        {
            mediaInfo = await mediaInfoExtractor.ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The extractor failed to get the media info from {FilePath}", filePath);
            throw;
        }

        if (mediaInfo is null)
        {
            return MediaInfoScanResult.Skipped;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var mimeType = await fileSystem.LookupMimeTypeAsync(filePath, cancellationToken).ConfigureAwait(false);
        var updated = await mediaFileRepository.UpdateAsync(
            mediaFile with
            {
                FileKind = fileSystem.ResolveFileKind(mimeType).ToString(),
                MimeType = mimeType,
                FileSize = ReadFileSize(filePath) ?? mediaFile.FileSize,
                MediaInfo = mediaInfo,
                ModifiedAt = now
            },
            modifiedByUserId: null,
            cancellationToken).ConfigureAwait(false);
        return updated ? MediaInfoScanResult.Updated : MediaInfoScanResult.Skipped;
    }

    private long? ReadFileSize(string filePath)
    {
        try
        {
            return fileSystem.GetFileSize(filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Media info extract item failed to read the size of media file {FilePath} and kept the stored size",
                filePath);
            return null;
        }
    }
}
