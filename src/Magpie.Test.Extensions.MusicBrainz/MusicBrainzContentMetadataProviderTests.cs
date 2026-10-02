using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz;
using Magpie.Extensions.MusicBrainz.Mapping;
using Magpie.Extensions.MusicBrainz.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Filtering;
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

    public static IEnumerable<TestCaseData> UnresolvedTrackAncestors()
    {
        yield return new TestCaseData(
            new MetadataContribution[]
            {
                new()
                {
                    ProviderType = "musicbrainz",
                    Source = "musicbrainz:release-group:b68c33f1-50da-4c27-92f4-c42cf5e66ece",
                    ExternalIds =
                    {
                        new ExternalId { Source = "musicbrainz", Value = "b68c33f1-50da-4c27-92f4-c42cf5e66ece" }
                    }
                }
            },
            "MusicBrainz has no release with track 63a9abaa-7b6c-4290-83e7-828de5802e7e.");
        yield return new TestCaseData(
            new MetadataContribution[]
            {
                new()
                {
                    ProviderType = "musicbrainz",
                    Source = "musicbrainz:artist:4cd13a09-ea6e-4eb5-9bc4-4b9141bdd3cd",
                    ExternalIds =
                    {
                        new ExternalId { Source = "musicbrainz", Value = "4cd13a09-ea6e-4eb5-9bc4-4b9141bdd3cd" }
                    }
                }
            },
            "MusicBrainz track 63a9abaa-7b6c-4290-83e7-828de5802e7e has no release group.");
    }

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
        cache.Setup(value => value.GetTrackReleasesAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<FetchResult<ReleaseSearchResponseDto>>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                string _,
                Func<CancellationToken, Task<FetchResult<ReleaseSearchResponseDto>>> loader,
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
            Assert.That(contribution.PublishDate, Is.EqualTo(new DateTime(1975, 11, 21, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(contribution.PublishDatePrecision, Is.EqualTo(TemporalPrecision.Day));
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
                result.Contribution.PublishDate,
                Is.EqualTo(new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(result.Contribution.PublishDatePrecision, Is.EqualTo(TemporalPrecision.Year));
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
    public async Task TryGetMetadataAsync_TrackAppearanceContribution_WhenTrackIsOnTheAlbumRelease()
    {
        // Arrange
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.ReleaseGroup && key.Mbid == "b68c33f1-50da-4c27-92f4-c42cf5e66ece"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    ReleaseGroup = new ReleaseGroupDto
                    {
                        Id = "b68c33f1-50da-4c27-92f4-c42cf5e66ece",
                        Title = "In Sight",
                        Releases =
                        [
                            new ReleaseDto
                            {
                                Id = "75c2e824-78bb-42c2-a6ec-5648357efd27",
                                Status = "Official",
                                Date = "2019-05-03"
                            },
                            new ReleaseDto
                            {
                                Id = "853a7dd8-0c27-40db-903c-38f378f8e8d8",
                                Status = "Official",
                                Date = "2021-11-19"
                            }
                        ]
                    }
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.Release && key.Mbid == "75c2e824-78bb-42c2-a6ec-5648357efd27"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Release = new ReleaseDto
                    {
                        Id = "75c2e824-78bb-42c2-a6ec-5648357efd27",
                        Media =
                        [
                            new MediumDto
                            {
                                Position = 1,
                                Tracks =
                                [
                                    new TrackDto
                                    {
                                        Id = "b8af3bd3-1816-4d34-883e-a1db7b62d675",
                                        Position = 1,
                                        Title = "Opening",
                                        Recording = new RecordingDto
                                        {
                                            Id = "8f737f98-13e1-4154-95bd-cb478e4a2f4f",
                                            Title = "Opening"
                                        }
                                    },
                                    new TrackDto
                                    {
                                        Id = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
                                        Position = 2,
                                        Title = "Bitter Blue",
                                        Length = 201000,
                                        Recording = new RecordingDto
                                        {
                                            Id = "e3063748-d1cb-450a-b35b-fa17446a9a6b",
                                            Title = "Bitter Blue"
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.Recording && key.Mbid == "63a9abaa-7b6c-4290-83e7-828de5802e7e"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.NotFound });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.Recording && key.Mbid == "e3063748-d1cb-450a-b35b-fa17446a9a6b"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Recording = new RecordingDto
                    {
                        Id = "e3063748-d1cb-450a-b35b-fa17446a9a6b",
                        Title = "Bitter Blue",
                        Disambiguation = "album version",
                        FirstReleaseDate = "2019-05-03",
                        Aliases = [new AliasDto { Name = "Amargo Azul", Locale = "es" }]
                    }
                }
            });
        var existing = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "musicbrainz:track:63a9abaa-7b6c-4290-83e7-828de5802e7e"
        };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
            Ordinal = 0
        });
        var album = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "musicbrainz:release-group:b68c33f1-50da-4c27-92f4-c42cf5e66ece"
        };
        album.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "b68c33f1-50da-4c27-92f4-c42cf5e66ece",
            Ordinal = 0
        });
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "music",
                Level = "track",
                Slug = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
                Name = "Bitter Blue"
            },
            ExistingMetadata = [existing],
            AncestorMetadata = [album],
            DefaultLanguagePreference = "en"
        };

        // Act
        var result = await _subject.TryGetMetadataAsync(request, default).ConfigureAwait(false);

        // Assert
        var contribution = result!.Contribution!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contribution.Source, Is.EqualTo("musicbrainz:track:63a9abaa-7b6c-4290-83e7-828de5802e7e"));
            Assert.That(contribution.Title, Is.EqualTo("Bitter Blue"));
            Assert.That(contribution.Overview, Is.EqualTo("album version"));
            Assert.That(contribution.SequenceNumber, Is.EqualTo(2));
            Assert.That(contribution.Duration, Is.EqualTo(TimeSpan.FromSeconds(201)));
            Assert.That(
                contribution.ExternalIds.Select(identifier => identifier.Value),
                Is.EqualTo(["63a9abaa-7b6c-4290-83e7-828de5802e7e"]));
            Assert.That(contribution.PublishDate, Is.EqualTo(new DateTime(2019, 5, 3)));
            Assert.That(contribution.AlternateTexts.Select(text => text.Text), Is.EqualTo(["Amargo Azul"]));
            Assert.That(contribution.DateLastRefreshed, Is.EqualTo(new DateTime(2026, 7, 13)));
        }

        _client.Verify(
            client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.Recording && key.Mbid == "63a9abaa-7b6c-4290-83e7-828de5802e7e"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task TryGetMetadataAsync_TrackAppearanceContribution_WhenAnotherReleaseHoldsTheTrack()
    {
        // Arrange
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.ReleaseGroup && key.Mbid == "b68c33f1-50da-4c27-92f4-c42cf5e66ece"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    ReleaseGroup = new ReleaseGroupDto
                    {
                        Id = "b68c33f1-50da-4c27-92f4-c42cf5e66ece",
                        Title = "In Sight",
                        Releases =
                        [
                            new ReleaseDto
                            {
                                Id = "75c2e824-78bb-42c2-a6ec-5648357efd27",
                                Status = "Official",
                                Date = "2019-05-03"
                            },
                            new ReleaseDto
                            {
                                Id = "853a7dd8-0c27-40db-903c-38f378f8e8d8",
                                Status = "Official",
                                Date = "2021-11-19"
                            }
                        ]
                    }
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.Release && key.Mbid == "75c2e824-78bb-42c2-a6ec-5648357efd27"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Release = new ReleaseDto
                    {
                        Id = "75c2e824-78bb-42c2-a6ec-5648357efd27",
                        Media =
                        [
                            new MediumDto
                            {
                                Position = 1,
                                Tracks =
                                [
                                    new TrackDto
                                    {
                                        Id = "b8af3bd3-1816-4d34-883e-a1db7b62d675",
                                        Position = 1,
                                        Title = "Opening",
                                        Recording = new RecordingDto
                                        {
                                            Id = "8f737f98-13e1-4154-95bd-cb478e4a2f4f",
                                            Title = "Opening"
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                }
            });
        _client.Setup(client => client.BrowseTrackReleasesAsync(
                "63a9abaa-7b6c-4290-83e7-828de5802e7e",
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<ReleaseSearchResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new ReleaseSearchResponseDto
                {
                    Releases =
                    [
                        new ReleaseDto
                        {
                            Id = "86ecd18e-f1fb-4ac7-b77a-5d0fd4bd830e",
                            Media =
                            [
                                new MediumDto
                                {
                                    Position = 1,
                                    Tracks =
                                    [
                                        new TrackDto
                                        {
                                            Id = "06ec70bd-4477-4a56-a623-5c9139de3444",
                                            Position = 1,
                                            Title = "Opening",
                                            Recording = new RecordingDto
                                            {
                                                Id = "8f737f98-13e1-4154-95bd-cb478e4a2f4f",
                                                Title = "Opening"
                                            }
                                        }
                                    ]
                                },
                                new MediumDto
                                {
                                    Position = 2,
                                    Tracks =
                                    [
                                        new TrackDto
                                        {
                                            Id = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
                                            Position = 1,
                                            Title = "Bitter Blue",
                                            Recording = new RecordingDto
                                            {
                                                Id = "e3063748-d1cb-450a-b35b-fa17446a9a6b",
                                                Title = "Bitter Blue"
                                            }
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.Recording && key.Mbid == "e3063748-d1cb-450a-b35b-fa17446a9a6b"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Recording = new RecordingDto
                    {
                        Id = "e3063748-d1cb-450a-b35b-fa17446a9a6b",
                        Title = "Bitter Blue"
                    }
                }
            });
        var existing = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "musicbrainz:track:63a9abaa-7b6c-4290-83e7-828de5802e7e"
        };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
            Ordinal = 0
        });
        var album = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "musicbrainz:release-group:b68c33f1-50da-4c27-92f4-c42cf5e66ece"
        };
        album.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "b68c33f1-50da-4c27-92f4-c42cf5e66ece",
            Ordinal = 0
        });
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "music",
                Level = "track",
                Slug = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
                Name = "Bitter Blue"
            },
            ExistingMetadata = [existing],
            AncestorMetadata = [album],
            DefaultLanguagePreference = "en"
        };

        // Act
        var result = await _subject.TryGetMetadataAsync(request, default).ConfigureAwait(false);

        // Assert
        var contribution = result!.Contribution!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contribution.Source, Is.EqualTo("musicbrainz:track:63a9abaa-7b6c-4290-83e7-828de5802e7e"));
            Assert.That(contribution.Title, Is.EqualTo("Bitter Blue"));
            Assert.That(contribution.SequenceNumber, Is.EqualTo(2));
        }

        _client.Verify(
            client => client.BrowseTrackReleasesAsync(
                "63a9abaa-7b6c-4290-83e7-828de5802e7e",
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCaseSource(nameof(UnresolvedTrackAncestors))]
    public void TryGetMetadataAsync_Throws_WhenNoReleaseHoldsTheTrack(
        MetadataContribution[] ancestorMetadata,
        string message)
    {
        // Arrange
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.ReleaseGroup && key.Mbid == "b68c33f1-50da-4c27-92f4-c42cf5e66ece"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    ReleaseGroup = new ReleaseGroupDto
                    {
                        Id = "b68c33f1-50da-4c27-92f4-c42cf5e66ece",
                        Title = "In Sight",
                        Releases =
                        [
                            new ReleaseDto
                            {
                                Id = "75c2e824-78bb-42c2-a6ec-5648357efd27",
                                Status = "Official",
                                Date = "2019-05-03"
                            },
                            new ReleaseDto
                            {
                                Id = "853a7dd8-0c27-40db-903c-38f378f8e8d8",
                                Status = "Official",
                                Date = "2021-11-19"
                            }
                        ]
                    }
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.Release && key.Mbid == "75c2e824-78bb-42c2-a6ec-5648357efd27"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Release = new ReleaseDto
                    {
                        Id = "75c2e824-78bb-42c2-a6ec-5648357efd27",
                        Media =
                        [
                            new MediumDto
                            {
                                Position = 1,
                                Tracks =
                                [
                                    new TrackDto
                                    {
                                        Id = "b8af3bd3-1816-4d34-883e-a1db7b62d675",
                                        Position = 1,
                                        Title = "Opening",
                                        Recording = new RecordingDto
                                        {
                                            Id = "8f737f98-13e1-4154-95bd-cb478e4a2f4f",
                                            Title = "Opening"
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                }
            });
        _client.Setup(client => client.BrowseTrackReleasesAsync(
                "63a9abaa-7b6c-4290-83e7-828de5802e7e",
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<ReleaseSearchResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new ReleaseSearchResponseDto { Releases = [] }
            });
        var existing = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "musicbrainz:track:63a9abaa-7b6c-4290-83e7-828de5802e7e"
        };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
            Ordinal = 0
        });
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "music",
                Level = "track",
                Slug = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
                Name = "Bitter Blue"
            },
            ExistingMetadata = [existing],
            AncestorMetadata = ancestorMetadata,
            DefaultLanguagePreference = "en"
        };

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _subject.TryGetMetadataAsync(request, default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo(message));
    }

    [Test]
    public void TryGetMetadataAsync_Throws_WhenTheReleaseLookupReturnsNoRelease()
    {
        // Arrange
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.ReleaseGroup && key.Mbid == "b68c33f1-50da-4c27-92f4-c42cf5e66ece"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    ReleaseGroup = new ReleaseGroupDto
                    {
                        Id = "b68c33f1-50da-4c27-92f4-c42cf5e66ece",
                        Title = "In Sight",
                        Releases =
                        [
                            new ReleaseDto
                            {
                                Id = "75c2e824-78bb-42c2-a6ec-5648357efd27",
                                Status = "Official",
                                Date = "2019-05-03"
                            },
                            new ReleaseDto
                            {
                                Id = "853a7dd8-0c27-40db-903c-38f378f8e8d8",
                                Status = "Official",
                                Date = "2021-11-19"
                            }
                        ]
                    }
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.Release && key.Mbid == "75c2e824-78bb-42c2-a6ec-5648357efd27"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto()
            });
        var existing = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "musicbrainz:track:63a9abaa-7b6c-4290-83e7-828de5802e7e"
        };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
            Ordinal = 0
        });
        var album = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "musicbrainz:release-group:b68c33f1-50da-4c27-92f4-c42cf5e66ece"
        };
        album.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "b68c33f1-50da-4c27-92f4-c42cf5e66ece",
            Ordinal = 0
        });
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "music",
                Level = "track",
                Slug = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
                Name = "Bitter Blue"
            },
            ExistingMetadata = [existing],
            AncestorMetadata = [album],
            DefaultLanguagePreference = "en"
        };

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _subject.TryGetMetadataAsync(request, default).ConfigureAwait(false));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo(
                "MusicBrainz returned no Release 75c2e824-78bb-42c2-a6ec-5648357efd27."));
    }

    [Test]
    public void TryGetMetadataAsync_Throws_WhenTheReleaseListsTheTrackWithoutATitle()
    {
        // Arrange
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.ReleaseGroup && key.Mbid == "b68c33f1-50da-4c27-92f4-c42cf5e66ece"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    ReleaseGroup = new ReleaseGroupDto
                    {
                        Id = "b68c33f1-50da-4c27-92f4-c42cf5e66ece",
                        Title = "In Sight",
                        Releases =
                        [
                            new ReleaseDto
                            {
                                Id = "75c2e824-78bb-42c2-a6ec-5648357efd27",
                                Status = "Official",
                                Date = "2019-05-03"
                            },
                            new ReleaseDto
                            {
                                Id = "853a7dd8-0c27-40db-903c-38f378f8e8d8",
                                Status = "Official",
                                Date = "2021-11-19"
                            }
                        ]
                    }
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.Release && key.Mbid == "75c2e824-78bb-42c2-a6ec-5648357efd27"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Release = new ReleaseDto
                    {
                        Id = "75c2e824-78bb-42c2-a6ec-5648357efd27",
                        Media =
                        [
                            new MediumDto
                            {
                                Position = 1,
                                Tracks =
                                [
                                    new TrackDto
                                    {
                                        Id = "b8af3bd3-1816-4d34-883e-a1db7b62d675",
                                        Position = 1,
                                        Title = "Opening",
                                        Recording = new RecordingDto
                                        {
                                            Id = "8f737f98-13e1-4154-95bd-cb478e4a2f4f",
                                            Title = "Opening"
                                        }
                                    },
                                    new TrackDto
                                    {
                                        Id = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
                                        Position = 2,
                                        Recording = new RecordingDto { Id = "e3063748-d1cb-450a-b35b-fa17446a9a6b" }
                                    }
                                ]
                            }
                        ]
                    }
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key =>
                    key.Kind == EntityKind.Recording && key.Mbid == "e3063748-d1cb-450a-b35b-fa17446a9a6b"),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Recording = new RecordingDto
                    {
                        Id = "e3063748-d1cb-450a-b35b-fa17446a9a6b",
                        Title = "Bitter Blue"
                    }
                }
            });
        var existing = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "musicbrainz:track:63a9abaa-7b6c-4290-83e7-828de5802e7e"
        };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
            Ordinal = 0
        });
        var album = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "musicbrainz:release-group:b68c33f1-50da-4c27-92f4-c42cf5e66ece"
        };
        album.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "b68c33f1-50da-4c27-92f4-c42cf5e66ece",
            Ordinal = 0
        });
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "music",
                Level = "track",
                Slug = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
                Name = "Bitter Blue"
            },
            ExistingMetadata = [existing],
            AncestorMetadata = [album],
            DefaultLanguagePreference = "en"
        };

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _subject.TryGetMetadataAsync(request, default).ConfigureAwait(false));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo(
                "MusicBrainz release 75c2e824-78bb-42c2-a6ec-5648357efd27 lists track " +
                "63a9abaa-7b6c-4290-83e7-828de5802e7e without a title."));
    }

    [Test]
    public void TryGetMetadataAsync_Throws_WhenTheReleaseGroupBrowseIsNotFound()
    {
        // Arrange
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Artist),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Artist = new ArtistDto { Id = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3", Name = "Queen" }
                }
            });
        _client.Setup(client => client.BrowseReleaseGroupsAsync(
                It.IsAny<BrowseCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<ReleaseGroupSearchResponseDto> { Outcome = FetchOutcome.NotFound });
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3",
            Ordinal = 0
        });
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "music",
                Level = "artist",
                Slug = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3",
                Name = "Queen"
            },
            ExistingMetadata = [existing],
            DefaultLanguagePreference = "en"
        };

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _subject.TryGetMetadataAsync(request, default).ConfigureAwait(false));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo(
                "MusicBrainz could not browse the release groups of " +
                "0383dadf-2a4e-4d10-a46a-e9e041da8eb3: NotFound."));
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
    public void TryGetMetadataAsync_Throws_WhenTheRecordingIsNotFound()
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
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _subject.TryGetMetadataAsync(request, default).ConfigureAwait(false));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo("MusicBrainz could not read Recording 550e8400-e29b-41d4-a716-446655440000: NotFound."));
    }
}
