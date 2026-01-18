using System;

using Magpie.Models.Downloads;

namespace Magpie.Data.Database.Entities;

public class Download
{
    public Guid Id { get; set; }
    public Guid? ContentItemId { get; set; }
    public Guid? ReleaseId { get; set; }
    public Guid DownloadClientId { get; set; }
    public required string ExternalId { get; set; }
    public required string Title { get; set; }
    public DownloadState State { get; set; }
    public string? OutputPath { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset? DateCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public ContentItem? ContentItem { get; set; }
    public Release? Release { get; set; }
    public required DownloadClient DownloadClient { get; set; }
}
