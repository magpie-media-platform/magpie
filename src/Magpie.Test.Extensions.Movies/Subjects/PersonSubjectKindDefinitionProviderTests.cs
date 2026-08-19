using System.Linq;

using Magpie.Extensions.Movies.Subjects;
using Magpie.Models.Enums;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Movies;

[TestFixture]
public sealed class PersonSubjectKindDefinitionProviderTests
{
    [Test]
    public void GetSubjectKindDefinitions_ReturnsFallbackIdentityFields_WhenKindIsPerson()
    {
        // Arrange
        var provider = new PersonSubjectKindDefinitionProvider();

        // Act
        var definition = provider.GetSubjectKindDefinitions().Single();

        // Assert
        Assert.That(
            definition.FallbackIdentityFields,
            Is.EqualTo(MetadataField.Title | MetadataField.BirthDate));
    }
}
