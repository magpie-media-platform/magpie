using System;

using Magpie.Models.Data;

namespace Magpie.Models.Enums;

/// <summary>
/// Flags indicating which metadata fields are supported or present.
/// </summary>
[Flags]
public enum MetadataField : long
{
    /// <summary>
    /// No metadata fields are supported.
    /// </summary>
    None = 0x0,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.Title" />
    /// </summary>
    Title = 0x1,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.OriginalTitle" />
    /// </summary>
    OriginalTitle = 0x2,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.SortTitle" />
    /// </summary>
    SortTitle = 0x4,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.Language" />
    /// </summary>
    Language = 0x8,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.Overview" />
    /// </summary>
    Overview = 0x10,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.PublishDate" />
    /// </summary>
    PublishDate = 0x20,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.Duration" />
    /// </summary>
    Duration = 0x40,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.ProductionStatus" />
    /// </summary>
    ProductionStatus = 0x80,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.Established" />
    /// </summary>
    Established = 0x100,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.Dissolved" />
    /// </summary>
    Dissolved = 0x200,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.BirthDate" />
    /// </summary>
    BirthDate = 0x400,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.DeathDate" />
    /// </summary>
    DeathDate = 0x800,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.ExternalIds" />
    /// </summary>
    ExternalIds = 0x1000,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.References" />
    /// </summary>
    References = 0x2000,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.Relationships" />
    /// </summary>
    Relationships = 0x4000,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.Images" />
    /// </summary>
    Images = 0x8000,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.Previews" />
    /// </summary>
    Previews = 0x10000,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.AlternateTexts" />
    /// </summary>
    AlternateTexts = 0x20000,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.ContentRatings" />
    /// </summary>
    ContentRatings = 0x40000,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.Awards" />
    /// </summary>
    Awards = 0x80000,
    /// <summary>
    /// Metadata field <see cref="MetadataContribution.Genres" />
    /// </summary>
    Genres = 0x100000,
    /// <summary>
    /// All metadata fields are supported.
    /// </summary>
    All = Title | OriginalTitle | SortTitle | Language | Overview | PublishDate | Duration |
          ProductionStatus | Established | Dissolved | BirthDate | DeathDate | ExternalIds |
          References | Relationships | Images | Previews | AlternateTexts | ContentRatings | Awards | Genres
}
