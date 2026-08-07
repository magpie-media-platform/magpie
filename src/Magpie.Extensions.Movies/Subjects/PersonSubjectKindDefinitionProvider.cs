using System.Collections.Generic;

using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.Movies.Subjects;

internal sealed class PersonSubjectKindDefinitionProvider : ISubjectKindDefinitionProvider
{
    public const string Slug = "person";

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

    public IEnumerable<SubjectKindDefinition> GetSubjectKindDefinitions()
    {
        yield return new SubjectKindDefinition
        {
            Slug = Slug,
            DisplayName = "Person",
            PluralDisplayName = "People",
            IconName = "person",
            SupportedMetadataFields = PersonMetadataFields,
            CustomMetadataFields = []
        };
    }
}
