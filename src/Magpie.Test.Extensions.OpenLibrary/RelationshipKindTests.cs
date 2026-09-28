using Magpie.Extensions.OpenLibrary;

using NUnit.Framework;

namespace Magpie.Test.Extensions.OpenLibrary;

[TestFixture]
public sealed class RelationshipKindTests
{
    [Test]
    public void RelationshipKinds_MatchDeclaredRoleSlugs_WhenMapperWritesThem()
    {
        // Arrange
        string[] declared = ["author", "publisher", "translator", "illustrator", "editor", "contributor"];

        // Act
        string[] emitted =
        [
            Constants.AuthorRelationshipKind,
            Constants.PublisherRelationshipKind,
            Constants.TranslatorRelationshipKind,
            Constants.IllustratorRelationshipKind,
            Constants.EditorRelationshipKind,
            Constants.ContributorRelationshipKind
        ];

        // Assert
        Assert.That(emitted, Is.EqualTo(declared));
    }
}
