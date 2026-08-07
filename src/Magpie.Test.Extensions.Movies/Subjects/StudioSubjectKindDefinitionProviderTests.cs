using System.Linq;

using Magpie.Extensions.Movies.Subjects;
using Magpie.Sdk.Content;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Movies;

[TestFixture]
public class StudioSubjectKindDefinitionProviderTests
{
    [Test]
    public void GetSubjectKindDefinitions_ReturnsStudioDefinition_WhenProvidersAreEnumerated()
    {
        // Arrange
        var providers = new ISubjectKindDefinitionProvider[]
        {
            new PersonSubjectKindDefinitionProvider(),
            new StudioSubjectKindDefinitionProvider()
        };

        // Act
        var definition = providers.SelectMany(p => p.GetSubjectKindDefinitions()).Single(d => d.Slug == "studio");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(definition.Slug, Is.EqualTo("studio"));
            Assert.That(definition.DisplayName, Is.EqualTo("Studio"));
            Assert.That(definition.PluralDisplayName, Is.EqualTo("Studios"));
            Assert.That(definition.IconName, Is.EqualTo("buildings"));
        }
    }

    [Test]
    public void GetSubjectKindDefinitions_ReturnsPersonAndStudio_WhenProvidersAreEnumerated()
    {
        // Arrange
        var providers = new ISubjectKindDefinitionProvider[]
        {
            new PersonSubjectKindDefinitionProvider(),
            new StudioSubjectKindDefinitionProvider()
        };

        // Act
        var slugs = providers.SelectMany(p => p.GetSubjectKindDefinitions()).Select(d => d.Slug).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(slugs, Does.Contain("person"));
            Assert.That(slugs, Does.Contain("studio"));
        }
    }
}
