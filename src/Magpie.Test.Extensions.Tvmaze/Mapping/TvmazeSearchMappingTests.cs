using System;
using System.Linq;
using System.Text.Json;

using Magpie.Extensions.Tvmaze.Mapping;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tvmaze.Mapping;

[TestFixture]
public class TvmazeSearchMappingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string HitJson = """
        { "score": 0.9, "show": {
          "id": 82,
          "name": "Game of Thrones",
          "language": "English",
          "genres": [ "Drama", "Adventure" ],
          "status": "Ended",
          "averageRuntime": 60,
          "premiered": "2011-04-17",
          "ended": "2019-05-19",
          "summary": "<p>Seven noble families fight.</p>",
          "network": { "id": 1, "name": "HBO" },
          "externals": { "imdb": "tt0944947", "thetvdb": 121361, "tvrage": 24493 },
          "image": { "medium": "https://static.tvmaze.com/uploads/medium.jpg" }
        } }
        """;

    [Test]
    public void ToShowCandidate_MapsIdentityAndDisplayFields_WhenShowIsProvided()
    {
        // Arrange
        var hit = JsonSerializer.Deserialize<SearchHitDto>(HitJson, JsonOptions)!;

        // Act
        var result = hit.Show.ToMetadataSearchCandidate();

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.ProviderType, Is.EqualTo("tvmaze"));
            Assert.That(result.ExternalIdSource, Is.EqualTo("tvmaze"));
            Assert.That(result.ExternalId, Is.EqualTo("82"));
            Assert.That(result.ContentType, Is.EqualTo("tv"));
            Assert.That(result.Level, Is.EqualTo("show"));
            Assert.That(result.Title, Is.EqualTo("Game of Thrones"));
            Assert.That(result.Overview, Is.EqualTo("Seven noble families fight."));
            Assert.That(result.Language, Is.EqualTo("English"));
            Assert.That(result.Year, Is.EqualTo(2011));
            Assert.That(result.ReleaseDate, Is.EqualTo(new DateTime(2011, 4, 17, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(
                result.ThumbnailUrl!.ToString(),
                Is.EqualTo("https://static.tvmaze.com/uploads/medium.jpg"));
        }
    }

    [Test]
    public void ToShowCandidate_MapsNetworkReference_WhenShowHasNetwork()
    {
        // Arrange
        var hit = JsonSerializer.Deserialize<SearchHitDto>(HitJson, JsonOptions)!;

        // Act
        var result = hit.Show.ToMetadataSearchCandidate();

        // Assert
        Assert.That(result, Is.Not.Null);
        var reference = result!.SubjectReferences.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reference.SubjectKind, Is.EqualTo("studio"));
            Assert.That(reference.DisplayName, Is.EqualTo("HBO"));
            Assert.That(reference.RelationshipKind, Is.EqualTo("network"));
            Assert.That(reference.ExternalIds["tvmaze"], Is.EqualTo("1"));
        }
    }

    [Test]
    public void ToMetadataSearchCandidate_ReturnsNull_WhenShowIsNull()
    {
        // Arrange

        // Act
        var result = ((ShowDto?)null).ToMetadataSearchCandidate();

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ToGuestSubjectRelationships_CapsCreditsAndKeepsContinuousOrdinals_WhenCreditsExceedCaps()
    {
        // Arrange
        var cast = Enumerable.Range(1, 31)
            .Select(index => new CastEntryDto
            {
                Person = new PersonDto { Id = index, Name = "Cast " + index },
                Character = new CharacterDto { Id = index, Name = "Character " + index }
            })
            .ToArray();
        var crew = Enumerable.Range(1, 31)
            .Select(index => new GuestCrewEntryDto
            {
                Person = new PersonDto { Id = 100 + index, Name = "Crew " + index },
                Type = "Director"
            })
            .ToArray();

        // Act
        var relationships = cast.ToGuestSubjectRelationships(
            crew,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relationships, Has.Count.EqualTo(60));
            Assert.That(relationships.Select(relationship => relationship.RelationshipOrdinal), Is.Ordered);
            Assert.That(relationships.Select(relationship => relationship.RelationshipOrdinal), Is.Unique);
            Assert.That(relationships[0].RelationshipOrdinal, Is.EqualTo(0));
            Assert.That(relationships[59].RelationshipOrdinal, Is.EqualTo(59));
            Assert.That(
                relationships.Take(30).Select(relationship => relationship.RelationshipKind),
                Is.All.EqualTo("guest-cast"));
            Assert.That(
                relationships.Skip(30).Select(relationship => relationship.RelationshipKind),
                Is.All.EqualTo("director"));
        }
    }

    [TestCase("javascript:alert(1)")]
    [TestCase("file:///image.jpg")]
    [TestCase("ftp://example.com/image.jpg")]
    [TestCase("/image.jpg")]
    public void ToMetadataSearchCandidate_OmitsUnsafeUrls_WhenProviderSuppliesNonWebUrls(string url)
    {
        // Arrange
        var show = new ShowDto
        {
            Id = 82,
            Name = "Show",
            Url = url,
            OfficialSite = url,
            Image = new ImageReferenceDto { Original = url, Medium = url }
        };

        // Act
        var references = show.ToReferences();
        var poster = show.Image.ToPoster(0);
        var profile = show.Image.ToProfileImage();
        var candidate = show.ToMetadataSearchCandidate();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(references, Is.Empty);
            Assert.That(poster, Is.Null);
            Assert.That(profile, Is.Null);
            Assert.That(candidate!.ThumbnailUrl, Is.Null);
        }
    }

    [TestCase(" http://example.com/image.jpg ", "http://example.com/image.jpg")]
    [TestCase(" https://example.com/image.jpg ", "https://example.com/image.jpg")]
    public void ToReferences_PreservesWebUrls_WhenProviderSuppliesHttpOrHttps(string url, string expectedUrl)
    {
        // Arrange
        var show = new ShowDto { Id = 82, Name = "Show", Url = url, OfficialSite = url };
        var image = new ImageReferenceDto { Original = url };

        // Act
        var references = show.ToReferences();
        var poster = image.ToPoster(0);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(references, Has.Count.EqualTo(2));
            Assert.That(references[0].Url.AbsoluteUri, Is.EqualTo(expectedUrl));
            Assert.That(references[1].Url.AbsoluteUri, Is.EqualTo(expectedUrl));
            Assert.That(poster!.Url.AbsoluteUri, Is.EqualTo(expectedUrl));
        }
    }
}
