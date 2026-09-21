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
            FallbackIdentityFields = MetadataField.Title | MetadataField.BirthDate
        };

        yield return new SubjectKindDefinition
        {
            Slug = StudioSlug,
            DisplayName = "Studio",
            PluralDisplayName = "Studios",
            IconName = "buildings",
            SupportedMetadataFields = StudioMetadataFields,
            CustomMetadataFields = []
        };
    }
}
