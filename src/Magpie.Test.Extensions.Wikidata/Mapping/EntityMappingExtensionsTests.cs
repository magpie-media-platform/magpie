using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Filtering;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata.Mapping;

[TestFixture]
internal sealed class EntityMappingExtensionsTests
{
    [TestCase("Q42", "Q42")]
    [TestCase(" q42 ", "Q42")]
    [TestCase("https://www.wikidata.org/entity/q42", "Q42")]
    [TestCase("http://www.wikidata.org/entity/Q42/", null)]
    [TestCase("Q0", null)]
    [TestCase("Q042", null)]
    [TestCase("Q\u0664\u0662", null)]
    [TestCase("P42", null)]
    [TestCase("Q42 UNION {}", null)]
    [TestCase("", null)]
    [TestCase(null, null)]
    public void ToQid_ReturnsCanonicalIdOrNull_WhenInputVaries(string? input, string? expected)
    {
        // Arrange
        var value = input;

        // Act
        var result = value.ToQid();

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("EN-us", "en")]
    [TestCase(" fr ", "fr")]
    [TestCase("e", null)]
    [TestCase("en123", null)]
    public void ToPrimaryLanguage_ReturnsPrimaryCodeOrNull_WhenInputVaries(string input, string? expected)
    {
        // Arrange
        var value = input;

        // Act
        var result = value.ToPrimaryLanguage();

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("{\"id\":42}", null)]
    [TestCase("{\"id\":\"q42\"}", "Q42")]
    [TestCase("{\"id\":\"Q042\"}", null)]
    public void EntityId_ReturnsCanonicalIdOrNull_WhenSnakIdVaries(string json, string? expected)
    {
        // Arrange
        var snak = new SnakDto
        {
            DataValue = new DataValueDto { Value = JsonSerializer.Deserialize<JsonElement>(json) }
        };

        // Act
        var result = snak.EntityId();

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void ToMetadataContribution_MapsTruthyStandardSchema_WhenMovie()
    {
        // Arrange
        var typeValue = JsonDocument.Parse("{\"id\":\"Q11424\"}").RootElement.Clone();
        var dateValue = JsonDocument.Parse(
            "{\"time\":\"+1999-03-31T00:00:00Z\",\"timezone\":0,\"before\":0,\"after\":0," +
            "\"precision\":11,\"calendarmodel\":\"http://www.wikidata.org/entity/Q1985727\"}")
            .RootElement.Clone();
        var durationValue = JsonDocument.Parse(
            "{\"amount\":\"+136\",\"unit\":\"http://www.wikidata.org/entity/Q7727\"}")
            .RootElement.Clone();
        var languageValue = JsonDocument.Parse("{\"id\":\"Q1860\"}").RootElement.Clone();
        var genreValue = JsonDocument.Parse("{\"id\":\"Q130232\"}").RootElement.Clone();
        var originalTitle = JsonDocument.Parse("{\"text\":\"The Matrix\",\"language\":\"en\"}")
            .RootElement.Clone();
        var entity = new EntityDto
        {
            Id = "Q83495",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["fr"] = new MonolingualTextDto { Value = "Matrix", Language = "fr" }
            },
            Descriptions = new Dictionary<string, MonolingualTextDto>
            {
                ["fr"] = new MonolingualTextDto { Value = "film de science-fiction", Language = "fr" }
            },
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P31"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto { Value = typeValue }
                        }
                    }
                ],
                ["P577"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto { DataValue = new DataValueDto { Value = dateValue } }
                    }
                ],
                ["P2047"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto { DataValue = new DataValueDto { Value = durationValue } }
                    }
                ],
                ["P364"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto { DataValue = new DataValueDto { Value = languageValue } }
                    }
                ],
                ["P136"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto { DataValue = new DataValueDto { Value = genreValue } }
                    }
                ],
                ["P1476"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto { DataValue = new DataValueDto { Value = originalTitle } }
                    }
                ],
                ["P3383"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("\"Poster file.jpg\"").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P18"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("\"Still.jpg\"").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P345"] =
                [
                    new StatementDto
                    {
                        Rank = "preferred",
                        Mainsnak = new SnakDto
                        {
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("\"tt0133093\"").RootElement.Clone()
                            }
                        }
                    },
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("\"tt-old\"").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P4947"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("\"603\"").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };
        var support = new Dictionary<string, EntityDto>
        {
            ["Q1860"] = new EntityDto
            {
                Id = "Q1860",
                Claims = new Dictionary<string, List<StatementDto>>
                {
                    ["P218"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                DataValue = new DataValueDto
                                {
                                    Value = JsonDocument.Parse("\"en\"").RootElement.Clone()
                                }
                            }
                        }
                    ]
                }
            },
            ["Q130232"] = new EntityDto
            {
                Id = "Q130232",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["fr"] = new MonolingualTextDto { Value = "drame", Language = "fr" }
                }
            }
        };
        var now = new DateTime(2026, 7, 13, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Movie,
            "fr",
            support,
            now);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Title, Is.EqualTo("Matrix"));
            Assert.That(result.OriginalTitle, Is.EqualTo("The Matrix"));
            Assert.That(result.Language, Is.EqualTo("en"));
            Assert.That(result.PublishDate, Is.EqualTo(new DateTime(1999, 3, 31, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(result.PublishDatePrecision, Is.EqualTo(TemporalPrecision.Day));
            Assert.That(result.Duration, Is.EqualTo(TimeSpan.FromMinutes(136)));
            Assert.That(result.Genres.Single().Name, Is.EqualTo("drame"));
            Assert.That(
                result.ExternalIds.Select(external => external.Value),
                Is.EquivalentTo(["Q83495", "tt0133093", "603"]));
            Assert.That(result.Images.Select(image => image.Type), Is.EqualTo(["poster", "backdrop"]));
            Assert.That(result.Images.First().Url.AbsoluteUri, Does.Contain("Poster%20file.jpg"));
            Assert.That(result.DateLastRefreshed, Is.EqualTo(now));
        }
    }

    [TestCase(EntityKind.Movie, Constants.MovieQid, null, null)]
    [TestCase(EntityKind.Show, Constants.TelevisionSeriesQid, null, null)]
    [TestCase(EntityKind.Season, Constants.SeasonQid, "Q2", null)]
    public void ToMetadataContribution_MapsSharedArtwork_WhenKindCarriesContentArtwork(
        EntityKind kind, string? typeQid, string? seriesQid, string? seasonQid)
    {
        // Arrange
        var claims = new Dictionary<string, List<StatementDto>>
        {
            [Constants.InstanceOfPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = typeQid })
                        }
                    }
                }
            ],
            [Constants.PartOfSeriesPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = seriesQid })
                        }
                    }
                }
            ],
            [Constants.SeasonPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = seasonQid })
                        }
                    }
                }
            ]
        };
        var entity = new EntityDto { Id = "Q1", Claims = claims };
        entity.Claims![Constants.PosterPropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto { Value = JsonSerializer.SerializeToElement("Poster.jpg") }
                }
            }
        ];
        entity.Claims[Constants.ImagePropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto { Value = JsonSerializer.SerializeToElement("Backdrop.jpg") }
                }
            }
        ];
        entity.Claims[Constants.LogoPropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto { Value = JsonSerializer.SerializeToElement("Logo.svg") }
                }
            }
        ];

        // Act
        var result = entity.ToMetadataContribution(
            kind,
            "en",
            new Dictionary<string, EntityDto>(),
            DateTime.UnixEpoch);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result!.Images.Select(image => image.Type),
                Is.EqualTo(["poster", "backdrop", "logo"]));
            Assert.That(
                result.Images.Select(image => image.IsPrimary),
                Is.EqualTo([true, false, false]));
            Assert.That(result.Images.Select(image => image.Ordinal), Is.EqualTo([0, 1, 2]));
        }
    }

    [TestCase(EntityKind.Show, Constants.TelevisionSeriesQid, null, null)]
    [TestCase(EntityKind.Season, Constants.SeasonQid, "Q2", null)]
    public void ToMetadataContribution_MapsP18AsPoster_WhenContentArtworkHasNoP3383(
        EntityKind kind, string? typeQid, string? seriesQid, string? seasonQid)
    {
        // Arrange
        var claims = new Dictionary<string, List<StatementDto>>
        {
            [Constants.InstanceOfPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = typeQid })
                        }
                    }
                }
            ],
            [Constants.PartOfSeriesPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = seriesQid })
                        }
                    }
                }
            ],
            [Constants.SeasonPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = seasonQid })
                        }
                    }
                }
            ]
        };
        var entity = new EntityDto { Id = "Q1", Claims = claims };
        entity.Claims![Constants.ImagePropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto { Value = JsonSerializer.SerializeToElement("Artwork.jpg") }
                }
            }
        ];

        // Act
        var result = entity.ToMetadataContribution(
            kind,
            "en",
            new Dictionary<string, EntityDto>(),
            DateTime.UnixEpoch);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Images.Single().Type, Is.EqualTo("poster"));
            Assert.That(result.Images.Single().IsPrimary, Is.True);
        }
    }

    [Test]
    public void ContentArtwork_ReturnsStableDeduplicatedOrder_WhenFilenamesOverlap()
    {
        // Arrange
        const string sharedFilename = "Shared.jpg";

        // Act
        var result = sharedFilename.ToContentArtwork(sharedFilename, "Logo.svg");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Select(image => image.Type), Is.EqualTo(["poster", "logo"]));
            Assert.That(result.Select(image => image.IsPrimary), Is.EqualTo([true, false]));
            Assert.That(result.Select(image => image.Ordinal), Is.EqualTo([0, 1]));
        }
    }

    [Test]
    public void ContentArtwork_UsesImageAsPoster_WhenPosterFilenameIsInvalid()
    {
        // Arrange
        const string invalidPosterFilename = "invalid\u0000poster";

        // Act
        var result = invalidPosterFilename.ToContentArtwork("Fallback.jpg", null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result.Single().Type, Is.EqualTo("poster"));
            Assert.That(result.Single().IsPrimary, Is.True);
        }
    }

    [TestCase(EntityKind.Movie, Constants.MovieQid, null, null)]
    [TestCase(EntityKind.Show, Constants.TelevisionSeriesQid, null, null)]
    [TestCase(EntityKind.Season, Constants.SeasonQid, "Q2", null)]
    [TestCase(EntityKind.Episode, Constants.EpisodeQid, "Q2", "Q3")]
    public void ToMetadataContribution_MapsRankedDeduplicatedTrailers_WhenKindCarriesPreviews(
        EntityKind kind, string? typeQid, string? seriesQid, string? seasonQid)
    {
        // Arrange
        var claims = new Dictionary<string, List<StatementDto>>
        {
            [Constants.InstanceOfPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = typeQid })
                        }
                    }
                }
            ],
            [Constants.PartOfSeriesPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = seriesQid })
                        }
                    }
                }
            ],
            [Constants.SeasonPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = seasonQid })
                        }
                    }
                }
            ]
        };
        var entity = new EntityDto { Id = "Q1", Claims = claims };
        entity.Claims![Constants.TrailerPropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement("ignored-normal")
                    }
                }
            },
            new StatementDto
            {
                Rank = "preferred",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement("first / trailer")
                    }
                }
            },
            new StatementDto
            {
                Rank = "preferred",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement("first / trailer")
                    }
                }
            },
            new StatementDto
            {
                Rank = "preferred",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement("second?trailer")
                    }
                }
            },
            new StatementDto
            {
                Rank = "deprecated",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement("ignored-deprecated")
                    }
                }
            }
        ];

        // Act
        var result = entity.ToMetadataContribution(
            kind,
            "en",
            new Dictionary<string, EntityDto>(),
            DateTime.UnixEpoch);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Previews, Has.Count.EqualTo(2));
            Assert.That(result.Previews.Select(preview => preview.Kind), Is.All.EqualTo("trailer"));
            Assert.That(result.Previews.Select(preview => preview.EmbedKind), Is.All.EqualTo("youtube"));
            Assert.That(
                result.Previews.Select(preview => preview.Source),
                Is.All.EqualTo("wikidata"));
            Assert.That(result.Previews.Select(preview => preview.Ordinal), Is.EqualTo([0, 1]));
            Assert.That(
                result.Previews.Select(preview => preview.Url.OriginalString),
                Is.EqualTo(
                [
                    "https://www.youtube.com/watch?v=first%20%2F%20trailer",
                    "https://www.youtube.com/watch?v=second%3Ftrailer"
                ]));
        }
    }

    [TestCase(EntityKind.Person, Constants.HumanQid, null, null)]
    [TestCase(EntityKind.Studio, null, null, null)]
    public void ToMetadataContribution_LeavesPreviewsEmpty_WhenKindDoesNotCarryTrailers(
        EntityKind kind, string? typeQid, string? seriesQid, string? seasonQid)
    {
        // Arrange
        var claims = new Dictionary<string, List<StatementDto>>
        {
            [Constants.InstanceOfPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = typeQid })
                        }
                    }
                }
            ],
            [Constants.PartOfSeriesPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = seriesQid })
                        }
                    }
                }
            ],
            [Constants.SeasonPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = seasonQid })
                        }
                    }
                }
            ]
        };
        var entity = new EntityDto { Id = "Q1", Claims = claims };
        entity.Claims![Constants.TrailerPropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto { Value = JsonSerializer.SerializeToElement("video") }
                }
            }
        ];

        // Act
        var result = entity.ToMetadataContribution(
            kind,
            "en",
            new Dictionary<string, EntityDto>(),
            DateTime.UnixEpoch);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Previews, Is.Empty);
        }
    }

    [TestCase(EntityKind.Movie, Constants.MovieQid, null, null)]
    [TestCase(EntityKind.Show, Constants.TelevisionSeriesQid, null, null)]
    [TestCase(EntityKind.Season, Constants.SeasonQid, "Q2", null)]
    [TestCase(EntityKind.Episode, Constants.EpisodeQid, "Q2", "Q3")]
    [TestCase(EntityKind.Person, Constants.HumanQid, null, null)]
    [TestCase(EntityKind.Studio, null, null, null)]
    public void ToMetadataContribution_LeavesSortTitleUnset_WhenWikidataHasNoSource(
        EntityKind kind, string? typeQid, string? seriesQid, string? seasonQid)
    {
        // Arrange
        var claims = new Dictionary<string, List<StatementDto>>
        {
            [Constants.InstanceOfPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = typeQid })
                        }
                    }
                }
            ],
            [Constants.PartOfSeriesPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = seriesQid })
                        }
                    }
                }
            ],
            [Constants.SeasonPropertyId] =
            [
                new StatementDto
                {
                    Rank = "normal",
                    Mainsnak = new SnakDto
                    {
                        SnakType = "value",
                        DataValue = new DataValueDto
                        {
                            Value = JsonSerializer.SerializeToElement(new { id = seasonQid })
                        }
                    }
                }
            ]
        };
        var entity = new EntityDto { Id = "Q1", Claims = claims };

        // Act
        var result = entity.ToMetadataContribution(
            kind,
            "en",
            new Dictionary<string, EntityDto>(),
            DateTime.UnixEpoch);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.SortTitle, Is.Null);
        }
    }

    [Test]
    public void ToContentRatings_ReturnsSelectedSourceQid_WhenClaimsAreMapped()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q1",
            Claims = new Dictionary<string, List<StatementDto>>
            {
                [Constants.MpaaRatingPropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                            }
                        }
                    },
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new { id = "Q11" })
                            }
                        }
                    }
                ]
            }
        };
        var support = new Dictionary<string, EntityDto>
        {
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "PG" }
                }
            },
            ["Q11"] = new EntityDto
            {
                Id = "Q11",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "R" }
                }
            }
        };

        // Act
        var result = entity.ToContentRatings(support, "en");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result.Single().EntityQid, Is.EqualTo("Q10"));
            Assert.That(result.Single().Value.Rating, Is.EqualTo("PG"));
        }
    }

    [Test]
    public void ToContentRatings_ReturnsFirstPreferredValuePerSystem_WhenVariantsExist()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q1",
            Claims = new Dictionary<string, List<StatementDto>>
            {
                [Constants.InstanceOfPropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new { id = Constants.MovieQid })
                            }
                        }
                    }
                ]
            }
        };
        entity.Claims![Constants.MpaaRatingPropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto { Value = JsonSerializer.SerializeToElement(new { id = "Q10" }) }
                }
            },
            new StatementDto
            {
                Rank = "preferred",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto { Value = JsonSerializer.SerializeToElement(new { id = "Q11" }) }
                }
            },
            new StatementDto
            {
                Rank = "preferred",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto { Value = JsonSerializer.SerializeToElement(new { id = "Q12" }) }
                }
            }
        ];
        entity.Claims[Constants.BbfcRatingPropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto { Value = JsonSerializer.SerializeToElement(new { id = "Q20" }) }
                }
            },
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto { Value = JsonSerializer.SerializeToElement(new { id = "Q21" }) }
                }
            }
        ];
        var support = new Dictionary<string, EntityDto>
        {
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "PG" }
                }
            },
            ["Q11"] = new EntityDto
            {
                Id = "Q11",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "R" }
                }
            },
            ["Q12"] = new EntityDto
            {
                Id = "Q12",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "NC-17" }
                }
            },
            ["Q20"] = new EntityDto
            {
                Id = "Q20",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "15" }
                }
            },
            ["Q21"] = new EntityDto
            {
                Id = "Q21",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "18" }
                }
            }
        };

        // Act
        var result = entity.ToContentRatings(support, "en");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Select(value => value.EntityQid), Is.EqualTo(["Q11", "Q20"]));
            Assert.That(result.Select(value => value.Value.Body), Is.EqualTo(["MPAA", "BBFC"]));
            Assert.That(result.Select(value => value.Value.Rating), Is.EqualTo(["R", "15"]));
            Assert.That(result.Select(value => value.Value.Ordinal), Is.EqualTo([0, 1]));
        }
    }

    [Test]
    public void ToAwards_ReturnsSourceQid_WhenClaimsAreMapped()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q1",
            Claims = new Dictionary<string, List<StatementDto>>
            {
                [Constants.AwardReceivedPropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new { id = "Q10" })
                            }
                        }
                    }
                ]
            }
        };
        var support = new Dictionary<string, EntityDto>
        {
            ["Q10"] = new EntityDto
            {
                Id = "Q10",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Language = "en", Value = "Best Picture" }
                }
            }
        };

        // Act
        var result = entity.ToAwards(support, "en");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result.Single().EntityQid, Is.EqualTo("Q10"));
            Assert.That(result.Single().Value.Name, Is.EqualTo("Best Picture"));
        }
    }

    [Test]
    public void ToMetadataContribution_MapsPeriodStart_WhenPublicationDateHasYearPrecision()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q1",
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P31"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q11424\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P577"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse(
                                    "{\"time\":\"+1999-00-00T00:00:00Z\",\"timezone\":0,\"before\":0," +
                                    "\"after\":0,\"precision\":9," +
                                    "\"calendarmodel\":\"http://www.wikidata.org/entity/Q1985727\"}")
                                    .RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Movie,
            "en",
            new Dictionary<string, EntityDto>(),
            DateTime.UtcNow);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result?.PublishDate, Is.EqualTo(new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(result?.PublishDatePrecision, Is.EqualTo(TemporalPrecision.Year));
        }
    }

    [TestCase("+2", "Q11574", 2d)]
    [TestCase("+2", "Q7727", 120d)]
    [TestCase("+2", "Q25235", 7200d)]
    [TestCase("+2", "Q1", null)]
    [TestCase("-2", "Q7727", null)]
    [TestCase("+79228162514264337593543950335", "Q25235", null)]
    public void ToMetadataContribution_MapOnlyPositiveNonOverflowingCommonValues_WhenDurationUnits(
        string amount,
        string unitQid,
        double? expectedSeconds)
    {
        // Arrange
        var typeValue = JsonDocument.Parse("{\"id\":\"Q11424\"}").RootElement.Clone();
        var durationValue = JsonDocument.Parse(
            "{\"amount\":\"" +
            amount +
            "\",\"unit\":\"http://www.wikidata.org/entity/" +
            unitQid +
            "\"}").RootElement.Clone();
        var entity = new EntityDto
        {
            Id = "Q1",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "Movie" }
            },
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P31"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto { Value = typeValue }
                        }
                    }
                ],
                ["P2047"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto { Value = durationValue }
                        }
                    }
                ]
            }
        };

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Movie,
            "en",
            new Dictionary<string, EntityDto>(),
            DateTime.UtcNow);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Duration?.TotalSeconds, Is.EqualTo(expectedSeconds));
    }

    [Test]
    public void ToMetadataContribution_UsesFirstDuration_WhenEqualRankStatementsDiffer()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q1",
            Claims = new Dictionary<string, List<StatementDto>>
            {
                [Constants.InstanceOfPropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new { id = Constants.MovieQid })
                            }
                        }
                    }
                ]
            }
        };
        entity.Claims![Constants.DurationPropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement(new
                            {
                                amount = "+180",
                                unit = "http://www.wikidata.org/entity/" + Constants.MinuteUnitQid
                            })
                    }
                }
            },
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement(new
                            {
                                amount = "+90",
                                unit = "http://www.wikidata.org/entity/" + Constants.MinuteUnitQid
                            })
                    }
                }
            }
        ];

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Movie,
            "en",
            new Dictionary<string, EntityDto>(),
            DateTime.UnixEpoch);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Duration, Is.EqualTo(TimeSpan.FromMinutes(180)));
        }
    }

    [Test]
    public void ToMetadataContribution_UsesFirstValidPreferredDuration_WhenPreferredRankExists()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q1",
            Claims = new Dictionary<string, List<StatementDto>>
            {
                [Constants.InstanceOfPropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new { id = Constants.MovieQid })
                            }
                        }
                    }
                ]
            }
        };
        entity.Claims![Constants.DurationPropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement(new
                            {
                                amount = "+120",
                                unit = "http://www.wikidata.org/entity/" + Constants.MinuteUnitQid
                            })
                    }
                }
            },
            new StatementDto
            {
                Rank = "preferred",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement(new
                            {
                                amount = "+1",
                                unit = "http://www.wikidata.org/entity/Q1"
                            })
                    }
                }
            },
            new StatementDto
            {
                Rank = "preferred",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement(new
                            {
                                amount = "+150",
                                unit = "http://www.wikidata.org/entity/" + Constants.MinuteUnitQid
                            })
                    }
                }
            },
            new StatementDto
            {
                Rank = "preferred",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement(new
                            {
                                amount = "+160",
                                unit = "http://www.wikidata.org/entity/" + Constants.MinuteUnitQid
                            })
                    }
                }
            }
        ];

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Movie,
            "en",
            new Dictionary<string, EntityDto>(),
            DateTime.UnixEpoch);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Duration, Is.EqualTo(TimeSpan.FromMinutes(150)));
        }
    }

    [Test]
    public void ToMetadataContribution_LeavesDurationUnset_WhenPreferredRankHasNoValidValue()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q1",
            Claims = new Dictionary<string, List<StatementDto>>
            {
                [Constants.InstanceOfPropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new { id = Constants.MovieQid })
                            }
                        }
                    }
                ]
            }
        };
        entity.Claims![Constants.DurationPropertyId] =
        [
            new StatementDto
            {
                Rank = "normal",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement(new
                            {
                                amount = "+120",
                                unit = "http://www.wikidata.org/entity/" + Constants.MinuteUnitQid
                            })
                    }
                }
            },
            new StatementDto
            {
                Rank = "preferred",
                Mainsnak = new SnakDto
                {
                    SnakType = "value",
                    DataValue = new DataValueDto
                    {
                        Value = JsonSerializer.SerializeToElement(new
                            {
                                amount = "+1",
                                unit = "http://www.wikidata.org/entity/Q1"
                            })
                    }
                }
            }
        ];

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Movie,
            "en",
            new Dictionary<string, EntityDto>(),
            DateTime.UnixEpoch);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Duration, Is.Null);
        }
    }

    [TestCase("+1999-00-00T00:00:00Z", 9, 1)]
    [TestCase("+1999-03-00T00:00:00Z", 10, 3)]
    public void ToMetadataContribution_MapsDissolvedPeriodStart_WhenShowEndHasCoarsePrecision(
        string time,
        int precision,
        int expectedMonth)
    {
        // Arrange
        var typeValue = JsonDocument.Parse("{\"id\":\"Q5398426\"}").RootElement.Clone();
        var endValue = JsonDocument.Parse(
            "{\"time\":\"" +
            time +
            "\",\"timezone\":0,\"before\":0,\"after\":0," +
            "\"precision\":" +
            precision +
            "," +
            "\"calendarmodel\":\"http://www.wikidata.org/entity/Q1985727\"}").RootElement.Clone();
        var entity = new EntityDto
        {
            Id = "Q1",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "A Show" }
            },
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P31"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            DataValue = new DataValueDto { Value = typeValue }
                        }
                    }
                ],
                ["P582"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            DataValue = new DataValueDto { Value = endValue }
                        }
                    }
                ]
            }
        };

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Show,
            "en",
            new Dictionary<string, EntityDto>(),
            DateTime.UtcNow);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result?.Dissolved,
                Is.EqualTo(new DateTime(1999, expectedMonth, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(result?.ProductionStatus, Is.EqualTo("Ended"));
        }
    }

    [Test]
    public void ToMetadataContribution_RequiresStandardHierarchyAndMapsQualifierOrdinal_WhenEpisode()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q300",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Value = "Pilot", Language = "en" }
            },
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P31"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q21191270\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P179"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q200\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P4908"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q100\"}").RootElement.Clone()
                            }
                        },
                        Qualifiers = new Dictionary<string, List<SnakDto>>
                        {
                            ["P1545"] =
                            [
                                new SnakDto
                                {
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonDocument.Parse("\"1\"").RootElement.Clone()
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Episode,
            "en",
            new Dictionary<string, EntityDto>(),
            DateTime.UtcNow,
            "Q100");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.SequenceNumber, Is.EqualTo(1));
        }
    }

    [Test]
    public void ToChildDefinitions_EmitsAlignedCoordinatesAndPeriodStart_WhenRowsAreStandard()
    {
        // Arrange
        var rows = new[]
        {
            new HierarchyRow
            {
                EntityKind = EntityKind.Episode,
                Qid = "Q300",
                Ordinal = 2,
                Title = "Second",
                PublishDate = new TemporalValue
                {
                    Value = new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Precision = TemporalPrecision.Year
                },
                ExternalIds = new Dictionary<string, string> { ["wikidata"] = "Q300" }
            }
        };

        // Act
        var result = rows.ToChildDefinitions(1, DateTime.UtcNow);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Single().Slug, Is.EqualTo("s01e02"));
            Assert.That(result.Single().AlignmentKey, Is.EqualTo("episode:1:2"));
            Assert.That(result.Single().InitialContribution?.SequenceNumber, Is.EqualTo(2));
            Assert.That(
                result.Single().InitialContribution?.PublishDate,
                Is.EqualTo(new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        }
    }

    [Test]
    public void ToMetadataContribution_MapsAwardWithConferredBodyAndQualifierYear_WhenMovie()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q1",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Value = "A Movie", Language = "en" }
            },
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P31"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q11424\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P166"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q500\"}").RootElement.Clone()
                            }
                        },
                        Qualifiers = new Dictionary<string, List<SnakDto>>
                        {
                            ["P585"] =
                            [
                                new SnakDto
                                {
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonDocument.Parse(
                                            "{\"time\":\"+2000-03-26T00:00:00Z\",\"timezone\":0,\"before\":0," +
                                            "\"after\":0,\"precision\":11," +
                                            "\"calendarmodel\":\"http://www.wikidata.org/entity/Q1985727\"}")
                                            .RootElement.Clone()
                                    }
                                }
                            ]
                        }
                    },
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q501\"}").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };
        var support = new Dictionary<string, EntityDto>
        {
            ["Q500"] = new EntityDto
            {
                Id = "Q500",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Value = "Academy Award for Best Picture", Language = "en" }
                },
                Claims = new Dictionary<string, List<StatementDto>>
                {
                    ["P1027"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonDocument.Parse("{\"id\":\"Q600\"}").RootElement.Clone()
                                }
                            }
                        }
                    ]
                }
            },
            ["Q600"] = new EntityDto
            {
                Id = "Q600",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto
                    {
                        Value = "Academy of Motion Picture Arts and Sciences",
                        Language = "en"
                    }
                }
            }
        };

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Movie,
            "en",
            support,
            DateTime.UtcNow);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Awards, Has.Count.EqualTo(1));
            Assert.That(result.Awards.Single().Name, Is.EqualTo("Academy Award for Best Picture"));
            Assert.That(result.Awards.Single().Body, Is.EqualTo("Academy of Motion Picture Arts and Sciences"));
            Assert.That(result.Awards.Single().Year, Is.EqualTo(2000));
            Assert.That(result.Awards.Single().Status, Is.EqualTo("Won"));
            Assert.That(result.Awards.Single().Ordinal, Is.EqualTo(0));
        }
    }

    [Test]
    public void ToMetadataContribution_MapsContentRatingsOnlyForResolvableSystems_WhenMovie()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q1",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Value = "A Movie", Language = "en" }
            },
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P31"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q11424\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P1657"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q700\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P2629"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q701\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P1981"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q702\"}").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };
        var support = new Dictionary<string, EntityDto>
        {
            ["Q700"] = new EntityDto
            {
                Id = "Q700",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Value = "R", Language = "en" }
                }
            },
            ["Q701"] = new EntityDto
            {
                Id = "Q701",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Value = "15", Language = "en" }
                }
            }
        };

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Movie,
            "en",
            support,
            DateTime.UtcNow);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.ContentRatings, Has.Count.EqualTo(2));
            Assert.That(result.ContentRatings.Select(rating => rating.Body), Is.EqualTo(["MPAA", "BBFC"]));
            Assert.That(result.ContentRatings.Select(rating => rating.Rating), Is.EqualTo(["R", "15"]));
            Assert.That(result.ContentRatings.Select(rating => rating.Ordinal), Is.EqualTo([0, 1]));
        }
    }

    [Test]
    public void ToMetadataContribution_MapsAwardBodyFallbackAndFskRating_WhenShow()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q2",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Value = "A Show", Language = "en" }
            },
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P31"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q5398426\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P166"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q510\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P1981"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q703\"}").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };
        var support = new Dictionary<string, EntityDto>
        {
            ["Q510"] = new EntityDto
            {
                Id = "Q510",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Value = "Peabody Award", Language = "en" }
                }
            },
            ["Q703"] = new EntityDto
            {
                Id = "Q703",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Value = "12", Language = "en" }
                }
            }
        };

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Show,
            "en",
            support,
            DateTime.UtcNow);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Awards.Single().Name, Is.EqualTo("Peabody Award"));
            Assert.That(result.Awards.Single().Body, Is.EqualTo("Peabody Award"));
            Assert.That(result.Awards.Single().Year, Is.Null);
            Assert.That(result.ContentRatings.Single().Body, Is.EqualTo("FSK"));
            Assert.That(result.ContentRatings.Single().Rating, Is.EqualTo("12"));
        }
    }

    [Test]
    public void ToMetadataContribution_MapsAwards_WhenEpisode()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q300",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Value = "Pilot", Language = "en" }
            },
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P31"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q21191270\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P179"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q200\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P4908"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q100\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P166"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q520\"}").RootElement.Clone()
                            }
                        },
                        Qualifiers = new Dictionary<string, List<SnakDto>>
                        {
                            ["P585"] =
                            [
                                new SnakDto
                                {
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonDocument.Parse(
                                            "{\"time\":\"+2013-09-22T00:00:00Z\",\"timezone\":0,\"before\":0," +
                                            "\"after\":0,\"precision\":11," +
                                            "\"calendarmodel\":\"http://www.wikidata.org/entity/Q1985727\"}")
                                            .RootElement.Clone()
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };
        var support = new Dictionary<string, EntityDto>
        {
            ["Q520"] = new EntityDto
            {
                Id = "Q520",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto
                    {
                        Value = "Primetime Emmy Award for Outstanding Writing for a Drama Series",
                        Language = "en"
                    }
                }
            }
        };

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Episode,
            "en",
            support,
            DateTime.UtcNow,
            "Q100");

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result!.Awards.Single().Name,
                Is.EqualTo("Primetime Emmy Award for Outstanding Writing for a Drama Series"));
            Assert.That(result.Awards.Single().Year, Is.EqualTo(2013));
            Assert.That(result.Awards.Single().Status, Is.EqualTo("Won"));
        }
    }

    [Test]
    public void ToMetadataContribution_PrefersConferredByQualifierForAwardBody_WhenPerson()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q77",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Value = "Jane Doe", Language = "en" }
            },
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P31"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q5\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P166"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q530\"}").RootElement.Clone()
                            }
                        },
                        Qualifiers = new Dictionary<string, List<SnakDto>>
                        {
                            ["P1027"] =
                            [
                                new SnakDto
                                {
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonDocument.Parse("{\"id\":\"Q640\"}").RootElement.Clone()
                                    }
                                }
                            ],
                            ["P585"] =
                            [
                                new SnakDto
                                {
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonDocument.Parse(
                                            "{\"time\":\"+2001-00-00T00:00:00Z\",\"timezone\":0,\"before\":0," +
                                            "\"after\":0,\"precision\":9," +
                                            "\"calendarmodel\":\"http://www.wikidata.org/entity/Q1985727\"}")
                                            .RootElement.Clone()
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };
        var support = new Dictionary<string, EntityDto>
        {
            ["Q530"] = new EntityDto
            {
                Id = "Q530",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Value = "honorary doctorate", Language = "en" }
                },
                Claims = new Dictionary<string, List<StatementDto>>
                {
                    ["P1027"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonDocument.Parse("{\"id\":\"Q641\"}").RootElement.Clone()
                                }
                            }
                        }
                    ]
                }
            },
            ["Q640"] = new EntityDto
            {
                Id = "Q640",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Value = "University of Testing", Language = "en" }
                }
            },
            ["Q641"] = new EntityDto
            {
                Id = "Q641",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Value = "Item Conferrer", Language = "en" }
                }
            }
        };

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Person,
            "en",
            support,
            DateTime.UtcNow);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Awards.Single().Name, Is.EqualTo("honorary doctorate"));
            Assert.That(result.Awards.Single().Body, Is.EqualTo("University of Testing"));
            Assert.That(result.Awards.Single().Year, Is.EqualTo(2001));
        }
    }

    [Test]
    public void ToMetadataContribution_MapsTheExternalIdsOfMagpieSources_WhenPerson()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q616886",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Value = "Ken Burns", Language = "en" }
            },
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P31"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q5\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P345"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("\"nm0122741\"").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P4985"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("\"204484\"").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P11449"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto { Value = JsonDocument.Parse("\"64989\"").RootElement.Clone() }
                        }
                    }
                ],
                ["P434"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("\"9e5e41c7-4ec2-4e18-b210-0241c666b462\"")
                                    .RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P244"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("\"n91020856\"").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Person,
            "en",
            new Dictionary<string, EntityDto>(),
            DateTime.UtcNow);

        // Assert
        Assert.That(
            result!.ExternalIds.Select(external => (external.Source, external.Value)),
            Is.EqualTo(new (string?, string)[]
            {
                ("wikidata", "Q616886"),
                ("tmdb", "204484"),
                ("imdb", "nm0122741"),
                ("tvmaze", "64989"),
                ("musicbrainz", "9e5e41c7-4ec2-4e18-b210-0241c666b462")
            }));
    }

    [Test]
    public void ToMetadataContribution_MapsAwards_WhenStudio()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q88",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Value = "A Studio", Language = "en" }
            },
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P166"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q540\"}").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };
        var support = new Dictionary<string, EntityDto>
        {
            ["Q540"] = new EntityDto
            {
                Id = "Q540",
                Labels = new Dictionary<string, MonolingualTextDto>
                {
                    ["en"] = new MonolingualTextDto { Value = "Academy Honorary Award", Language = "en" }
                }
            }
        };

        // Act
        var result = entity.ToMetadataContribution(
            EntityKind.Studio,
            "en",
            support,
            DateTime.UtcNow);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Awards.Single().Name, Is.EqualTo("Academy Honorary Award"));
            Assert.That(result.Awards.Single().Body, Is.EqualTo("Academy Honorary Award"));
            Assert.That(result.Awards.Single().Status, Is.EqualTo("Won"));
        }
    }

    [TestCase(EntityKind.Movie, Constants.MovieQid)]
    [TestCase(EntityKind.Show, Constants.TelevisionSeriesQid)]
    public void ToMetadataContribution_MapsAllValidTruthyOfficialWebsites_WhenSearchableKind(
        EntityKind kind,
        string typeQid)
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q123",
            Claims = new Dictionary<string, List<StatementDto>>
            {
                [Constants.InstanceOfPropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement(new { id = typeQid })
                            }
                        }
                    }
                ],
                [Constants.OfficialWebsitePropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement("https://Example.com/Official")
                            }
                        }
                    },
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement("https://Example.com/Official")
                            }
                        }
                    },
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement("http://example.com/archive")
                            }
                        }
                    },
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement("ftp://example.com/ignored")
                            }
                        }
                    },
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement("relative/ignored")
                            }
                        }
                    },
                    new StatementDto
                    {
                        Rank = "deprecated",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonSerializer.SerializeToElement("https://example.com/deprecated")
                            }
                        }
                    }
                ]
            }
        };

        // Act
        var result = entity.ToMetadataContribution(
            kind,
            "en",
            new Dictionary<string, EntityDto>(),
            DateTime.UtcNow);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result!.References.Select(reference => reference.Url.AbsoluteUri),
                Is.EqualTo(["https://example.com/Official", "http://example.com/archive"]));
            Assert.That(result.References.Select(reference => reference.Ordinal), Is.EqualTo([0, 1]));
            Assert.That(result.References.All(reference => reference.Kind == "official"), Is.True);
            Assert.That(result.References.All(reference => reference.Source == "wikidata"), Is.True);
        }
    }

    [Test]
    public void RequiredSupportQids_ReturnsAwardAndRatingQids_WhenSupportEntitiesAreRequired()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q1",
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P364"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q1860\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P136"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q130232\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
                ["P166"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q500\"}").RootElement.Clone()
                            }
                        },
                        Qualifiers = new Dictionary<string, List<SnakDto>>
                        {
                            ["P1027"] =
                            [
                                new SnakDto
                                {
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonDocument.Parse("{\"id\":\"Q640\"}").RootElement.Clone()
                                    }
                                }
                            ]
                        }
                    }
                ],
                ["P1657"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q700\"}").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };

        // Act
        var result = entity.RequiredSupportQids();

        // Assert
        Assert.That(result, Is.EquivalentTo(["Q1860", "Q130232", "Q500", "Q640", "Q700"]));
    }

    [Test]
    public void RequiredAwardBodyQids_ReturnsUnfetchedConferrerQids_WhenSomeEntitiesAreCached()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q1",
            Claims = new Dictionary<string, List<StatementDto>>
            {
                ["P166"] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q500\"}").RootElement.Clone()
                            }
                        }
                    },
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q501\"}").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };
        var support = new Dictionary<string, EntityDto>
        {
            ["Q500"] = new EntityDto
            {
                Id = "Q500",
                Claims = new Dictionary<string, List<StatementDto>>
                {
                    ["P1027"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonDocument.Parse("{\"id\":\"Q600\"}").RootElement.Clone()
                                }
                            }
                        }
                    ]
                }
            },
            ["Q501"] = new EntityDto
            {
                Id = "Q501",
                Claims = new Dictionary<string, List<StatementDto>>
                {
                    ["P1027"] =
                    [
                        new StatementDto
                        {
                            Rank = "normal",
                            Mainsnak = new SnakDto
                            {
                                SnakType = "value",
                                DataValue = new DataValueDto
                                {
                                    Value = JsonDocument.Parse("{\"id\":\"Q640\"}").RootElement.Clone()
                                }
                            }
                        }
                    ]
                }
            },
            ["Q640"] = new EntityDto { Id = "Q640" }
        };

        // Act
        var result = entity.RequiredAwardBodyQids(support);

        // Assert
        Assert.That(result, Is.EqualTo(["Q600"]));
    }
}
