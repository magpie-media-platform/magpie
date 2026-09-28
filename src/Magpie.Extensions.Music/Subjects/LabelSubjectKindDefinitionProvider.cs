using System.Collections.Generic;

using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.Music.Subjects;

internal sealed class LabelSubjectKindDefinitionProvider : ISubjectKindDefinitionProvider
{
    public const string Slug = "music-label";

    private const MetadataField LabelMetadataFields =
        MetadataField.Title |
        MetadataField.Overview |
        MetadataField.Established |
        MetadataField.Dissolved |
        MetadataField.ExternalIds |
        MetadataField.References |
        MetadataField.Relationships |
        MetadataField.Images |
        MetadataField.AlternateTexts;

    public IEnumerable<SubjectKindDefinition> GetSubjectKindDefinitions()
    {
        yield return new SubjectKindDefinition
        {
            Slug = Slug,
            DisplayName = "Label",
            PluralDisplayName = "Labels",
            IconName = "equalizer",
            SupportedMetadataFields = LabelMetadataFields,
            CustomMetadataFields = [],
            RelationshipRoles =
            [
                new()
                {
                    Slug = "music-label",
                    DisplayName = "Label",
                    Description = "The record labels, separated by spaces.",
                    Examples = ["Example Records", "Other Records"]
                }
            ]
        };
    }
}
