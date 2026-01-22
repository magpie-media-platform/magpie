using System;

namespace Magpie.Data.Database.Entities;

public record MediaFile
{
    public Guid Id { get; set; }
    public Guid ContentItemId { get; set; }
    public required string Kind { get; set; }
    public string? MimeType { get; set; }
    public required string RelativePath { get; set; }
    public long Size { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedByUserId { get; set; }

    public MediaInfo? MediaInfo { get; set; }
    public ContentItem? ContentItem { get; set; }
}
