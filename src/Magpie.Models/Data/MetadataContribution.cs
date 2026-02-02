using System;
using System.Collections.Generic;

namespace Magpie.Models.Data;

/// <summary>
/// A metadata contribution from a provider.
/// </summary>
public record MetadataContribution
{
    /// <summary>
    /// The metadata contribution id.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The metadata container the contribution belongs to.
    /// </summary>
    public Guid MetadataContainerId { get; set; }

    /// <summary>
    /// The priority order.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// Whether the contribution is locked from automatic updates.
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>
    /// The extension that defines the metadata provider.
    /// </summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>
    /// The metadata provider type.
    /// </summary>
    public required string ProviderType { get; set; }

    /// <summary>
    /// Attribution source for the metadata.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// The primary title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The original title in the original language.
    /// </summary>
    public string? OriginalTitle { get; set; }

    /// <summary>
    /// The title used for sorting.
    /// </summary>
    public string? SortTitle { get; set; }

    /// <summary>
    /// The primary language.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// A description or summary.
    /// </summary>
    public string? Overview { get; set; }

    /// <summary>
    /// The publication or release date.
    /// </summary>
    public DateTime? PublishDate { get; set; }

    /// <summary>
    /// The duration of the content.
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// The production status (e.g., "In Production", "Released").
    /// </summary>
    public string? ProductionStatus { get; set; }

    /// <summary>
    /// When the entity was established.
    /// </summary>
    public DateTime? Established { get; set; }

    /// <summary>
    /// When the entity was dissolved.
    /// </summary>
    public DateTime? Dissolved { get; set; }

    /// <summary>
    /// The date of birth.
    /// </summary>
    public DateTime? BirthDate { get; set; }

    /// <summary>
    /// The date of death.
    /// </summary>
    public DateTime? DeathDate { get; set; }

    /// <summary>
    /// When the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The id of the user that created metadata contribution.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// When the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// The id of the user that last modified the metadata contribution.
    /// </summary>
    public Guid? ModifiedByUserId { get; set; }

    /// <summary>
    /// When the contribution was last refreshed.
    /// </summary>
    public DateTime? DateLastRefreshed { get; set; }

    /// <summary>
    /// The <see cref="MetadataContainer"/> that the contribution belongs to.
    /// </summary>
    public MetadataContainer? MetadataContainer { get; set; }

    /// <summary>
    /// The <see cref="AlternateText"/> entries associated with the contribution.
    /// </summary>
    public ICollection<AlternateText> AlternateTexts { get; } = [];

    /// <summary>
    /// The <see cref="Award"/> entries associated with the contribution.
    /// </summary>
    public ICollection<Award> Awards { get; } = [];

    /// <summary>
    /// The <see cref="ContentRating"/> entries associated with the contribution.
    /// </summary>
    public ICollection<ContentRating> ContentRatings { get; } = [];

    /// <summary>
    /// The <see cref="CustomMetadataValue"/> values associated with the contribution.
    /// </summary>
    public ICollection<CustomMetadataValue> CustomMetadata { get; } = [];

    /// <summary>
    /// The <see cref="ExternalId"/> entries associated with the contribution.
    /// </summary>
    public ICollection<ExternalId> ExternalIds { get; } = [];

    /// <summary>
    /// The <see cref="Genre"/> entries associated with the contribution.
    /// </summary>
    public ICollection<Genre> Genres { get; } = [];

    /// <summary>
    /// The <see cref="Image"/> entries associated with the contribution.
    /// </summary>
    public ICollection<Image> Images { get; } = [];

    /// <summary>
    /// The <see cref="Preview"/> entries associated with the contribution.
    /// </summary>
    public ICollection<Preview> Previews { get; } = [];

    /// <summary>
    /// The <see cref="Reference"/> entries associated with the contribution.
    /// </summary>
    public ICollection<Reference> References { get; } = [];

    /// <summary>
    /// The <see cref="Relationship"/> entries associated with the contribution.
    /// </summary>
    public ICollection<Relationship> Relationships { get; } = [];
}
