using System.Collections.Generic;

using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.Movies.Subjects;

internal sealed class StudioSubjectKindDefinitionProvider : ISubjectKindDefinitionProvider
{
    public const string Slug = "studio";

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
            Slug = Slug,
            DisplayName = "Studio",
            PluralDisplayName = "Studios",
            IconName = "buildings",
            SupportedMetadataFields = StudioMetadataFields,
            CustomMetadataFields = []
        };
    }
}
