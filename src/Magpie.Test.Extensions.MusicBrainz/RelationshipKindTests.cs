using Magpie.Extensions.MusicBrainz;

using NUnit.Framework;

namespace Magpie.Test.Extensions.MusicBrainz;

[TestFixture]
public sealed class RelationshipKindTests
{
    [Test]
    public void RelationshipKinds_MatchDeclaredRoleSlugs_WhenMapperWritesThem()
    {
        // Arrange
        string[] declared = ["artist", "performer", "writer", "crew", "music-label"];

        // Act
        string[] emitted =
        [
            Constants.ArtistRelationshipKind,
            Constants.PerformerRelationshipKind,
            Constants.WriterRelationshipKind,
            Constants.CrewRelationshipKind,
            Constants.LabelSubjectKind
        ];

        // Assert
        Assert.That(emitted, Is.EqualTo(declared));
    }
}
