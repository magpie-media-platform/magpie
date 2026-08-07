using System.Collections.Generic;
using System.Linq;

using Magpie.Models.Enums;

namespace Magpie.Models;

/// <summary>
/// The catalog of standard metadata fields.
/// </summary>
public static class MetadataFieldExtensions
{
    /// <summary>
    /// The definitions of the standard metadata fields.
    /// </summary>
    private static IReadOnlyList<MetadataFieldDefinition> StandardFields { get; } =
    [
        Text(MetadataField.Title, "title", "Title", "the title filter", "a title", true, true),
        Text(
            MetadataField.OriginalTitle,
            "originalTitle",
            "Original title",
            "the original-title filter",
            "an original title",
            suppressRoutineTrimWarning: true),
        Text(
            MetadataField.SortTitle,
            "sortTitle",
            "Sort title",
            "the sort-title filter",
            "a sort title",
            suppressRoutineTrimWarning: true),
        Text(MetadataField.Language, "language", "Language", "the language filter", "a language", true),
        Text(MetadataField.Overview, "overview", "Overview", "the overview filter", "an overview", true),
        Date(
            MetadataField.PublishDate,
            "publishDate",
            "Publish date",
            "the publish-date filter",
            "a publish date",
            true),
        Duration(MetadataField.Duration, "duration", "Duration", "the duration filter", "a duration"),
        Text(
            MetadataField.ProductionStatus,
            "productionStatus",
            "Production status",
            "the production-status filter",
            "a production status"),
        Date(
            MetadataField.Established,
            "established",
            "Established",
            "the established-date filter",
            "an established date"),
        Date(MetadataField.Dissolved, "dissolved", "Dissolved", "the dissolved-date filter", "a dissolved date"),
        Date(MetadataField.BirthDate, "birthDate", "Birth date", "the birth-date filter", "a birth date"),
        Date(MetadataField.DeathDate, "deathDate", "Death date", "the death-date filter", "a death date"),
        Collection(MetadataField.ExternalIds, "externalIds", "External identifiers", "the external-id filter"),
        Collection(MetadataField.References, "references", "References", "the reference filter"),
        Collection(MetadataField.Relationships, "relationships", "Relationships", "the relationship filter"),
        Collection(MetadataField.Images, "images", "Images", "the image filter"),
        Collection(MetadataField.Previews, "previews", "Previews", "the preview filter"),
        Collection(MetadataField.AlternateTexts, "alternateTexts", "Alternate texts", "the alternate-text filter"),
        Collection(MetadataField.ContentRatings, "contentRatings", "Content ratings", "the content-rating filter"),
        Collection(MetadataField.Awards, "awards", "Awards", "the award filter"),
        Collection(MetadataField.Genres, "genres", "Genres", "the genre filter"),
        Number(MetadataField.SequenceNumber, "sequenceNumber", "Sequence number", "the sequence-number filter")
    ];

    /// <summary>
    /// Gets the definition of a standard metadata field. If the field has no definition, returns null.
    /// </summary>
    /// <param name="field">The metadata field to look up</param>
    public static MetadataFieldDefinition? GetDefinition(this MetadataField field) =>
        StandardFields.FirstOrDefault(definition => definition.Field == field);

    private static MetadataFieldDefinition Text(
        MetadataField field,
        string key,
        string displayName,
        string filterDescription,
        string missingValueDescription,
        bool candidateResultCheckable = false,
        bool suppressRoutineTrimWarning = false) =>
        new()
        {
            Field = field,
            Key = key,
            DisplayName = displayName,
            FilterDescription = filterDescription,
            MissingValueDescription = missingValueDescription,
            CandidateResultCheckable = candidateResultCheckable,
            SuppressRoutineCandidateTrimWarning = suppressRoutineTrimWarning
        };

    private static MetadataFieldDefinition Date(
        MetadataField field,
        string key,
        string displayName,
        string filterDescription,
        string missingValueDescription,
        bool candidateResultCheckable = false) =>
        new()
        {
            Field = field,
            Key = key,
            DisplayName = displayName,
            FilterDescription = filterDescription,
            MissingValueDescription = missingValueDescription,
            CandidateResultCheckable = candidateResultCheckable
        };

    private static MetadataFieldDefinition Duration(
        MetadataField field,
        string key,
        string displayName,
        string filterDescription,
        string missingValueDescription) =>
        new()
        {
            Field = field,
            Key = key,
            DisplayName = displayName,
            FilterDescription = filterDescription,
            MissingValueDescription = missingValueDescription
        };

    private static MetadataFieldDefinition Number(
        MetadataField field,
        string key,
        string displayName,
        string filterDescription) =>
        new()
        {
            Field = field,
            Key = key,
            DisplayName = displayName,
            FilterDescription = filterDescription,
            MissingValueDescription = "a value"
        };

    private static MetadataFieldDefinition Collection(
        MetadataField field,
        string key,
        string displayName,
        string filterDescription) =>
        new()
        {
            Field = field,
            Key = key,
            DisplayName = displayName,
            FilterDescription = filterDescription,
            MissingValueDescription = "a value"
        };
}
