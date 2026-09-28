using Magpie.Extensions.Tmdb;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tmdb;

[TestFixture]
public sealed class RelationshipKindTests
{
    [Test]
    public void RelationshipKinds_MatchDeclaredRoleSlugs_WhenMapperWritesThem()
    {
        // Arrange
        string[] declared = ["cast", "director", "writer", "crew", "studio", "network"];

        // Act
        string[] emitted =
        [
            Constants.Cast,
            Constants.Director,
            Constants.Writer,
            Constants.Crew,
            Constants.Studio,
            Constants.Network
        ];

        // Assert
        Assert.That(emitted, Is.EqualTo(declared));
    }
}
