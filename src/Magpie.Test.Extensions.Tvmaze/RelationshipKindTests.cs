using Magpie.Extensions.Tvmaze;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tvmaze;

[TestFixture]
public sealed class RelationshipKindTests
{
    [Test]
    public void RelationshipKinds_MatchDeclaredRoleSlugs_WhenMapperWritesThem()
    {
        // Arrange
        string[] declared = ["cast", "guest-cast", "director", "writer", "crew", "network"];

        // Act
        string[] emitted =
        [
            RelationshipKinds.Cast,
            RelationshipKinds.GuestCast,
            RelationshipKinds.Director,
            RelationshipKinds.Writer,
            RelationshipKinds.Crew,
            RelationshipKinds.Network
        ];

        // Assert
        Assert.That(emitted, Is.EqualTo(declared));
    }
}
