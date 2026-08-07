using System;

namespace Magpie.Models.Enums;

/// <summary>
/// Flags for the metadata fields that are supported or present.
/// </summary>
[Flags]
public enum MetadataField : long
{
    /// <summary>
    /// No metadata fields.
    /// </summary>
    None = 0x0,
    /// <summary>
    /// The title.
    /// </summary>
    Title = 0x1,
    /// <summary>
    /// The title in the original language.
    /// </summary>
    OriginalTitle = 0x2,
    /// <summary>
    /// The title for sort order.
    /// </summary>
    SortTitle = 0x4,
    /// <summary>
    /// The primary language.
    /// </summary>
    Language = 0x8,
    /// <summary>
    /// The descriptive overview text.
    /// </summary>
    Overview = 0x10,
    /// <summary>
    /// The publish date.
    /// </summary>
    PublishDate = 0x20,
    /// <summary>
    /// The duration.
    /// </summary>
    Duration = 0x40,
    /// <summary>
    /// The production status.
    /// </summary>
    ProductionStatus = 0x80,
    /// <summary>
    /// The established date.
    /// </summary>
    Established = 0x100,
    /// <summary>
    /// The dissolved date.
    /// </summary>
    Dissolved = 0x200,
    /// <summary>
    /// The birth date.
    /// </summary>
    BirthDate = 0x400,
    /// <summary>
    /// The death date.
    /// </summary>
    DeathDate = 0x800,
    /// <summary>
    /// The identifiers at external providers.
    /// </summary>
    ExternalIds = 0x1000,
    /// <summary>
    /// The external references.
    /// </summary>
    References = 0x2000,
    /// <summary>
    /// The relationships to other subjects.
    /// </summary>
    Relationships = 0x4000,
    /// <summary>
    /// The images.
    /// </summary>
    Images = 0x8000,
    /// <summary>
    /// The previews.
    /// </summary>
    Previews = 0x10000,
    /// <summary>
    /// The alternate texts.
    /// </summary>
    AlternateTexts = 0x20000,
    /// <summary>
    /// The content ratings.
    /// </summary>
    ContentRatings = 0x40000,
    /// <summary>
    /// The awards.
    /// </summary>
    Awards = 0x80000,
    /// <summary>
    /// The genres.
    /// </summary>
    Genres = 0x100000,
    /// <summary>
    /// The sequence number.
    /// </summary>
    SequenceNumber = 0x200000,
    /// <summary>
    /// All metadata fields.
    /// </summary>
    All = Title |
        OriginalTitle |
        SortTitle |
        Language |
        Overview |
        PublishDate |
        Duration |
        ProductionStatus |
        Established |
        Dissolved |
        BirthDate |
        DeathDate |
        ExternalIds |
        References |
        Relationships |
        Images |
        Previews |
        AlternateTexts |
        ContentRatings |
        Awards |
        Genres |
        SequenceNumber
}
