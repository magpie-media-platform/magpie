using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class ContentMetadataProviderRelationshipTests
{
    [TestCase("game", "movie")]
    [TestCase("tv", "movie")]
    public async Task GetRelationshipsAsync_ReturnsEmpty_WhenContentTypeAndLevelAreUnsupported(
        string contentType,
        string level)
    {
        // Arrange
        var entityResolver = new Mock<IEntityResolver>();
        var cache = new Mock<IQueryCoordinator>();
        var subject = new ContentMetadataProvider(
            null!,
            entityResolver.Object,
            cache.Object,
            Mock.Of<IQueryExecutor>(),
            TimeProvider.System,
            NullLogger<ContentMetadataProvider>.Instance);
        var item = new ContentHierarchyItem
        {
            ContentType = contentType,
            Level = level,
            Slug = "item",
            Name = "Item"
        };

        // Act
        var result = await subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = new MetadataContribution { ProviderType = "seed" },
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
        entityResolver.Verify(
            resolver => resolver.ResolveContentAsync(
                It.IsAny<EntityKind>(),
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<IReadOnlyList<MetadataContribution>>(),
                It.IsAny<IReadOnlyList<MetadataContribution>>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task GetRelationshipsAsync_ReturnsEmpty_WhenResolutionIsUnresolved()
    {
        // Arrange
        var settingsResolver = new Mock<ISettingsResolver>();
        settingsResolver.Setup(value => value.ResolveAsync("en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            });
        var entityResolver = new Mock<IEntityResolver>();
        var cache = new Mock<IQueryCoordinator>();
        entityResolver.Setup(resolver => resolver.ResolveContentAsync(
            EntityKind.Movie,
            It.IsAny<ContentHierarchyItem>(),
            It.IsAny<IReadOnlyList<MetadataContribution>>(),
            It.IsAny<IReadOnlyList<MetadataContribution>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityResolution { Kind = EntityResolutionKind.NotFound });
        var subject = new ContentMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            Mock.Of<IQueryExecutor>(),
            TimeProvider.System,
            NullLogger<ContentMetadataProvider>.Instance);
        var item = new ContentHierarchyItem
        {
            ContentType = Constants.MovieContentType,
            Level = Constants.MovieLevel,
            Slug = "the-matrix",
            Name = "The Matrix"
        };

        // Act
        var result = await subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = new MetadataContribution { ProviderType = "seed" },
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
        cache.Verify(
            resolver => resolver.GetEntityAsync(
                It.IsAny<string>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task GetRelationshipsAsync_ReturnsEmpty_WhenContentEntityIsMissing()
    {
        // Arrange
        var settingsResolver = new Mock<ISettingsResolver>();
        settingsResolver.Setup(value => value.ResolveAsync("en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            });
        var entityResolver = new Mock<IEntityResolver>();
        var cache = new Mock<IQueryCoordinator>();
        entityResolver.Setup(resolver => resolver.ResolveContentAsync(
            EntityKind.Movie,
            It.IsAny<ContentHierarchyItem>(),
            It.IsAny<IReadOnlyList<MetadataContribution>>(),
            It.IsAny<IReadOnlyList<MetadataContribution>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityResolution { Kind = EntityResolutionKind.Resolved, Qid = "Q200" });
        cache.Setup(resolver => resolver.GetEntityAsync(
            "Q200",
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityDto?)null);
        var subject = new ContentMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            Mock.Of<IQueryExecutor>(),
            TimeProvider.System,
            NullLogger<ContentMetadataProvider>.Instance);
        var item = new ContentHierarchyItem
        {
            ContentType = Constants.MovieContentType,
            Level = Constants.MovieLevel,
            Slug = "the-matrix",
            Name = "The Matrix"
        };

        // Act
        var result = await subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = new MetadataContribution { ProviderType = "seed" },
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetRelationshipsAsync_MapsCastRelationship_WhenSubjectResolves()
    {
        // Arrange
        var content = new EntityDto
        {
            Id = "Q200",
            Claims = new Dictionary<string, List<StatementDto>>
            {
                [Constants.CastMemberPropertyId] =
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
                        },
                        Qualifiers = new Dictionary<string, List<SnakDto>>
                        {
                            [Constants.CharacterRoleNamePropertyId] =
                            [
                                new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonDocument.Parse("\"Neo\"").RootElement.Clone()
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };
        var castMember = new EntityDto
        {
            Id = "Q5",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["de"] = new MonolingualTextDto { Language = "de", Value = "Keanu Reeves (DE)" },
                ["en"] = new MonolingualTextDto { Language = "en", Value = "Keanu Reeves" }
            },
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
                                Value = JsonDocument.Parse(
                                    "{\"id\":\"" + Constants.HumanQid + "\"}").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };
        var settingsResolver = new Mock<ISettingsResolver>();
        settingsResolver.Setup(value => value.ResolveAsync("de-DE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext
            {
                Language = "de",
                FallbackLanguages = ["de", "en"],
                UserAgent = "Magpie/test"
            });
        var entityResolver = new Mock<IEntityResolver>();
        var cache = new Mock<IQueryCoordinator>();
        var existing = new MetadataContribution { ProviderType = "seed" };
        var contribution = new MetadataContribution { ProviderType = "seed", Language = "fr" };
        contribution.ExternalIds.Add(new ExternalId { Source = Constants.ProviderType, Value = "Q200" });
        entityResolver.Setup(resolver => resolver.ResolveContentAsync(
            EntityKind.Movie,
            It.IsAny<ContentHierarchyItem>(),
            It.Is<IReadOnlyList<MetadataContribution>>(list =>
                list.Count == 2 && list[0] == contribution && list[1] == existing),
            It.IsAny<IReadOnlyList<MetadataContribution>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityResolution { Kind = EntityResolutionKind.Resolved, Qid = "Q200" });
        cache.Setup(resolver => resolver.GetEntityAsync(
            "Q200",
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);
        cache.Setup(resolver => resolver.GetEntitiesAsync(
            It.Is<IReadOnlyCollection<string>>(qids => qids.Contains("Q5")),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>
            {
                ["Q5"] = castMember
            });
        var subject = new ContentMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            Mock.Of<IQueryExecutor>(),
            TimeProvider.System,
            NullLogger<ContentMetadataProvider>.Instance);
        var item = new ContentHierarchyItem
        {
            ContentType = Constants.MovieContentType,
            Level = Constants.MovieLevel,
            Slug = "the-matrix",
            Name = "The Matrix",
            LanguagePreferences = ["de-DE"]
        };

        // Act
        var result = await subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = contribution,
                ExistingMetadata = [existing],
            },
            default).ConfigureAwait(false);

        // Assert
        var relationship = result.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relationship.RelationshipKind, Is.EqualTo("cast"));
            Assert.That(relationship.RelationshipQualifier, Is.EqualTo("Neo"));
            Assert.That(relationship.SubjectKind, Is.EqualTo("person"));
            Assert.That(relationship.SubjectName, Is.EqualTo("Keanu Reeves (DE)"));
            Assert.That(relationship.RelationshipOrdinal, Is.EqualTo(0));
        }

        cache.Verify(
            resolver => resolver.GetEntitiesAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task GetRelationshipsAsync_ResolvesQualifierFromCharacterLabel_WhenStatementHasNoQualifierText()
    {
        // Arrange
        var content = new EntityDto
        {
            Id = "Q200",
            Claims = new Dictionary<string, List<StatementDto>>
            {
                [Constants.CastMemberPropertyId] =
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
                        },
                        Qualifiers = new Dictionary<string, List<SnakDto>>
                        {
                            [Constants.CharacterRolePropertyId] =
                            [
                                new SnakDto
                                {
                                    SnakType = "value",
                                    DataValue = new DataValueDto
                                    {
                                        Value = JsonDocument.Parse("{\"id\":\"Q10\"}").RootElement.Clone()
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };
        var castMember = new EntityDto
        {
            Id = "Q5",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "Keanu Reeves" }
            },
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
                                Value = JsonDocument.Parse(
                                    "{\"id\":\"" + Constants.HumanQid + "\"}").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };
        var character = new EntityDto
        {
            Id = "Q10",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "Neo" }
            }
        };
        var settingsResolver = new Mock<ISettingsResolver>();
        settingsResolver.Setup(value => value.ResolveAsync("en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            });
        var entityResolver = new Mock<IEntityResolver>();
        var cache = new Mock<IQueryCoordinator>();
        entityResolver.Setup(resolver => resolver.ResolveContentAsync(
            EntityKind.Movie,
            It.IsAny<ContentHierarchyItem>(),
            It.IsAny<IReadOnlyList<MetadataContribution>>(),
            It.IsAny<IReadOnlyList<MetadataContribution>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityResolution { Kind = EntityResolutionKind.Resolved, Qid = "Q200" });
        cache.Setup(resolver => resolver.GetEntityAsync(
            "Q200",
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);
        cache.Setup(resolver => resolver.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>
            {
                ["Q5"] = castMember,
                ["Q10"] = character
            });
        var subject = new ContentMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            Mock.Of<IQueryExecutor>(),
            TimeProvider.System,
            NullLogger<ContentMetadataProvider>.Instance);
        var item = new ContentHierarchyItem
        {
            ContentType = Constants.MovieContentType,
            Level = Constants.MovieLevel,
            Slug = "the-matrix",
            Name = "The Matrix"
        };

        // Act
        var result = await subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = new MetadataContribution { ProviderType = "seed" },
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result.Single().RelationshipQualifier, Is.EqualTo("Neo"));
    }

    [Test]
    public async Task GetRelationshipsAsync_MapsStudioKindAndSkipsUnresolvedSubjects_WhenRelationshipIsStudio()
    {
        // Arrange
        var content = new EntityDto
        {
            Id = "Q200",
            Claims = new Dictionary<string, List<StatementDto>>
            {
                [Constants.ProductionCompanyPropertyId] =
                [
                    new StatementDto
                    {
                        Rank = "normal",
                        Mainsnak = new SnakDto
                        {
                            SnakType = "value",
                            DataValue = new DataValueDto
                            {
                                Value = JsonDocument.Parse("{\"id\":\"Q10\"}").RootElement.Clone()
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
                                Value = JsonDocument.Parse("{\"id\":\"Q11\"}").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };
        var studio = new EntityDto
        {
            Id = "Q10",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "Support Studio" }
            }
        };
        var settingsResolver = new Mock<ISettingsResolver>();
        settingsResolver.Setup(value => value.ResolveAsync("en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            });
        var entityResolver = new Mock<IEntityResolver>();
        var cache = new Mock<IQueryCoordinator>();
        entityResolver.Setup(resolver => resolver.ResolveContentAsync(
            EntityKind.Movie,
            It.IsAny<ContentHierarchyItem>(),
            It.IsAny<IReadOnlyList<MetadataContribution>>(),
            It.IsAny<IReadOnlyList<MetadataContribution>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityResolution { Kind = EntityResolutionKind.Resolved, Qid = "Q200" });
        cache.Setup(resolver => resolver.GetEntityAsync(
            "Q200",
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);
        cache.Setup(resolver => resolver.GetEntitiesAsync(
            It.Is<IReadOnlyCollection<string>>(qids => qids.Contains("Q10") && qids.Contains("Q11")),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>
            {
                ["Q10"] = studio
            });
        var subject = new ContentMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            Mock.Of<IQueryExecutor>(),
            TimeProvider.System,
            NullLogger<ContentMetadataProvider>.Instance);
        var item = new ContentHierarchyItem
        {
            ContentType = Constants.MovieContentType,
            Level = Constants.MovieLevel,
            Slug = "the-matrix",
            Name = "The Matrix"
        };

        // Act
        var result = await subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = item,
                Contribution = new MetadataContribution { ProviderType = "seed" },
                ExistingMetadata = [],
            },
            default).ConfigureAwait(false);

        // Assert
        var relationship = result.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relationship.RelationshipKind, Is.EqualTo("studio"));
            Assert.That(relationship.SubjectKind, Is.EqualTo("studio"));
            Assert.That(relationship.SubjectName, Is.EqualTo("Support Studio"));
        }
    }
}
