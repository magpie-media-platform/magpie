using System.Collections.Generic;

using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Content;

namespace Magpie.Extensions.Books.Subjects;

internal sealed class PublisherSubjectKindDefinitionProvider : ISubjectKindDefinitionProvider
{
    public const string Slug = "book-publisher";

    private const MetadataField PublisherMetadataFields =
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
            DisplayName = "Publisher",
            PluralDisplayName = "Publishers",
            IconName = "books",
            SupportedMetadataFields = PublisherMetadataFields,
            CustomMetadataFields = []
        };
    }
}
