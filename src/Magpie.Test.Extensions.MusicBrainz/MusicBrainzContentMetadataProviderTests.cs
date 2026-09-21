using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz;
using Magpie.Extensions.MusicBrainz.Mapping;
using Magpie.Extensions.MusicBrainz.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.MusicBrainz;

[TestFixture]
public class MusicBrainzContentMetadataProviderTests
{
    private Mock<IMusicBrainzClient> _client = null!;
    private Mock<ICoverArtArchiveClient> _coverClient = null!;
    private IContentHierarchyMetadataProvider _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new Mock<IMusicBrainzClient>();
        _coverClient = new Mock<ICoverArtArchiveClient>();
        var settingsResolver = new Mock<ISettingsResolver>();
        settingsResolver.Setup(resolver => resolver.ResolveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Settings());
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(value => value.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<EntityResponseDto>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                EntityCacheKey _,
                Func<CancellationToken, Task<FetchResult<EntityResponseDto>>> loader,
                CancellationToken token) => loader(token));
        cache.Setup(value => value.GetBrowsePageAsync(
                It.IsAny<BrowseCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<ReleaseGroupSearchResponseDto>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                BrowseCacheKey _,
                Func<CancellationToken, Task<FetchResult<ReleaseGroupSearchResponseDto>>> loader,
                CancellationToken token) => loader(token));
        cache.Setup(value => value.GetCoverAsync(
                It.IsAny<CoverCacheKey>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<Uri>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                CoverCacheKey _,
                Func<CancellationToken, Task<FetchResult<Uri>>> loader,
                CancellationToken token) => loader(token));
        _subject = new ContentMetadataProvider(
            settingsResolver.Object,
            _client.Object,
            _coverClient.Object,
            cache.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
    }

    [TestCase(500)]
    [TestCase(600)]
    public async Task TryGetMetadataAsync_FiveHundredChildren_WhenArtistHasAtLeastFiveHundredReleaseGroups(
        int providerCount)
    {
        // Arrange
        const string artistMbid = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3";
        _client.Setup(client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Artist = new ArtistDto
                    {
                        Id = artistMbid,
                        Name = "Queen",
                        SortName = "Queen",
                        Aliases = [new AliasDto { Name = "クイーン", Locale = "ja" }],
                        Genres = [new GenreDto { Name = "rock", Count = 10 }],
                        Isnis = ["0000000121707484"],
                        Ipis = ["00045620792"]
                    }
                }
            });
        _client.Setup(client => client.BrowseReleaseGroupsAsync(
                It.IsAny<BrowseCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BrowseCacheKey key, Settings _, CancellationToken _) =>
                new FetchResult<ReleaseGroupSearchResponseDto>
                {
                    Outcome = FetchOutcome.Success,
                    Value = new ReleaseGroupSearchResponseDto
                    {
                        ReleaseGroupCount = providerCount,
                        ReleaseGroupOffset = key.Offset,
                        ReleaseGroups = [.. Enumerable.Range(key.Offset, 100)
                            .Select(value => new ReleaseGroupDto
                            {
                                Id = new Guid(value + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1).ToString("D"),
                                Title = "Album " + value
                            })]
                    }
                });
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = artistMbid,
            Ordinal = 0
        });

        // Act
        var result = await _subject.TryGetMetadataAsync(
            new MetadataFetchRequest
            {
                Item = new ContentHierarchyItem
                {
                    Id = Guid.NewGuid(),
                    ContentType = "music",
                    Level = "artist",
                    Slug = artistMbid,
                    Name = "Queen"
                },
                ExistingMetadata = [existing],
                DefaultLanguagePreference = "en"
            },
            default).ConfigureAwait(false);

        // Assert
        var metadata = result!;
        var contribution = metadata.Contribution!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(contribution.AlternateTexts.Single().Text, Is.EqualTo("クイーン"));
            Assert.That(contribution.Genres.Single().Name, Is.EqualTo("rock"));
            Assert.That(
                contribution.ExternalIds.Select(identifier => identifier.Source),
                Is.EqualTo(["musicbrainz", "isni", "ipi"]));
            Assert.That(metadata.Children, Has.Count.EqualTo(500));
        }

        _client.Verify(
            client => client.BrowseReleaseGroupsAsync(
                It.IsAny<BrowseCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(5));
    }

    [Test]
    public async Task TryGetMetadataAsync_AlbumContribution_WhenAlbumHasRepresentativeRelease()
    {
        // Arrange
        const string groupMbid = "550e8400-e29b-41d4-a716-446655440000";
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.ReleaseGroup),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    ReleaseGroup = new ReleaseGroupDto
                    {
                        Id = groupMbid,
                        Title = "A Night at the Opera",
                        FirstReleaseDate = "1975-11-21",
                        Releases =
                        [
                            new ReleaseDto
                            {
                                Id = "33333333-3333-4333-8333-333333333333",
                                Status = "Bootleg",
                                Date = "1974"
                            },
                            new ReleaseDto
                            {
                                Id = "a0b1c2d3-e4f5-4678-9012-abcdefabcdef",
                                Status = "Official",
                                Date = "1975-11-21"
                            }
                        ]
                    }
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Release),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Release = new ReleaseDto
                    {
                        Id = "a0b1c2d3-e4f5-4678-9012-abcdefabcdef",
                        Media =
                        [
                            new MediumDto
                            {
                                Position = 1,
                                Format = "CD",
                                TrackCount = 2,
                                Tracks =
                                [
                                    new TrackDto
                                    {
                                        Id = "11111111-1111-4111-8111-111111111111",
                                        Position = 1,
                                        Title = "Overture",
                                        Recording = new RecordingDto
                                        {
                                            Id = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
                                            Title = "Overture"
                                        }
                                    },
                                    new TrackDto
                                    {
                                        Id = "22222222-2222-4222-8222-222222222222",
                                        Position = 2,
                                        Title = "Overture reprise",
                                        Recording = new RecordingDto
                                        {
                                            Id = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
                                            Title = "Overture"
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                }
            });
        _coverClient.Setup(client => client.GetReleaseGroupFrontAsync(
                groupMbid,
                It.IsAny<int>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<Uri>
            {
                Outcome = FetchOutcome.Success,
                Value = new Uri("https://archive.org/front.jpg")
            });
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = groupMbid,
            Ordinal = 0
        });

        // Act
        var result = await _subject.TryGetMetadataAsync(
            new MetadataFetchRequest
            {
                Item = new ContentHierarchyItem
                {
                    Id = Guid.NewGuid(),
                    ContentType = "music",
                    Level = "album",
                    Slug = groupMbid,
                    Name = "A Night at the Opera"
                },
                ExistingMetadata = [existing],
                DefaultLanguagePreference = "en"
            },
            default).ConfigureAwait(false);

        // Assert
        var metadata = result!;
        var contribution = metadata.Contribution!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(
                contribution.ExternalIds.Single(identifier =>
                    identifier.Source == Constants.ProviderType).Value,
                Is.EqualTo(groupMbid));
            Assert.That(contribution.Images.Single().Url.AbsoluteUri, Is.EqualTo("https://archive.org/front.jpg"));
            Assert.That(
                metadata.Children.Select(child => child.Slug),
                Is.EqualTo(
                [
                    "11111111-1111-4111-8111-111111111111",
                    "22222222-2222-4222-8222-222222222222"
                ]));
            Assert.That(
                metadata.Children.Select(child => child.ExternalIds[Constants.ProviderType]),
                Is.EqualTo(
                [
                    "11111111-1111-4111-8111-111111111111",
                    "22222222-2222-4222-8222-222222222222"
                ]));
        }

        _client.Verify(
            client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Release),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task TryGetMetadataAsync_AlbumContribution_WhenRepresentativeReleaseLookupFails()
    {
        // Arrange
        const string groupMbid = "550e8400-e29b-41d4-a716-446655440000";
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.ReleaseGroup),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    ReleaseGroup = new ReleaseGroupDto
                    {
                        Id = groupMbid,
                        Title = "Fetched album",
                        FirstReleaseDate = "2001",
                        Releases =
                        [
                            new ReleaseDto
                            {
                                Id = "a0b1c2d3-e4f5-4678-9012-abcdefabcdef",
                                Status = "Official",
                                Date = "2001"
                            }
                        ]
                    }
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Release),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.Unavailable });
        _coverClient.Setup(client => client.GetReleaseGroupFrontAsync(
                groupMbid,
                It.IsAny<int>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<Uri> { Outcome = FetchOutcome.NotFound });
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = groupMbid,
            Ordinal = 0
        });

        // Act
        var result = await _subject.TryGetMetadataAsync(
            new MetadataFetchRequest
            {
                Item = new ContentHierarchyItem
                {
                    Id = Guid.NewGuid(),
                    ContentType = "music",
                    Level = "album",
                    Slug = groupMbid,
                    Name = "Fetched album"
                },
                ExistingMetadata = [existing],
                DefaultLanguagePreference = "en"
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Contribution!.Title, Is.EqualTo("Fetched album"));
            Assert.That(
                result.Contribution.ExternalIds.Select(identifier => identifier.Source),
                Is.EqualTo(["musicbrainz"]));
            Assert.That(result.Children, Is.Empty);
        }
    }

    [Test]
    public async Task TryGetMetadataAsync_TrackContribution_WhenRecordingHasNoSequenceOrCover()
    {
        // Arrange
        const string recordingMbid = "550e8400-e29b-41d4-a716-446655440000";
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Recording),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Recording = new RecordingDto
                    {
                        Id = recordingMbid,
                        Title = "Bohemian Rhapsody",
                        Length = 354000,
                        Isrcs = ["GBUM71029604"]
                    }
                }
            });
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = recordingMbid,
            Ordinal = 0
        });

        // Act
        var result = await _subject.TryGetMetadataAsync(
            new MetadataFetchRequest
            {
                Item = new ContentHierarchyItem
                {
                    Id = Guid.NewGuid(),
                    ContentType = "music",
                    Level = "track",
                    Slug = recordingMbid,
                    Name = "Bohemian Rhapsody"
                },
                ExistingMetadata = [existing],
                DefaultLanguagePreference = "en"
            },
            default).ConfigureAwait(false);

        // Assert
        var contribution = result!.Contribution!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(contribution.Duration, Is.EqualTo(TimeSpan.FromSeconds(354)));
            Assert.That(contribution.SequenceNumber, Is.Null);
            Assert.That(contribution.Images, Is.Empty);
            Assert.That(
                contribution.ExternalIds.Select(identifier => identifier.Source),
                Is.EqualTo(["musicbrainz", "isrc"]));
        }

        _coverClient.Verify(
            client => client.GetReleaseGroupFrontAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void TryGetMetadataAsync_Throws_WhenTheRecordingLookupCouldNotAnswer()
    {
        // Arrange
        const string recordingMbid = "550e8400-e29b-41d4-a716-446655440000";
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Recording),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.Unavailable });
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = recordingMbid,
            Ordinal = 0
        });
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "music",
                Level = "track",
                Slug = recordingMbid,
                Name = "Bohemian Rhapsody"
            },
            ExistingMetadata = [existing],
            DefaultLanguagePreference = "en"
        };

        // Act
        // Assert
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _subject.TryGetMetadataAsync(request, default).ConfigureAwait(false));
    }

    [Test]
    public async Task TryGetMetadataAsync_ReturnsNull_WhenTheRecordingIsNotFound()
    {
        // Arrange
        const string recordingMbid = "550e8400-e29b-41d4-a716-446655440000";
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Recording),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.NotFound });
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = recordingMbid,
            Ordinal = 0
        });

        // Act
        var result = await _subject.TryGetMetadataAsync(
            new MetadataFetchRequest
            {
                Item = new ContentHierarchyItem
                {
                    Id = Guid.NewGuid(),
                    ContentType = "music",
                    Level = "track",
                    Slug = recordingMbid,
                    Name = "Bohemian Rhapsody"
                },
                ExistingMetadata = [existing],
                DefaultLanguagePreference = "en"
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }
}
