using System;

namespace Magpie.Data.Database.Entities;

public class Preview
{
    public Guid Id { get; set; }
    public Guid MetadataContributionId { get; set; }
    public int Ordinal { get; set; }
    public required string PreviewType { get; set; }
    public string? MimeType { get; set; }
    public required Uri Url { get; set; }
    public string? Attribution { get; set; }
    public string? Title { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? Language { get; set; }

    public required MetadataContribution MetadataContribution { get; set; }
}
