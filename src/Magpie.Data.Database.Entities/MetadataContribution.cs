using System;
using System.Collections.Generic;

namespace Magpie.Data.Database.Entities;

public class MetadataContribution
{
    public Guid Id { get; set; }
    public Guid MetadataContainerId { get; set; }
    public int Ordinal { get; set; }
    public bool Locked { get; set; }
    public string? Provider { get; set; }
    public DateTimeOffset DateAdded { get; set; }
    public DateTimeOffset? DateLastRefreshed { get; set; }
    public string? Attribution { get; set; }
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? SortTitle { get; set; }
    public string? Language { get; set; }
    public string? Overview { get; set; }
    public DateTime? PublishDate { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? ProductionStatus { get; set; }
    public DateTime? Established { get; set; }
    public DateTime? Dissolved { get; set; }
    public DateTime? BirthDate { get; set; }
    public DateTime? DeathDate { get; set; }

    public required MetadataContainer MetadataContainer { get; set; }
    public ICollection<CustomMetadataValue> CustomMetadataValues { get; } = [];
    public ICollection<ExternalId> ExternalIds { get; } = [];
    public ICollection<Reference> References { get; } = [];
    public ICollection<Relationship> Relationships { get; } = [];
    public ICollection<Image> Images { get; } = [];
    public ICollection<Preview> Previews { get; } = [];
    public ICollection<AlternateText> AlternateTexts { get; } = [];
    public ICollection<ContentRating> ContentRatings { get; } = [];
    public ICollection<Award> Awards { get; } = [];
    public ICollection<Genre> Genres { get; } = [];
}
