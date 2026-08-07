using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tvmaze;
using Magpie.Extensions.Tvmaze.Mapping;
using Magpie.Models;
using Magpie.Models.Data;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tvmaze;

[TestFixture]
public class TvmazeContentMetadataProviderRelationshipTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string ShowWithCreditsJson = """
        {
          "id": 82,
          "name": "Game of Thrones",
          "network": { "id": 8, "name": "HBO",
            "country": { "name": "United States", "code": "US", "timezone": "America/New_York" },
            "officialSite": "https://www.hbo.com/game-of-thrones" },
          "webChannel": { "id": 1, "name": "HBO Max" },
          "_embedded": {
            "cast": [
              { "person": { "id": 14072, "name": "Peter Dinklage", "birthday": "1969-06-11",
                  "country": { "name": "United States", "code": "US", "timezone": "America/New_York" },
                  "gender": "Male",
                  "image": { "medium": "https://static.tvmaze.com/uploads/dinklage-m.jpg",
                    "original": "https://static.tvmaze.com/uploads/dinklage.jpg" } },
                "character": { "id": 22587, "name": "Tyrion Lannister" },
                "self": false, "voice": true },
              { "person": { "id": 0, "name": "Broken Person" }, "character": { "name": "Nobody" } }
            ],
            "crew": [
              { "type": "Creator", "person": { "id": 15309, "name": "David Benioff" } },
              { "type": "Director", "person": { "id": 15310, "name": "Alan Taylor" } },
              { "type": "Writer", "person": { "id": 15311, "name": "Jane Espenson" } }
            ]
          }
        }
        """;

    private const string GuestCastJson = """
        [
          {
            "person": { "id": 7, "name": "Mackenzie Lintz", "birthday": "1996-11-22" },
            "character": { "id": 17, "name": "Norrie Calvert-Hill" },
            "self": true,
            "voice": true
          }
        ]
        """;

    private const string GuestCrewJson = """
        [
          {
            "person": { "id": 15, "name": "Stephen King" },
            "guestCrewType": "Story"
          }
        ]
        """;

    private Mock<IClient> _client = null!;
    private ShowDto _show = null!;

    [SetUp]
    public void SetUp()
    {
        _show = JsonSerializer.Deserialize<ShowDto>(ShowWithCreditsJson, JsonOptions)!;
        _client = new Mock<IClient>();
        _client
            .Setup(client => client.GetShowWithCreditsAsync(82, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_show);
        _client
            .Setup(client => client.GetEpisodeGuestCastAsync(4952, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize<GuestCastEntryDto[]>(GuestCastJson, JsonOptions)!);
        _client
            .Setup(client => client.GetEpisodeGuestCrewAsync(4952, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize<GuestCrewEntryDto[]>(GuestCrewJson, JsonOptions)!);
    }

    [Test]
    public async Task GetRelationshipsAsync_MapsRelationships_WhenShowHasCastCrewAndNetworks()
    {
        // Arrange
        var subject = new ContentMetadataProvider(
            _client.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Name = "Game of Thrones",
            Slug = "game-of-thrones"
        };
        var contribution = new MetadataContribution { ProviderType = "tvmaze" };
        contribution.ExternalIds.Add(new ExternalId { Source = "tvmaze", Value = "82", Ordinal = 0 });
        var existing = new MetadataContribution { ProviderType = "tvmaze" };
        existing.ExternalIds.Add(new ExternalId { Source = "tvmaze", Value = "999", Ordinal = 0 });

        // Act
        var relationships = await subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = contribution,
                ExistingMetadata = [existing],
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relationships, Has.Count.EqualTo(6));

            var tyrion = relationships[0];
            Assert.That(tyrion.RelationshipKind, Is.EqualTo("cast"));
            Assert.That(tyrion.RelationshipQualifier, Is.EqualTo("Tyrion Lannister (voice)"));
            Assert.That(tyrion.SubjectKind, Is.EqualTo("person"));
            Assert.That(tyrion.SubjectName, Is.EqualTo("Peter Dinklage"));
            Assert.That(tyrion.SubjectExternalIds["tvmaze"], Is.EqualTo("14072"));
            Assert.That(tyrion.InitialSubjectContribution!.Source, Is.EqualTo("tvmaze:person:14072"));
            Assert.That(
                tyrion.InitialSubjectContribution.BirthDate,
                Is.EqualTo(new DateTime(1969, 6, 11, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(tyrion.InitialSubjectContribution.Images.First().Type, Is.EqualTo("profile"));
            Assert.That(
                tyrion.InitialSubjectContribution.Images.First().Url.ToString(),
                Is.EqualTo("https://static.tvmaze.com/uploads/dinklage.jpg"));
            Assert.That(
                tyrion.InitialSubjectContribution.CustomMetadata.Select(value => (value.Key, value.Value)),
                Is.EqualTo([("gender", "male"), ("country", "US")]));

            Assert.That(
                relationships.Select(relationship => relationship.RelationshipKind),
                Is.EqualTo(["cast", "crew", "director", "writer", "network", "network"]));
            Assert.That(relationships[1].RelationshipQualifier, Is.EqualTo("Creator"));
            Assert.That(
                relationships.Select(relationship => relationship.RelationshipOrdinal),
                Is.EqualTo([0, 1, 2, 3, 4, 5]));

            var network = relationships[4];
            Assert.That(network.SubjectKind, Is.EqualTo("studio"));
            Assert.That(network.SubjectName, Is.EqualTo("HBO"));
            Assert.That(network.SubjectExternalIds["tvmaze"], Is.EqualTo("8"));
            Assert.That(network.InitialSubjectContribution!.Source, Is.EqualTo("tvmaze:network:8"));
            Assert.That(network.InitialSubjectContribution.CustomMetadata.Single().Key, Is.EqualTo("country"));
            Assert.That(network.InitialSubjectContribution.CustomMetadata.Single().Value, Is.EqualTo("US"));
            Assert.That(network.InitialSubjectContribution.References.Single().Kind, Is.EqualTo("official"));
            Assert.That(
                network.InitialSubjectContribution.References.Single().Url.ToString(),
                Is.EqualTo("https://www.hbo.com/game-of-thrones"));

            var webChannel = relationships[5];
            Assert.That(webChannel.SubjectName, Is.EqualTo("HBO Max"));
            Assert.That(webChannel.SubjectExternalIds["tvmaze"], Is.EqualTo("1"));
            Assert.That(webChannel.InitialSubjectContribution!.Source, Is.EqualTo("tvmaze:webchannel:1"));
            Assert.That(webChannel.InitialSubjectContribution.CustomMetadata, Is.Empty);
        }
    }

    [Test]
    public async Task GetRelationshipsAsync_GuestCredits_WhenItemIsEpisode()
    {
        // Arrange
        var subject = new ContentMetadataProvider(
            _client.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "episode",
            Name = "Winter Is Coming",
            Slug = "s01e01"
        };
        var contribution = new MetadataContribution { ProviderType = "tvmaze" };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "4952",
            Ordinal = 0
        });
        var existing = new MetadataContribution { ProviderType = "tvmaze" };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "9999",
            Ordinal = 0
        });

        // Act
        var relationships = await subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = contribution,
                ExistingMetadata = [existing],
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relationships, Has.Count.EqualTo(2));
            Assert.That(
                relationships.Select(relationship => relationship.RelationshipOrdinal),
                Is.EqualTo([0, 1]));
            Assert.That(relationships[0].RelationshipKind, Is.EqualTo("guest-cast"));
            Assert.That(
                relationships[0].RelationshipQualifier,
                Is.EqualTo("Norrie Calvert-Hill (self, voice)"));
            Assert.That(relationships[0].SubjectExternalIds["tvmaze"], Is.EqualTo("7"));
            Assert.That(relationships[1].RelationshipKind, Is.EqualTo("writer"));
            Assert.That(relationships[1].RelationshipQualifier, Is.EqualTo("Story"));
            Assert.That(relationships[1].SubjectName, Is.EqualTo("Stephen King"));
        }

        _client.Verify(
            client => client.GetEpisodeGuestCastAsync(4952, It.IsAny<CancellationToken>()),
            Times.Once);
        _client.Verify(
            client => client.GetEpisodeGuestCrewAsync(4952, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task GetRelationshipsAsync_ReturnsEmpty_WhenNoTvmazeShowIdIsKnown()
    {
        // Arrange
        var subject = new ContentMetadataProvider(
            _client.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Name = "Game of Thrones",
            Slug = "game-of-thrones"
        };
        var contribution = new MetadataContribution { ProviderType = "tvmaze" };

        // Act
        var relationships = await subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = contribution,
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(relationships, Is.Empty);
        _client.Verify(
            client => client.GetShowWithCreditsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task GetRelationshipsAsync_EmptyWithoutClientCalls_WhenEpisodeIdIsUnknown()
    {
        // Arrange
        var subject = new ContentMetadataProvider(
            _client.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "episode",
            Name = "Winter Is Coming",
            Slug = "s01e01"
        };
        var contribution = new MetadataContribution { ProviderType = "tvmaze" };

        // Act
        var relationships = await subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = contribution,
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(relationships, Is.Empty);
        _client.Verify(
            client => client.GetEpisodeGuestCastAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _client.Verify(
            client => client.GetEpisodeGuestCrewAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task GetRelationshipsAsync_ReturnsEmpty_ForNonShowLevels()
    {
        // Arrange
        var subject = new ContentMetadataProvider(
            _client.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "season",
            Name = "Season 1",
            Slug = "season-1"
        };
        var contribution = new MetadataContribution { ProviderType = "tvmaze" };
        var existing = new MetadataContribution { ProviderType = "tvmaze" };
        existing.ExternalIds.Add(new ExternalId { Source = "tvmaze", Value = "82", Ordinal = 0 });

        // Act
        var relationships = await subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = contribution,
                ExistingMetadata = [existing],
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(relationships, Is.Empty);
        _client.Verify(
            client => client.GetShowWithCreditsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _client.Verify(
            client => client.GetEpisodeGuestCastAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _client.Verify(
            client => client.GetEpisodeGuestCrewAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
