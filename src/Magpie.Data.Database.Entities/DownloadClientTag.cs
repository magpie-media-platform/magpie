using System;

namespace Magpie.Data.Database.Entities;

public record DownloadClientTag
{
    public Guid DownloadClientId { get; set; }
    public required string TagName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public DownloadClient? DownloadClient { get; set; }
    public Tag? Tag { get; set; }
}
