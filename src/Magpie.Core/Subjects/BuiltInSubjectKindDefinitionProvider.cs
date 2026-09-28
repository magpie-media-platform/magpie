using System.Collections.Generic;

using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Content;

namespace Magpie.Core.Subjects;

internal sealed class BuiltInSubjectKindDefinitionProvider : ISubjectKindDefinitionProvider
{
    public const string PersonSlug = "person";

    public const string StudioSlug = "studio";

    private const MetadataField PersonMetadataFields =
        MetadataField.Title |
        MetadataField.OriginalTitle |
        MetadataField.SortTitle |
        MetadataField.Language |
        MetadataField.Overview |
        MetadataField.BirthDate |
        MetadataField.DeathDate |
        MetadataField.ExternalIds |
        MetadataField.References |
        MetadataField.Relationships |
        MetadataField.Images |
        MetadataField.AlternateTexts |
        MetadataField.Awards;

    private const MetadataField StudioMetadataFields =
        MetadataField.Title |
        MetadataField.Overview |
        MetadataField.ExternalIds |
        MetadataField.References |
        MetadataField.Relationships |
        MetadataField.Images;

    public IEnumerable<SubjectKindDefinition> GetSubjectKindDefinitions()
    {
        yield return new SubjectKindDefinition
        {
            Slug = PersonSlug,
            DisplayName = "Person",
            PluralDisplayName = "People",
            IconName = "person",
            SupportedMetadataFields = PersonMetadataFields,
            CustomMetadataFields = [],
            FallbackIdentityFields = MetadataField.Title | MetadataField.BirthDate,
            RelationshipRoles =
            [
                new()
                {
                    Slug = "cast",
                    DisplayName = "Cast",
                    Description = "The cast members, separated by spaces.",
                    Examples = ["Ada Lovelace", "Alan Turing"]
                },
                new()
                {
                    Slug = "guest-cast",
                    DisplayName = "Guest cast",
                    Description = "The guest cast members, separated by spaces.",
                    Examples = ["Ada Lovelace", "Alan Turing"]
                },
                new()
                {
                    Slug = "director",
                    DisplayName = "Director",
                    Description = "The directors, separated by spaces.",
                    Examples = ["Ada Lovelace", "Alan Turing"]
                },
                new()
                {
                    Slug = "writer",
                    DisplayName = "Writer",
                    Description = "The writers, separated by spaces.",
                    Examples = ["Ada Lovelace", "Alan Turing"]
                },
                new()
                {
                    Slug = "crew",
                    DisplayName = "Crew",
                    Description = "The other crew members, separated by spaces.",
                    Examples = ["Ada Lovelace", "Alan Turing"]
                },
                new()
                {
                    Slug = "author",
                    DisplayName = "Author",
                    Description = "The authors, separated by spaces.",
                    Examples = ["Ada Lovelace", "Alan Turing"]
                },
                new()
                {
                    Slug = "translator",
                    DisplayName = "Translator",
                    Description = "The translators, separated by spaces.",
                    Examples = ["Ada Lovelace", "Alan Turing"]
                },
                new()
                {
                    Slug = "illustrator",
                    DisplayName = "Illustrator",
                    Description = "The illustrators, separated by spaces.",
                    Examples = ["Ada Lovelace", "Alan Turing"]
                },
                new()
                {
                    Slug = "editor",
                    DisplayName = "Editor",
                    Description = "The editors, separated by spaces.",
                    Examples = ["Ada Lovelace", "Alan Turing"]
                },
                new()
                {
                    Slug = "contributor",
                    DisplayName = "Contributor",
                    Description = "The other contributors, separated by spaces.",
                    Examples = ["Ada Lovelace", "Alan Turing"]
                },
                new()
                {
                    Slug = "artist",
                    DisplayName = "Artist",
                    Description = "The credited artists, separated by spaces.",
                    Examples = ["Ada Lovelace", "Alan Turing"]
                },
                new()
                {
                    Slug = "performer",
                    DisplayName = "Performer",
                    Description = "The performers, separated by spaces.",
                    Examples = ["Ada Lovelace", "Alan Turing"]
                }
            ]
        };

        yield return new SubjectKindDefinition
        {
            Slug = StudioSlug,
            DisplayName = "Studio",
            PluralDisplayName = "Studios",
            IconName = "buildings",
            SupportedMetadataFields = StudioMetadataFields,
            CustomMetadataFields = [],
            RelationshipRoles =
            [
                new()
                {
                    Slug = "studio",
                    DisplayName = "Studio",
                    Description = "The production studios, separated by spaces.",
                    Examples = ["Example Pictures", "Other Pictures"]
                },
                new()
                {
                    Slug = "network",
                    DisplayName = "Network",
                    Description = "The networks that broadcast the content, separated by spaces.",
                    Examples = ["Example Network", "Other Network"]
                }
            ]
        };
    }
}
