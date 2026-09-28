using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Content;
using Magpie.Abstractions.Repositories;
using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Jobs;

namespace Magpie.Core.Jobs;

internal sealed class MediaFileRenameJob(
    IMediaFileRepository mediaFileRepository,
    IContentRepository contentRepository,
    IContentFileService contentFileService,
    IProgressReporter progressReporter) : IJob
{
    public static readonly JobSpec Spec = new()
    {
        JobType = MagpieJobs.Types.MediaFileRename,
        DisplayName = "Media file rename",
        Description = "Renames one media file using its effective rename template.",
        Category = "Content",
        Scope = JobScope.Content,
        Enabled = true,
        OnDemand = true,
        FailurePolicy = JobFailurePolicy.Retry,
        SupportedTriggerModes = [JobTriggerMode.Manual, JobTriggerMode.Schedule, JobTriggerMode.Event],
        ConcurrencyScope = ConcurrencyScope.PerTypeAndTarget,
        LockPartitionFields = [MagpieJobs.Parameters.MediaFileId],
        Parameters =
        [
            new(MagpieJobs.Parameters.MediaFileId, "The media file id to rename.", DataType.String, true, null)
            {
                ValidateValue = value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _)
                    ? null : "must be a valid GUID."
            }
        ],
    };

    public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue(MagpieJobs.Parameters.MediaFileId, out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException("A media file ID is required.");
        }

        if (!Guid.TryParse(raw, out var mediaFileId))
        {
            throw new ArgumentException("The media file ID has an invalid format.");
        }

        return RenameAsync(
            mediaFileId,
            context.ManualTriggerUserId,
            context.CorrelationId,
            progressReporter,
            cancellationToken);
    }

    private async Task RenameAsync(
        Guid mediaFileId,
        Guid? userId,
        Guid? correlationId,
        IProgressReporter progress,
        CancellationToken cancellationToken = default)
    {
        progress.Report(0, "The rename job will load the media file.");

        var mediaFile = await mediaFileRepository
            .GetByIdAsync(mediaFileId, cancellationToken)
            .ConfigureAwait(false);
        if (mediaFile is null)
        {
            throw new InvalidOperationException($"Media file {mediaFileId} does not exist.");
        }

        var contentItem = await contentRepository
            .GetByIdAsync(mediaFile.ContentItemId, cancellationToken)
            .ConfigureAwait(false);
        if (contentItem is null)
        {
            throw new InvalidOperationException(
                $"Media file {mediaFileId} references missing content item {mediaFile.ContentItemId}.");
        }

        progress.Report(null, "The rename job will get the target path.");
        var maskPlan = await contentFileService
            .PreviewRenameMaskAsync(
                contentItem.ContentHierarchyItemId,
                new() { MediaFileIds = [mediaFileId] },
                cancellationToken)
            .ConfigureAwait(false);
        if (maskPlan.HasMask)
        {
            progress.Report(null, "The rename job will rename the file.");
            var outcome = await contentFileService
                .ExecuteRenameMaskAsync(contentItem.ContentHierarchyItemId, maskPlan, userId, cancellationToken)
                .ConfigureAwait(false);
            var entry = outcome.Files.FirstOrDefault(file => file.MediaFileId == mediaFileId);
            if (entry is not { Completed: true })
            {
                throw new InvalidOperationException(
                    entry?.Error ?? $"The rename mask does not apply to media file {mediaFileId}.");
            }

            progress.Report(1, "The file rename completed.");
            return;
        }

        var preview = await contentFileService
            .PreviewRenameAsync(
                contentItem.ContentHierarchyItemId,
                new FileInfo(mediaFile.FilePath),
                mediaFile.MediaInfo,
                cancellationToken)
            .ConfigureAwait(false);

        progress.Report(null, "The rename job will rename the file.");
        await contentFileService
            .ExecuteRenameAsync(preview, userId, correlationId, cancellationToken)
            .ConfigureAwait(false);
        progress.Report(1, "The file rename completed.");
    }
}
