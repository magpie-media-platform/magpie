using System.Linq;

using Magpie.Extensions.Books.Subjects;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Books.Subjects;

[TestFixture]
public sealed class PublisherSubjectKindDefinitionProviderTests
{
    [Test]
    public void GetSubjectKindDefinitions_ReturnsBookPublisherDefinition_WhenEnumerated()
    {
        // Arrange
        var provider = new PublisherSubjectKindDefinitionProvider();

        // Act
        var definition = provider.GetSubjectKindDefinitions().Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(definition.Slug, Is.EqualTo("book-publisher"));
            Assert.That(definition.DisplayName, Is.EqualTo("Publisher"));
            Assert.That(definition.PluralDisplayName, Is.EqualTo("Publishers"));
            Assert.That(definition.IconName, Is.EqualTo("books"));
            Assert.That(definition.CustomMetadataFields, Is.Empty);
        }
    }
}
