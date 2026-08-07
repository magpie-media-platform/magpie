using System.Linq;

using Magpie.Extensions.Music.Subjects;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Music;

[TestFixture]
public class LabelSubjectKindDefinitionProviderTests
{
    [Test]
    public void GetSubjectKindDefinitions_ReturnsMusicLabelDefinition_WhenEnumerated()
    {
        // Arrange
        var provider = new LabelSubjectKindDefinitionProvider();

        // Act
        var definition = provider.GetSubjectKindDefinitions().Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(definition.Slug, Is.EqualTo("music-label"));
            Assert.That(LabelSubjectKindDefinitionProvider.Slug, Is.EqualTo("music-label"));
            Assert.That(definition.DisplayName, Is.EqualTo("Label"));
            Assert.That(definition.PluralDisplayName, Is.EqualTo("Labels"));
            Assert.That(definition.IconName, Is.EqualTo("equalizer"));
            Assert.That(definition.CustomMetadataFields, Is.Empty);
        }
    }
}
