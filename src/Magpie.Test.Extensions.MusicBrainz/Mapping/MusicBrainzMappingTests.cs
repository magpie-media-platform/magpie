using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

using Magpie.Extensions.MusicBrainz;
using Magpie.Extensions.MusicBrainz.Mapping;
using Magpie.Models;
using Magpie.Models.Filtering;

using NUnit.Framework;

namespace Magpie.Test.Extensions.MusicBrainz.Mapping;

[TestFixture]
public class MusicBrainzMappingTests
{
    [Test]
    public void ArtistContribution_Contribution_WhenArtistContainsMetadata()
    {
        // Arrange
        const string json = """
        {
          "id": "0383dadf-2a4e-4d10-a46a-e9e041da8eb3",
          "name": "Queen",
          "sort-name": "Queen",
          "disambiguation": "UK rock group",
          "life-span": { "begin": "1970-06", "end": "1991", "ended": true },
          "aliases": [
            { "name": "クイーン", "locale": "ja", "primary": true },
            { "name": "クイーン", "locale": "ja" },
            { "name": "  " }
          ],
          "genres": [
            { "name": "rock", "count": 9 },
            { "name": "glam rock", "count": 3 },
            { "name": "ignored", "count": 0 }
          ],
          "isnis": ["0000000121707484"],
          "ipis": ["00045620792"],
          "relations": [
            { "type": "official homepage", "url": { "resource": "https://queenonline.com/" } },
            { "type": "streaming music", "url": { "resource": "https://old.invalid/" } },
            { "type": "discogs", "url": { "resource": "/relative" } }
          ]
        }
        """;
        var artist = JsonSerializer.Deserialize<ArtistDto>(json)!;

        // Act
        var contribution = artist.ArtistContribution(
            new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc));
        var hit = artist.ToHit()!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contribution.Title, Is.EqualTo("Queen"));
            Assert.That(contribution.Established, Is.EqualTo(new DateTime(1970, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(contribution.Dissolved, Is.EqualTo(new DateTime(1991, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(contribution.AlternateTexts, Has.Count.EqualTo(1));
            Assert.That(contribution.AlternateTexts.Single().Language, Is.EqualTo("ja"));
            Assert.That(contribution.Genres.Select(genre => genre.Name), Is.EqualTo(["rock", "glam rock"]));
            Assert.That(
                contribution.ExternalIds.Select(identifier => identifier.Source),
                Is.EqualTo(["musicbrainz", "isni", "ipi"]));
            Assert.That(
                contribution.References.Select(reference => reference.Kind),
                Is.EqualTo(["official homepage", "streaming music"]));
            Assert.That(hit.Aliases, Has.Count.EqualTo(3));
            Assert.That(hit.BirthDate?.Precision, Is.EqualTo(TemporalPrecision.Month));
            Assert.That(hit.DeathDate?.Precision, Is.EqualTo(TemporalPrecision.Year));
        }
    }

    [Test]
    public void AlbumContribution_Contribution_WhenAlbumContainsMetadata()
    {
        // Arrange
        const string groupJson = """
        {
          "id": "550e8400-e29b-41d4-a716-446655440000",
          "title": "A Night at the Opera",
          "first-release-date": "1975-11-21",
          "aliases": [{ "name": "Una noche en la ópera", "locale": "es" }],
          "genres": [{ "name": "rock", "count": 5 }],
          "relations": [
            { "type": "discogs", "url": { "resource": "https://discogs.com/master/1" } },
            { "type": "bandcamp", "url": { "resource": "https://example.bandcamp.com" } }
          ]
        }
        """;
        const string releaseJson = """
        {
          "id": "a0b1c2d3-e4f5-4678-9012-abcdefabcdef",
          "text-representation": { "language": "eng", "script": "Latn" },
          "media": [
            { "position": 1, "format": "CD", "track-count": 2, "tracks": [] },
            { "position": 2, "format": "Digital Media", "track-count": 3, "tracks": [] },
            { "position": 3, "format": "CD", "track-count": 1, "tracks": [] }
          ]
        }
        """;
        var group = JsonSerializer.Deserialize<ReleaseGroupDto>(groupJson)!;
        var release = JsonSerializer.Deserialize<ReleaseDto>(releaseJson)!;

        // Act
        var contribution = group.AlbumContribution(
            release,
            new Uri("https://archive.org/front.jpg"),
            DateTime.UtcNow);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                contribution.ExternalIds.Select(identifier => identifier.Source),
                Is.EqualTo(["musicbrainz"]));
            Assert.That(contribution.Genres.Single().Name, Is.EqualTo("rock"));
            Assert.That(contribution.Language, Is.EqualTo("eng"));
            Assert.That(contribution.AlternateTexts.Single().Text, Is.EqualTo("Una noche en la ópera"));
            Assert.That(
                contribution.References.Select(reference => reference.Kind),
                Is.EqualTo(["discogs", "bandcamp"]));
            Assert.That(contribution.CustomMetadata.Single(value => value.Key == "trackCount").Value, Is.EqualTo("6"));
            Assert.That(
                contribution.CustomMetadata.Single(value => value.Key == "format").Value,
                Is.EqualTo("CD, Digital Media"));
            Assert.That(contribution.Images.Single().Url.AbsoluteUri, Is.EqualTo("https://archive.org/front.jpg"));
        }
    }

    [Test]
    public void RecordingContribution_Contribution_WhenRecordingContainsMetadata()
    {
        // Arrange
        var recording = new RecordingDto
        {
            Id = "550e8400-e29b-41d4-a716-446655440000",
            Title = "Bohemian Rhapsody",
            FirstReleaseDate = "1975-10-31",
            Aliases = [new AliasDto { Name = "Rapsodia bohemia", Locale = "es" }],
            Genres = [new GenreDto { Name = "rock", Count = 5 }],
            Relations = [new RelationDto
            {
                Type = "official homepage",
                Url = new UrlDto { Resource = "https://example.com/recording" }
            }]
        };

        // Act
        var contribution = recording.RecordingContribution(DateTime.UnixEpoch);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contribution.AlternateTexts.Single().Text, Is.EqualTo("Rapsodia bohemia"));
            Assert.That(
                contribution.PublishDate,
                Is.EqualTo(new DateTime(1975, 10, 31, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(contribution.Genres.Single().Name, Is.EqualTo("rock"));
            Assert.That(
                contribution.References.Single().Url.AbsoluteUri,
                Is.EqualTo("https://example.com/recording"));
        }
    }

    [Test]
    public void TrackChildren_Children_WhenReleaseHasDuplicateRecordingTracks()
    {
        // Arrange
        const string json = """
        {
          "id": "a0b1c2d3-e4f5-4678-9012-abcdefabcdef",
          "media": [
            {
              "position": 1,
              "format": "CD",
              "tracks": [
                {
                  "id": "11111111-1111-4111-8111-111111111111",
                  "position": 1,
                  "title": "Overture",
                  "length": 1000,
                  "recording": {
                    "id": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
                    "title": "Overture",
                    "isrcs": ["US-AAA-00-00001", "GBBBB0100002"]
                  }
                },
                {
                  "id": "22222222-2222-4222-8222-222222222222",
                  "position": 2,
                  "title": "Overture (reprise)",
                  "recording": {
                    "id": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
                    "title": "Overture"
                  }
                },
                {
                  "position": 3,
                  "title": "Missing track identity",
                  "recording": { "id": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb" }
                }
              ]
            }
          ]
        }
        """;
        var release = JsonSerializer.Deserialize<ReleaseDto>(json)!;

        // Act
        var children = release.TrackChildren();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(children, Has.Count.EqualTo(2));
            Assert.That(
                children.Select(child => child.Slug),
                Is.EqualTo(
                [
                    "11111111-1111-4111-8111-111111111111",
                    "22222222-2222-4222-8222-222222222222"
                ]));
            Assert.That(
                children.Select(child => child.ExternalIds["musicbrainz"]),
                Is.EqualTo(
                [
                    "11111111-1111-4111-8111-111111111111",
                    "22222222-2222-4222-8222-222222222222"
                ]));
            Assert.That(
                children[0].InitialContribution!.ExternalIds.Select(identifier => identifier.Source),
                Is.EqualTo(["musicbrainz", "isrc", "isrc"]));
            Assert.That(children[1].Ordinal, Is.EqualTo(2));
        }
    }

    [Test]
    public void SelectRepresentativeRelease_OfficialEarliestRelease_WhenCandidatesDiffer()
    {
        // Arrange
        var group = new ReleaseGroupDto
        {
            Releases =
            [
                new ReleaseDto
                {
                    Id = "33333333-3333-4333-8333-333333333333",
                    Status = "Bootleg",
                    Date = "1970"
                },
                new ReleaseDto
                {
                    Id = "22222222-2222-4222-8222-222222222222",
                    Status = "Official",
                    Date = "2000"
                },
                new ReleaseDto
                {
                    Id = "11111111-1111-4111-8111-111111111111",
                    Status = "Official",
                    Date = "1999"
                }
            ]
        };

        // Act
        var selected = group.SelectRepresentativeRelease();

        // Assert
        Assert.That(selected!.Id, Is.EqualTo("11111111-1111-4111-8111-111111111111"));
    }

    [Test]
    public void SelectDurationRelease_EarliestPreciseCompleteRelease_WhenEditionsDiffer()
    {
        // Arrange
        var group = new ReleaseGroupDto
        {
            Releases =
            [
                new ReleaseDto
                {
                    Id = "11111111-1111-4111-8111-111111111111",
                    Status = "Official",
                    Date = "1959",
                    Media = [new MediumDto { TrackCount = 3 }]
                },
                new ReleaseDto
                {
                    Id = "22222222-2222-4222-8222-222222222222",
                    Status = "Official",
                    Date = "1959-08-17",
                    Media = [new MediumDto { TrackCount = 5 }]
                },
                new ReleaseDto
                {
                    Id = "33333333-3333-4333-8333-333333333333",
                    Status = "Official",
                    Date = "2000-01-01",
                    Media = [new MediumDto { TrackCount = 20 }]
                }
            ]
        };

        // Act
        var selected = group.SelectDurationRelease();

        // Assert
        Assert.That(selected!.Id, Is.EqualTo("22222222-2222-4222-8222-222222222222"));
    }

    [Test]
    public void ToCandidate_MapsSearchFields_WhenRecordingContainsMetadata()
    {
        // Arrange
        var recording = new RecordingDto
        {
            Id = "550e8400-e29b-41d4-a716-446655440000",
            Title = "Bohemian Rhapsody",
            Disambiguation = "album version",
            FirstReleaseDate = "1975-10-31",
            Length = 354000,
            Isrcs = ["GBUM71029604"],
            Aliases = [new AliasDto { Name = "Rapsodia bohemia", Locale = "es" }],
            ArtistCredit =
            [
                new ArtistCreditDto
                {
                    Name = "Queen",
                    Artist = new ArtistDto { Id = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3", Name = "Queen" }
                }
            ]
        };

        // Act
        var hit = recording.ToHit()!;
        var candidate = hit.ToCandidate();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate.Overview, Is.EqualTo("album version"));
            Assert.That(candidate.SubjectReferences.Single().DisplayName, Is.EqualTo("Queen"));
            Assert.That(hit.FirstReleaseDate?.Precision, Is.EqualTo(TemporalPrecision.Day));
            Assert.That(hit.Aliases?.Single().Name, Is.EqualTo("Rapsodia bohemia"));
        }
    }

    [Test]
    public void ToHit_KeepsTheCreditName_WhenTheArtistCreditHasNoNestedArtist()
    {
        // Arrange
        var recording = new RecordingDto
        {
            Id = "550e8400-e29b-41d4-a716-446655440000",
            Title = "Inline credit",
            ArtistCredit = [new ArtistCreditDto { Name = "Inline Artist" }]
        };

        // Act
        var hit = recording.ToHit()!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hit.ArtistReferences.Single().CreditName, Is.EqualTo("Inline Artist"));
            Assert.That(hit.ArtistReferences.Single().Mbid, Is.Null);
        }
    }

    [TestCase(Constants.PersonArtistType, Constants.PersonSubjectKind)]
    [TestCase("Group", "artist")]
    [TestCase(null, "artist")]
    public void ToCandidate_Result_WhenArtistCreditTypeDeterminesSubjectKind(
        string? artistType,
        string expectedSubjectKind)
    {
        // Arrange
        var recording = new RecordingDto
        {
            Id = "550e8400-e29b-41d4-a716-446655440000",
            Title = "Track",
            ArtistCredit =
            [
                new ArtistCreditDto
                {
                    Name = "Artist",
                    Artist = new ArtistDto
                    {
                        Id = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3",
                        Name = "Artist",
                        Type = artistType
                    }
                }
            ]
        };

        // Act
        var hit = recording.ToHit()!;
        var candidate = hit.ToCandidate();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hit.ArtistReferences.Single().ArtistType, Is.EqualTo(artistType));
            Assert.That(candidate.SubjectReferences.Single().SubjectKind, Is.EqualTo(expectedSubjectKind));
        }
    }

    [Test]
    public void TrackChildren_Result_WhenTrackArtistCreditDiffersFromRecordingCredit()
    {
        // Arrange
        const string json = """
        {
          "media": [{
            "position": 1,
            "tracks": [{
              "id": "11111111-1111-4111-8111-111111111111",
              "position": 1,
              "title": "Tonight I Celebrate My Love",
              "artist-credit": [
                { "name": "Peabo Bryson", "joinphrase": " & ",
                  "artist": { "id": "b8170690-3a95-42e4-adef-afc9576ed90c", "name": "Peabo Bryson" } },
                { "name": "Roberta Flack",
                  "artist": { "id": "5298bbcb-7330-49bf-a780-2d757f10a20a", "name": "Roberta Flack" } }
              ],
              "recording": {
                "id": "3e8f6736-bab4-489a-828c-18067222a350",
                "title": "Tonight I Celebrate My Love",
                "artist-credit": [
                  { "name": "Peabo Bryson", "joinphrase": " with ",
                    "artist": { "id": "b8170690-3a95-42e4-adef-afc9576ed90c", "name": "Peabo Bryson" } },
                  { "name": "Roberta Flack",
                    "artist": { "id": "5298bbcb-7330-49bf-a780-2d757f10a20a", "name": "Roberta Flack" } }
                ]
              }
            }]
          }]
        }
        """;
        var release = JsonSerializer.Deserialize<ReleaseDto>(json)!;

        // Act
        var child = release.TrackChildren().Single();

        // Assert
        Assert.That(child.InitialContribution!.Overview, Is.EqualTo("Peabo Bryson & Roberta Flack"));
    }

    [Test]
    public void TrackChildren_Result_WhenTrackArtistCreditMatchesRecordingCredit()
    {
        // Arrange
        var credit = new ArtistCreditDto
        {
            Name = "Queen",
            Artist = new ArtistDto { Id = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3", Name = "Queen" }
        };
        var release = new ReleaseDto
        {
            Media = [new MediumDto
            {
                Tracks = [new TrackDto
                {
                    Id = "11111111-1111-4111-8111-111111111111",
                    Title = "Bohemian Rhapsody",
                    ArtistCredit = [credit],
                    Recording = new RecordingDto
                    {
                        Id = "550e8400-e29b-41d4-a716-446655440000",
                        ArtistCredit = [credit]
                    }
                }]
            }]
        };

        // Act
        var child = release.TrackChildren().Single();

        // Assert
        Assert.That(child.InitialContribution!.Overview, Is.Null);
    }

    [TestCase("Person", true)]
    [TestCase("Group", false)]
    [TestCase(null, false)]
    public void ArtistMapping_Result_WhenArtistTypeControlsOutputLifeSpan(string? artistType, bool expectedPerson)
    {
        // Arrange
        var artist = new ArtistDto
        {
            Id = "0383dadf-2a4e-4d10-a46a-e9e041da8eb3",
            Name = "Artist",
            Type = artistType,
            Country = " gb ",
            LifeSpan = new LifeSpanDto { Begin = "1970-06", End = "1991" }
        };

        // Act
        var contribution = artist.ArtistContribution(DateTime.UnixEpoch);
        var hit = artist.ToHit()!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contribution.BirthDate is not null, Is.EqualTo(expectedPerson));
            Assert.That(contribution.DeathDate is not null, Is.EqualTo(expectedPerson));
            Assert.That(contribution.Established is not null, Is.EqualTo(!expectedPerson));
            Assert.That(contribution.Dissolved is not null, Is.EqualTo(!expectedPerson));
            Assert.That(hit.BirthDate, Is.Not.Null);
            Assert.That(hit.DeathDate, Is.Not.Null);
            Assert.That(hit.Established, Is.Not.Null);
            Assert.That(hit.Dissolved, Is.Not.Null);
            Assert.That(
                contribution.CustomMetadata.Single(value => value.Key == "country").Value,
                Is.EqualTo("GB"));
        }
    }

    [Test]
    public void LabelSubjectRelationships_Result_WhenEnrichedLabelIsAvailable()
    {
        // Arrange
        const string labelMbid = "44444444-4444-4444-8444-444444444444";
        var release = new ReleaseDto
        {
            LabelInfo = [new LabelInfoDto
            {
                CatalogNumber = "BLP 1577",
                Label = new LabelDto { Id = labelMbid, Name = "Blue Note" }
            }]
        };
        var labels = new Dictionary<string, LabelDto>(StringComparer.OrdinalIgnoreCase)
        {
            [labelMbid] = new LabelDto
            {
                Id = labelMbid,
                Name = "Blue Note",
                SortName = "Blue Note",
                Disambiguation = "US jazz label",
                LabelCode = 133,
                LifeSpan = new LifeSpanDto { Begin = "1939", End = "2020" },
                Isnis = ["0000000123456789"],
                Ipis = ["00045620792"],
                Relations = [new RelationDto
                {
                    Type = "catalog site",
                    Url = new UrlDto { Resource = "https://example.test/catalog" }
                }]
            }
        };

        // Act
        var relationship = release.LabelSubjectRelationships(
            DateTime.UnixEpoch,
            labels).Single();
        var contribution = relationship.InitialSubjectContribution!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relationship.RelationshipQualifier, Is.EqualTo("BLP 1577"));
            Assert.That(contribution.Title, Is.EqualTo("Blue Note"));
            Assert.That(contribution.SortTitle, Is.EqualTo("Blue Note"));
            Assert.That(contribution.Overview, Is.EqualTo("US jazz label"));
            Assert.That(contribution.Established, Is.EqualTo(new DateTime(1939, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(contribution.Dissolved, Is.EqualTo(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(
                contribution.ExternalIds.Select(value => value.Source),
                Is.EqualTo(["musicbrainz", "ifpi", "isni", "ipi"]));
            Assert.That(
                contribution.ExternalIds.Single(value => value.Source == "ifpi").Value,
                Is.EqualTo("133"));
            Assert.That(contribution.References.Single().Kind, Is.EqualTo("catalog site"));
        }
    }

    [Test]
    public void Deserialize_Result_WhenRelationCarriesArtistWorkAndAttributeCredits()
    {
        // Arrange
        const string json = """
        {
          "id": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
          "relations": [
            {
              "type": "producer",
              "target-type": "artist",
              "direction": "backward",
              "artist": {
                "id": "11111111-1111-4111-8111-111111111111",
                "name": "Producer",
                "sort-name": "Producer, The"
              }
            }
          ],
          "media": [{
            "position": 1,
            "tracks": [{
              "position": 1,
              "recording": {
                "id": "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
                "relations": [
                  {
                    "type": "instrument",
                    "target-type": "artist",
                    "artist": {
                      "id": "22222222-2222-4222-8222-222222222222",
                      "name": "Performer"
                    },
                    "attributes": ["additional", "electric guitar"],
                    "attribute-values": { "instrument": "electric guitar" },
                    "attribute-credits": { "instrument": "lead guitar" }
                  },
                  {
                    "type": "performance",
                    "target-type": "work",
                    "work": {
                      "id": "33333333-3333-4333-8333-333333333333",
                      "title": "Composition",
                      "relations": [{
                        "type": "composer",
                        "target-type": "artist",
                        "artist": {
                          "id": "44444444-4444-4444-8444-444444444444",
                          "name": "Composer"
                        }
                      }]
                    }
                  }
                ]
              }
            }]
          }]
        }
        """;

        // Act
        var release = JsonSerializer.Deserialize<ReleaseDto>(json)!;
        var medium = release.Media!.Single()!;
        var recording = medium.Tracks!.Single()!.Recording!;
        var relations = recording.Relations!;
        var performer = relations.First();
        var work = relations.Last().Work!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(release.Relations!.Single().Artist!.SortName, Is.EqualTo("Producer, The"));
            Assert.That(performer.Attributes, Is.EqualTo(["additional", "electric guitar"]));
            Assert.That(performer.AttributeValues!["instrument"], Is.EqualTo("electric guitar"));
            Assert.That(performer.AttributeCredits!["instrument"], Is.EqualTo("lead guitar"));
            Assert.That(work.Title, Is.EqualTo("Composition"));
            Assert.That(work.Relations!.Single().Artist!.Name, Is.EqualTo("Composer"));
        }
    }

    [Test]
    public void TrackSubjectRelationships_Result_WhenPerformersAndWritersNeedNormalization()
    {
        // Arrange
        var recording = new RecordingDto
        {
            ArtistCredit =
            [
                new ArtistCreditDto
                {
                    Name = "Credited Performer",
                    Artist = new ArtistDto
                    {
                        Id = "00000000-0000-4000-8000-000000000001",
                        Name = "Credited Performer",
                        SortName = "Credited Performer, The"
                    }
                }
            ],
            Relations =
            [
                new RelationDto
                {
                    Type = Constants.InstrumentRelationshipType,
                    TargetType = Constants.ArtistRelationTarget,
                    Artist = new ArtistDto
                    {
                        Id = "00000000-0000-4000-8000-000000000001",
                        Name = "Credited Performer",
                        SortName = "Credited Performer, The"
                    },
                    Attributes = ["additional", "electric guitar"],
                    AttributeCredits = new Dictionary<string, string> { ["instrument"] = "lead guitar" }
                },
                new RelationDto
                {
                    Type = Constants.VocalRelationshipType,
                    TargetType = Constants.ArtistRelationTarget,
                    Artist = new ArtistDto
                    {
                        Id = "00000000-0000-4000-8000-000000000002",
                        Name = "Vocalist",
                        SortName = "Vocalist, The"
                    },
                    Attributes = ["guest"]
                },
                new RelationDto
                {
                    Type = Constants.PerformerRelationshipType,
                    TargetType = Constants.ArtistRelationTarget,
                    Artist = new ArtistDto
                    {
                        Id = "00000000-0000-4000-8000-000000000003",
                        Name = "Keyboardist",
                        SortName = "Keyboardist, The"
                    },
                    Attributes = ["solo", "piano"]
                },
                new RelationDto
                {
                    Type = Constants.PerformingOrchestraRelationshipType,
                    TargetType = Constants.ArtistRelationTarget,
                    Artist = new ArtistDto
                    {
                        Id = "00000000-0000-4000-8000-000000000004",
                        Name = "Orchestra",
                        SortName = "Orchestra, The"
                    }
                },
                new RelationDto
                {
                    Type = "performance",
                    TargetType = Constants.WorkRelationTarget,
                    Work = new WorkDto
                    {
                        Id = "00000000-0000-4000-8000-000000000999",
                        Title = "Work",
                        Relations =
                        [
                            new RelationDto
                            {
                                Type = Constants.ComposerRelationshipType,
                                TargetType = Constants.ArtistRelationTarget,
                                Artist = new ArtistDto
                                {
                                    Id = "00000000-0000-4000-8000-000000000005",
                                    Name = "Composer",
                                    SortName = "Composer, The"
                                }
                            },
                            new RelationDto
                            {
                                Type = Constants.LyricistRelationshipType,
                                TargetType = Constants.ArtistRelationTarget,
                                Artist = new ArtistDto
                                {
                                    Id = "00000000-0000-4000-8000-000000000006",
                                    Name = "Lyricist",
                                    SortName = "Lyricist, The"
                                }
                            },
                            new RelationDto
                            {
                                Type = Constants.WriterRelationshipType,
                                TargetType = Constants.ArtistRelationTarget,
                                Artist = new ArtistDto
                                {
                                    Id = "00000000-0000-4000-8000-000000000007",
                                    Name = "Writer",
                                    SortName = "Writer, The"
                                }
                            }
                        ]
                    }
                }
            ]
        };

        // Act
        var relationships = recording.TrackSubjectRelationships(DateTime.UnixEpoch);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                relationships.Select(relationship => relationship.RelationshipKind),
                Is.EqualTo(["performer", "performer", "performer", "performer", "writer", "writer", "writer"]));
            Assert.That(
                relationships.Select(relationship => relationship.RelationshipQualifier),
                Is.EqualTo(["lead guitar", "piano", null, "Vocals", "Composer", "Lyricist", "Writer"]));
            Assert.That(
                relationships.Select(relationship => relationship.RelationshipOrdinal),
                Is.EqualTo([0, 1, 2, 3, 4, 5, 6]));
            Assert.That(
                relationships,
                Has.All.Property(nameof(SubjectRelationshipDefinition.SubjectKind))
                    .EqualTo(Constants.PersonSubjectKind));
            Assert.That(
                relationships[0].InitialSubjectContribution!.SortTitle,
                Is.EqualTo("Credited Performer, The"));
        }
    }

    [Test]
    public void AlbumSubjectRelationships_Result_WhenCrewRoleVariantsArePresent()
    {
        // Arrange
        var types = new[]
        {
            Constants.ProducerRelationshipType,
            Constants.EngineerRelationshipType,
            Constants.AudioRelationshipType,
            Constants.AudioEngineerRelationshipType,
            Constants.SoundRelationshipType,
            Constants.SoundEngineerRelationshipType,
            Constants.RecordingRelationshipType,
            Constants.RecordingEngineerRelationshipType,
            Constants.FieldRecordistRelationshipType,
            Constants.MasteringRelationshipType,
            Constants.MasteringEngineerRelationshipType,
            Constants.BalanceRelationshipType,
            Constants.MixRelationshipType,
            Constants.MixerRelationshipType,
            Constants.ArrangerRelationshipType,
            Constants.InstrumentArrangerRelationshipType,
            Constants.VocalArrangerRelationshipType,
            Constants.OrchestratorRelationshipType
        };
        var release = new ReleaseDto
        {
            Relations = [.. types
                .Select((type, index) => new RelationDto
                {
                    Type = type,
                    TargetType = Constants.ArtistRelationTarget,
                    Artist = new ArtistDto
                    {
                        Id = "00000000-0000-4000-8000-" + (index + 1).ToString("D12", CultureInfo.InvariantCulture),
                        Name = "Person " + index,
                        SortName = "Person " + index + ", The"
                    }
                })]
        };

        // Act
        var relationships = new ReleaseGroupDto().AlbumSubjectRelationships(
            release,
            DateTime.UnixEpoch);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relationships, Has.Count.EqualTo(types.Length));
            Assert.That(relationships.Count(value => value.RelationshipQualifier == "Producer"), Is.EqualTo(1));
            Assert.That(relationships.Count(value => value.RelationshipQualifier == "Engineer"), Is.EqualTo(11));
            Assert.That(relationships.Count(value => value.RelationshipQualifier == "Mixer"), Is.EqualTo(2));
            Assert.That(relationships.Count(value => value.RelationshipQualifier == "Arranger"), Is.EqualTo(4));
            Assert.That(
                relationships,
                Has.All.Property(nameof(SubjectRelationshipDefinition.RelationshipKind))
                    .EqualTo(Constants.CrewRelationshipKind));
        }
    }

    [Test]
    public void AlbumSubjectRelationships_Result_WhenRelationshipGroupsExceedCaps()
    {
        // Arrange
        var group = new ReleaseGroupDto
        {
            ArtistCredit = [.. Enumerable.Range(1, 31)
                .Select(index => new ArtistCreditDto
                {
                    Name = "Performer " + index,
                    Artist = new ArtistDto
                    {
                        Id = "00000000-0000-4000-8000-" + index.ToString("D12", CultureInfo.InvariantCulture),
                        Name = "Performer " + index,
                        SortName = "Performer " + index + ", The"
                    }
                })]
        };
        var release = new ReleaseDto
        {
            LabelInfo =
            [
                new LabelInfoDto
                {
                    Label = new LabelDto { Id = "00000000-0000-4000-8000-000000000100", Name = "Label" }
                }
            ],
            Relations = [.. Enumerable.Range(101, 21)
                .Select(index => new RelationDto
                {
                    Type = Constants.ProducerRelationshipType,
                    TargetType = Constants.ArtistRelationTarget,
                    Artist = new ArtistDto
                    {
                        Id = "00000000-0000-4000-8000-" + index.ToString("D12", CultureInfo.InvariantCulture),
                        Name = "Crew " + index,
                        SortName = "Crew " + index + ", The"
                    }
                })],
            Media =
            [
                new MediumDto
                {
                    Tracks =
                    [
                        new TrackDto
                        {
                            Recording = new RecordingDto
                            {
                                Relations =
                                [
                                    new RelationDto
                                    {
                                        Type = "performance",
                                        TargetType = Constants.WorkRelationTarget,
                                        Work = new WorkDto
                                        {
                                            Id = "00000000-0000-4000-8000-000000000999",
                                            Title = "Work",
                                            Relations = [.. Enumerable.Range(201, 11)
                                                .Select(index => new RelationDto
                                                {
                                                    Type = Constants.ComposerRelationshipType,
                                                    TargetType = Constants.ArtistRelationTarget,
                                                    Artist = new ArtistDto
                                                    {
                                                        Id = "00000000-0000-4000-8000-" + index.ToString(
                                                            "D12",
                                                            CultureInfo.InvariantCulture),
                                                        Name = "Writer " + index,
                                                        SortName = "Writer " + index + ", The"
                                                    }
                                                })]
                                        }
                                    }
                                ]
                            }
                        }
                    ]
                }
            ]
        };

        // Act
        var relationships = group.AlbumSubjectRelationships(release, DateTime.UnixEpoch);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relationships, Has.Count.EqualTo(61));
            Assert.That(relationships[0].RelationshipKind, Is.EqualTo(Constants.LabelSubjectKind));
            Assert.That(relationships.Count(value => value.RelationshipKind == "performer"), Is.EqualTo(30));
            Assert.That(relationships.Count(value => value.RelationshipKind == "crew"), Is.EqualTo(20));
            Assert.That(relationships.Count(value => value.RelationshipKind == "writer"), Is.EqualTo(10));
            Assert.That(
                relationships.Select(value => value.RelationshipOrdinal),
                Is.EqualTo(Enumerable.Range(0, 61)));
        }
    }

    [Test]
    public void ArtistContribution_Result_WhenReferenceProjectionNormalizesAndRejectsRelations()
    {
        // Arrange
        const string json = """
        {
          "id": "0383dadf-2a4e-4d10-a46a-e9e041da8eb3",
          "name": "Queen",
          "relations": [
            { "type": "streaming music", "url": { "resource": "https://discarded.example/" } },
            { "type": "streaming music", "url": { "resource": "https://discarded.example/" } },
            { "type": "blog", "url": { "resource": "https://discarded.example/" } },
            { "type": "official homepage", "url": { "resource": "ftp://invalid.example/" } },
            { "type": "discogs", "url": { "resource": "https://discogs.example.com/master/1" } },
            { "type": "wikidata", "url": { "resource": "https://evil-wikidata.org/wiki/Q1" } },
            { "type": "official homepage", "url": { "resource": "https://www.discogs.com/master/1" } },
            { "type": "official homepage", "url": { "resource": "https://music.wikidata.org/wiki/Q1" } }
          ]
        }
        """;
        var artist = JsonSerializer.Deserialize<ArtistDto>(json)!;

        // Act
        var contribution = artist.ArtistContribution(DateTime.UtcNow);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                contribution.References.Select(reference => reference.Kind),
                Is.EqualTo(["streaming music", "blog", "discogs", "wikidata"]));
            Assert.That(
                contribution.References.Select(reference => reference.Url.AbsoluteUri),
                Is.EqualTo(
                [
                    "https://discarded.example/",
                    "https://discarded.example/",
                    "https://www.discogs.com/master/1",
                    "https://music.wikidata.org/wiki/Q1"
                ]));
        }
    }

    [Test]
    public void ToHit_Result_WhenAliasTagLabelAndUrlRelationProvenanceIsPresent()
    {
        // Arrange
        const string artistJson = """
        {
          "id": "0383dadf-2a4e-4d10-a46a-e9e041da8eb3",
          "name": "Queen",
          "aliases": [{ "name": "Queen UK", "type": "Artist name" }],
          "genres": [{ "name": "progressive rock", "count": 9 }],
          "tags": [{ "name": "rock", "count": 4 }],
          "relations": [
            { "type": "official homepage", "url": { "resource": "https://queenonline.com/" } }
          ]
        }
        """;
        const string releaseWithKnownEmptyLabelsJson = """
        {
          "id": "a0b1c2d3-e4f5-4678-9012-abcdefabcdef",
          "release-group": {
            "id": "550e8400-e29b-41d4-a716-446655440000",
            "title": "A Night at the Opera"
          },
          "label-info": []
        }
        """;
        const string releaseWithUnknownLabelsJson = """
        {
          "id": "a0b1c2d3-e4f5-4678-9012-abcdefabcdee",
          "release-group": {
            "id": "550e8400-e29b-41d4-a716-446655440001",
            "title": "News of the World"
          }
        }
        """;
        const string recordingJson = """
        {
          "id": "550e8400-e29b-41d4-a716-446655440002",
          "title": "Bohemian Rhapsody",
          "tags": [],
          "relations": [
            { "type": "wikidata", "url": { "resource": "https://www.wikidata.org/wiki/Q187745" } }
          ]
        }
        """;
        var artist = JsonSerializer.Deserialize<ArtistDto>(artistJson)!;
        var knownLabels = JsonSerializer.Deserialize<ReleaseDto>(releaseWithKnownEmptyLabelsJson)!;
        var unknownLabels = JsonSerializer.Deserialize<ReleaseDto>(releaseWithUnknownLabelsJson)!;
        var recording = JsonSerializer.Deserialize<RecordingDto>(recordingJson)!;

        // Act
        var artistHit = artist.ToHit()!;
        var knownLabelsHit = knownLabels.ToHit()!;
        var unknownLabelsHit = unknownLabels.ToHit()!;
        var recordingHit = recording.ToHit()!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(artistHit.Aliases!.Single().Type, Is.EqualTo("Artist name"));
            Assert.That(artistHit.Genres, Is.EqualTo(["rock"]));
            Assert.That(artistHit.UrlRelations!.Single().Type, Is.EqualTo("official homepage"));
            Assert.That(knownLabelsHit.LabelReferences, Is.Empty);
            Assert.That(unknownLabelsHit.LabelReferences, Is.Null);
            Assert.That(recordingHit.Genres, Is.Empty);
            Assert.That(recordingHit.UrlRelations!.Single().Type, Is.EqualTo("wikidata"));
        }
    }
}
