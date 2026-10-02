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
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.MusicBrainz;

[TestFixture]
public class MusicBrainzContentMetadataProviderRelationshipTests
{
    private const string ReleaseGroupMbid = "550e8400-e29b-41d4-a716-446655440000";
    private const string ReleaseMbid = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string LabelMbid = "44444444-4444-4444-8444-444444444444";

    private Mock<IMusicBrainzClient> _client = null!;
    private IContentHierarchyMetadataRelationshipProvider _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new Mock<IMusicBrainzClient>();
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
        _subject = new ContentMetadataProvider(
            settingsResolver.Object,
            _client.Object,
            Mock.Of<ICoverArtArchiveClient>(),
            cache.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
    }

    [Test]
    public async Task GetRelationshipsAsync_EmitsLabelSubject_WhenRepresentativeReleaseCarriesLabelInfo()
    {
        // Arrange
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
                        Id = ReleaseGroupMbid,
                        Title = "Blue Train",
                        Releases = [new ReleaseDto { Id = ReleaseMbid, Status = "Official", Date = "1958-01-15" }]
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
                        Id = ReleaseMbid,
                        Title = "Blue Train",
                        LabelInfo =
                        [
                            new LabelInfoDto
                            {
                                CatalogNumber = "BLP 1577",
                                Label = new LabelDto { Id = LabelMbid, Name = "Blue Note" }
                            },
                            new LabelInfoDto
                            {
                                CatalogNumber = "BLP 1577-B",
                                Label = new LabelDto { Id = LabelMbid, Name = "Blue Note" }
                            },
                            new LabelInfoDto { Label = new LabelDto { Name = "   " } }
                        ]
                    }
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Label),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Label = new LabelDto
                    {
                        Id = LabelMbid,
                        Name = "Blue Note Records",
                        SortName = "Blue Note",
                        Disambiguation = "US jazz label",
                        LabelCode = 133,
                        LifeSpan = new LifeSpanDto { Begin = "1939-01-06", Ended = false },
                        Isnis = ["0000000123456789"],
                        Ipis = ["00045620792"],
                        Relations =
                        [
                            new RelationDto
                            {
                                Type = "official homepage",
                                Url = new UrlDto { Resource = "https://www.bluenote.com/" }
                            }
                        ]
                    }
                }
            });
        var item = new ContentHierarchyItem
        {
            ContentType = "music",
            Level = "album",
            Slug = "blue-train",
            Name = "Blue Train"
        };
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "musicbrainz:release-group:" + ReleaseGroupMbid
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = ReleaseGroupMbid,
            Ordinal = 0
        });

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = contribution,
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relationships, Has.Count.EqualTo(1));
            Assert.That(relationships[0].RelationshipKind, Is.EqualTo("music-label"));
            Assert.That(relationships[0].SubjectKind, Is.EqualTo("music-label"));
            Assert.That(relationships[0].SubjectName, Is.EqualTo("Blue Note Records"));
            Assert.That(relationships[0].RelationshipQualifier, Is.EqualTo("BLP 1577"));
            Assert.That(relationships[0].RelationshipOrdinal, Is.EqualTo(0));
            Assert.That(
                relationships[0].SubjectExternalIds[Constants.ProviderType],
                Is.EqualTo(LabelMbid));
            Assert.That(relationships[0].InitialSubjectContribution, Is.Not.Null);
            Assert.That(relationships[0].InitialSubjectContribution!.Title, Is.EqualTo("Blue Note Records"));
            Assert.That(relationships[0].InitialSubjectContribution!.SortTitle, Is.EqualTo("Blue Note"));
            Assert.That(relationships[0].InitialSubjectContribution!.Overview, Is.EqualTo("US jazz label"));
            Assert.That(
                relationships[0].InitialSubjectContribution!.Established,
                Is.EqualTo(new DateTime(1939, 1, 6, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(
                relationships[0].InitialSubjectContribution!.ExternalIds,
                Has.Exactly(1).Matches<ExternalId>(externalId =>
                    externalId.Source == "ifpi" && externalId.Value == "133"));
            Assert.That(
                relationships[0].InitialSubjectContribution!.References.Single().Url.AbsoluteUri,
                Is.EqualTo("https://www.bluenote.com/"));
            Assert.That(
                relationships[0].InitialSubjectContribution!.DateLastRefreshed,
                Is.EqualTo(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc)));
        }

        _client.Verify(
            client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Label),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task GetRelationshipsAsync_Result_WhenLabelLookupsFail()
    {
        // Arrange
        const string malformedLabelMbid = "55555555-5555-4555-8555-555555555555";
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
                        Id = ReleaseGroupMbid,
                        Releases = [new ReleaseDto { Id = ReleaseMbid, Status = "Official" }]
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
                        Id = ReleaseMbid,
                        LabelInfo =
                        [
                            new LabelInfoDto
                            {
                                CatalogNumber = "NOT FOUND",
                                Label = new LabelDto { Id = LabelMbid, Name = "Fallback One" }
                            },
                            new LabelInfoDto
                            {
                                CatalogNumber = "MALFORMED",
                                Label = new LabelDto { Id = malformedLabelMbid, Name = "Fallback Two" }
                            }
                        ]
                    }
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Label && key.Mbid == LabelMbid),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.NotFound });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Label && key.Mbid == malformedLabelMbid),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.Malformed });
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = Constants.ProviderType + ":" + ReleaseGroupMbid
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = ReleaseGroupMbid,
            Ordinal = 0
        });

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = new ContentHierarchyItem
                {
                    ContentType = "music",
                    Level = "album",
                    Slug = "item",
                    Name = "Item"
                },
                Contribution = contribution,
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                relationships.Select(value => value.SubjectName),
                Is.EqualTo(["Fallback One", "Fallback Two"]));
            Assert.That(
                relationships.Select(value => value.RelationshipQualifier),
                Is.EqualTo(["NOT FOUND", "MALFORMED"]));
            Assert.That(
                relationships.Select(value => value.InitialSubjectContribution!.Title),
                Is.EqualTo(["Fallback One", "Fallback Two"]));
            Assert.That(relationships.All(value => value.InitialSubjectContribution!.Overview is null), Is.True);
        }
    }

    [Test]
    public async Task GetRelationshipsAsync_Result_WhenLabelCountExceedsLookupCap()
    {
        // Arrange
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
                        Id = ReleaseGroupMbid,
                        Releases = [new ReleaseDto { Id = ReleaseMbid, Status = "Official" }]
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
                        Id = ReleaseMbid,
                        LabelInfo =
                        [
                            .. Enumerable.Range(1, 27)
                                .Select(index => new LabelInfoDto
                                {
                                    CatalogNumber = "CAT " + index,
                                    Label = new LabelDto
                                    {
                                        Id = $"00000000-0000-4000-8000-{index:D12}",
                                        Name = "Label " + index
                                    }
                                }),
                            new LabelInfoDto
                            {
                                CatalogNumber = "NO ID",
                                Label = new LabelDto { Name = "Independent" }
                            },
                        ]
                    }
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Label),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto>
            {
                Outcome = FetchOutcome.Success,
                Value = new EntityResponseDto
                {
                    Label = new LabelDto { Name = "Hydrated Label", Disambiguation = "Hydrated" }
                }
            });
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = Constants.ProviderType + ":" + ReleaseGroupMbid
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = ReleaseGroupMbid,
            Ordinal = 0
        });

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = new ContentHierarchyItem
                {
                    ContentType = "music",
                    Level = "album",
                    Slug = "item",
                    Name = "Item"
                },
                Contribution = contribution,
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relationships, Has.Count.EqualTo(28));
            Assert.That(relationships.Take(25).All(value => value.SubjectName == "Hydrated Label"), Is.True);
            Assert.That(relationships[25].SubjectName, Is.EqualTo("Label 26"));
            Assert.That(relationships[26].SubjectName, Is.EqualTo("Label 27"));
            Assert.That(relationships[26].RelationshipQualifier, Is.EqualTo("CAT 27"));
            Assert.That(relationships[27].SubjectName, Is.EqualTo("Independent"));
            Assert.That(relationships[27].RelationshipQualifier, Is.EqualTo("NO ID"));
            Assert.That(relationships[27].SubjectExternalIds, Is.Empty);
        }

        _client.Verify(
            client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Label),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(25));
    }

    [Test]
    public async Task GetRelationshipsAsync_Result_WhenItemLevelIsUnsupported()
    {
        // Arrange
        var item = new ContentHierarchyItem
        {
            ContentType = "music",
            Level = "artist",
            Slug = "john-coltrane",
            Name = "John Coltrane"
        };
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType
        };

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
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
            client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task GetRelationshipsAsync_Result_WhenAlbumCarriesPeopleRelations()
    {
        // Arrange
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
                        Id = ReleaseGroupMbid,
                        ArtistCredit =
                        [
                            new ArtistCreditDto
                            {
                                Name = "Album Artist",
                                Artist = new ArtistDto
                                {
                                    Id = "11111111-1111-4111-8111-111111111111",
                                    Name = "Album Artist",
                                    SortName = "Artist, Album"
                                }
                            }
                        ],
                        Releases = [new ReleaseDto { Id = ReleaseMbid, Status = "Official", Date = "2000-01-01" }]
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
                        Id = ReleaseMbid,
                        Relations =
                        [
                            new RelationDto
                            {
                                Type = "producer",
                                TargetType = "artist",
                                Artist = new ArtistDto
                                {
                                    Id = "22222222-2222-4222-8222-222222222222",
                                    Name = "Producer"
                                }
                            },
                            new RelationDto
                            {
                                Type = "mix",
                                TargetType = "artist",
                                Artist = new ArtistDto
                                {
                                    Id = "33333333-3333-4333-8333-333333333333",
                                    Name = "Mixer"
                                }
                            }
                        ],
                        Media =
                        [
                            new MediumDto
                            {
                                Position = 1,
                                Tracks =
                                [
                                    new TrackDto
                                    {
                                        Position = 1,
                                        Recording = new RecordingDto
                                        {
                                            Id = "44444444-4444-4444-8444-444444444444",
                                            Relations =
                                            [
                                                new RelationDto
                                                {
                                                    Type = "recording",
                                                    TargetType = "artist",
                                                    Artist = new ArtistDto
                                                    {
                                                        Id = "55555555-5555-4555-8555-555555555555",
                                                        Name = "Engineer"
                                                    }
                                                },
                                                new RelationDto
                                                {
                                                    Type = "performance",
                                                    TargetType = "work",
                                                    Work = new WorkDto
                                                    {
                                                        Id = "66666666-6666-4666-8666-666666666666",
                                                        Title = "Work",
                                                        Relations =
                                                        [
                                                            new RelationDto
                                                            {
                                                                Type = "composer",
                                                                TargetType = "artist",
                                                                Artist = new ArtistDto
                                                                {
                                                                    Id = "77777777-7777-4777-8777-777777777777",
                                                                    Name = "Composer"
                                                                }
                                                            },
                                                            new RelationDto
                                                            {
                                                                Type = "lyricist",
                                                                TargetType = "artist",
                                                                Artist = new ArtistDto
                                                                {
                                                                    Id = "88888888-8888-4888-8888-888888888888",
                                                                    Name = "Lyricist"
                                                                }
                                                            }
                                                        ]
                                                    }
                                                }
                                            ]
                                        }
                                    }
                                ]
                            }
                        ]
                    }
                }
            });
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = Constants.ProviderType + ":" + ReleaseGroupMbid
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = ReleaseGroupMbid,
            Ordinal = 0
        });

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = new ContentHierarchyItem
                {
                    ContentType = "music",
                    Level = "album",
                    Slug = "item",
                    Name = "Item"
                },
                Contribution = contribution,
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                relationships.Select(value => value.RelationshipKind),
                Is.EqualTo(["performer", "crew", "crew", "crew", "writer", "writer"]));
            Assert.That(
                relationships.Select(value => value.RelationshipQualifier),
                Is.EqualTo([null, "Producer", "Engineer", "Mixer", "Composer", "Lyricist"]));
            Assert.That(
                relationships.Select(value => value.SubjectName),
                Is.EqualTo(["Album Artist", "Producer", "Engineer", "Mixer", "Composer", "Lyricist"]));
            Assert.That(
                relationships.Select(value => value.RelationshipOrdinal),
                Is.EqualTo([0, 1, 2, 3, 4, 5]));
            Assert.That(
                relationships[0].SubjectExternalIds[Constants.ProviderType],
                Is.EqualTo("11111111-1111-4111-8111-111111111111"));
            Assert.That(relationships[0].InitialSubjectContribution!.SortTitle, Is.EqualTo("Artist, Album"));
        }
    }

    [Test]
    public async Task GetRelationshipsAsync_Result_WhenTrackCarriesPeopleRelations()
    {
        // Arrange
        const string recordingMbid = "11111111-1111-4111-8111-111111111111";
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
                        ArtistCredit =
                        [
                            new ArtistCreditDto
                            {
                                Name = "Singer",
                                Artist = new ArtistDto
                                {
                                    Id = "22222222-2222-4222-8222-222222222222",
                                    Name = "Singer"
                                }
                            }
                        ],
                        Relations =
                        [
                            new RelationDto
                            {
                                Type = "vocal",
                                TargetType = "artist",
                                Artist = new ArtistDto
                                {
                                    Id = "22222222-2222-4222-8222-222222222222",
                                    Name = "Singer"
                                },
                                Attributes = ["guest"],
                                AttributeCredits = new Dictionary<string, string> { ["vocal"] = "lead vocals" }
                            },
                            new RelationDto
                            {
                                Type = "performance",
                                TargetType = "work",
                                Work = new WorkDto
                                {
                                    Id = "33333333-3333-4333-8333-333333333333",
                                    Title = "Song",
                                    Relations =
                                    [
                                        new RelationDto
                                        {
                                            Type = "composer",
                                            TargetType = "artist",
                                            Artist = new ArtistDto
                                            {
                                                Id = "44444444-4444-4444-8444-444444444444",
                                                Name = "Composer"
                                            }
                                        }
                                    ]
                                }
                            }
                        ]
                    }
                }
            });
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = Constants.ProviderType + ":" + recordingMbid
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = recordingMbid,
            Ordinal = 0
        });

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = new ContentHierarchyItem
                {
                    ContentType = "music",
                    Level = "track",
                    Slug = "item",
                    Name = "Item"
                },
                Contribution = contribution,
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                relationships.Select(value => value.RelationshipKind),
                Is.EqualTo(["performer", "writer"]));
            Assert.That(
                relationships.Select(value => value.RelationshipQualifier),
                Is.EqualTo(["lead vocals", "Composer"]));
            Assert.That(
                relationships.Select(value => value.SubjectName),
                Is.EqualTo(["Singer", "Composer"]));
        }
    }

    [Test]
    public async Task GetRelationshipsAsync_Result_WhenTrackAppearsOnTheAlbumRelease()
    {
        // Arrange
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
                        Id = ReleaseGroupMbid,
                        Title = "In Sight",
                        Releases = [new ReleaseDto { Id = ReleaseMbid, Status = "Official", Date = "2019-05-03" }]
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
                        Id = ReleaseMbid,
                        Media =
                        [
                            new MediumDto
                            {
                                Position = 1,
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
                        ArtistCredit =
                        [
                            new ArtistCreditDto
                            {
                                Name = "Kind of Blue",
                                Artist = new ArtistDto
                                {
                                    Id = "4cd13a09-ea6e-4eb5-9bc4-4b9141bdd3cd",
                                    Name = "Kind of Blue"
                                }
                            }
                        ]
                    }
                }
            });
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "musicbrainz:track:63a9abaa-7b6c-4290-83e7-828de5802e7e"
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
            Ordinal = 0
        });
        var album = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = "musicbrainz:release-group:" + ReleaseGroupMbid
        };
        album.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = ReleaseGroupMbid,
            Ordinal = 0
        });

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = new ContentHierarchyItem
                {
                    ContentType = "music",
                    Level = "track",
                    Slug = "63a9abaa-7b6c-4290-83e7-828de5802e7e",
                    Name = "Bitter Blue"
                },
                Contribution = contribution,
                ExistingMetadata = [],
                AncestorMetadata = [album],
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relationships.Select(value => value.RelationshipKind), Is.EqualTo(["performer"]));
            Assert.That(relationships.Select(value => value.SubjectName), Is.EqualTo(["Kind of Blue"]));
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
    public async Task GetRelationshipsAsync_Result_WhenRepresentativeReleaseFails()
    {
        // Arrange
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
                        Id = ReleaseGroupMbid,
                        ArtistCredit =
                        [
                            new ArtistCreditDto
                            {
                                Artist = new ArtistDto
                                {
                                    Id = "11111111-1111-4111-8111-111111111111",
                                    Name = "Album Artist"
                                }
                            }
                        ],
                        Releases = [new ReleaseDto { Id = ReleaseMbid, Status = "Official" }]
                    }
                }
            });
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Release),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.Malformed });
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = Constants.ProviderType + ":" + ReleaseGroupMbid
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = ReleaseGroupMbid,
            Ordinal = 0
        });

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = new ContentHierarchyItem
                {
                    ContentType = "music",
                    Level = "album",
                    Slug = "item",
                    Name = "Item"
                },
                Contribution = contribution,
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relationships, Has.Count.EqualTo(1));
            Assert.That(relationships[0].RelationshipKind, Is.EqualTo("performer"));
            Assert.That(relationships[0].SubjectName, Is.EqualTo("Album Artist"));
        }
    }

    [Test]
    public async Task GetRelationshipsAsync_Result_WhenTrackMbidIsMissing()
    {
        // Arrange
        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = new ContentHierarchyItem
                {
                    ContentType = "music",
                    Level = "track",
                    Slug = "item",
                    Name = "Item"
                },
                Contribution = new MetadataContribution { ProviderType = Constants.ProviderType },
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(relationships, Is.Empty);

        _client.Verify(
            client => client.GetEntityAsync(
                It.IsAny<EntityCacheKey>(),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task GetRelationshipsAsync_Result_WhenRecordingCannotBeResolved()
    {
        // Arrange
        const string recordingMbid = "11111111-1111-4111-8111-111111111111";
        _client.Setup(client => client.GetEntityAsync(
                It.Is<EntityCacheKey>(key => key.Kind == EntityKind.Recording),
                It.IsAny<Settings>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FetchResult<EntityResponseDto> { Outcome = FetchOutcome.NotFound });
        var contribution = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            Source = Constants.ProviderType + ":" + recordingMbid
        };
        contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = recordingMbid,
            Ordinal = 0
        });

        // Act
        var relationships = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = new ContentHierarchyItem
                {
                    ContentType = "music",
                    Level = "track",
                    Slug = "item",
                    Name = "Item"
                },
                Contribution = contribution,
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(relationships, Is.Empty);
    }
}
