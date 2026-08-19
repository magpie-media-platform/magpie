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
internal sealed class ContentMetadataProviderTests
{
    [TestCase("movie", "season")]
    [TestCase("tv", "movie")]
    [TestCase("game", "movie")]
    public async Task TryGetMetadataAsync_ReturnsNull_WhenContentTypeAndLevelAreUnsupported(
        string contentType,
        string level)
    {
        // Arrange
        var entityResolver = new Mock<IEntityResolver>();
        var cache = new Mock<IQueryCoordinator>();
        var queryExecutor = new Mock<IQueryExecutor>();
        var subject = new ContentMetadataProvider(
            null!,
            entityResolver.Object,
            cache.Object,
            queryExecutor.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataProvider>.Instance);
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                ContentType = contentType,
                Level = level,
                Slug = "item",
                Name = "Item"
            },
            ExistingMetadata = [],
            DefaultLanguagePreference = "en"
        };

        // Act
        var result = await subject.TryGetMetadataAsync(request, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
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
    public async Task TryGetMetadataAsync_ReturnsNull_WhenResolutionIsUnresolved()
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
        var queryExecutor = new Mock<IQueryExecutor>();
        var subject = new ContentMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            queryExecutor.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataProvider>.Instance);
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                ContentType = "movie",
                Level = "movie",
                Slug = "a-movie",
                Name = "A Movie"
            },
            ExistingMetadata = [],
            DefaultLanguagePreference = "en"
        };

        // Act
        var result = await subject.TryGetMetadataAsync(request, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
        cache.Verify(
            resolver => resolver.GetEntityAsync(
                It.IsAny<string>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task TryGetMetadataAsync_ReturnsNull_WhenEntityIsMissing()
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
            .ReturnsAsync(new EntityResolution { Kind = EntityResolutionKind.Resolved, Qid = "Q300" });
        cache.Setup(resolver => resolver.GetEntityAsync(
            "Q300",
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityDto?)null);
        var queryExecutor = new Mock<IQueryExecutor>();
        var subject = new ContentMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            queryExecutor.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataProvider>.Instance);
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                ContentType = "movie",
                Level = "movie",
                Slug = "a-movie",
                Name = "A Movie"
            },
            ExistingMetadata = [],
            DefaultLanguagePreference = "en"
        };

        // Act
        var result = await subject.TryGetMetadataAsync(request, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TryGetMetadataAsync_ReturnsNull_WhenEntityKindDoesNotMatch()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q300",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "Not A Movie" }
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
            .ReturnsAsync(new EntityResolution { Kind = EntityResolutionKind.Resolved, Qid = "Q300" });
        cache.Setup(resolver => resolver.GetEntityAsync(
            "Q300",
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        var queryExecutor = new Mock<IQueryExecutor>();
        var subject = new ContentMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            queryExecutor.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataProvider>.Instance);
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                ContentType = "movie",
                Level = "movie",
                Slug = "a-movie",
                Name = "A Movie"
            },
            ExistingMetadata = [],
            DefaultLanguagePreference = "en"
        };

        // Act
        var result = await subject.TryGetMetadataAsync(request, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TryGetMetadataAsync_BuildsSeasonChildren_WhenShowResolvesWithoutAwards()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q200",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "A Show" }
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
                                    "{\"id\":\"" + Constants.TelevisionSeriesQid + "\"}")
                                    .RootElement.Clone()
                            }
                        }
                    }
                ]
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
            EntityKind.Show,
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
            .ReturnsAsync(entity);
        cache.Setup(resolver => resolver.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>());
        var queryExecutor = new Mock<IQueryExecutor>();
        queryExecutor.Setup(executor => executor.GetHierarchyAsync(
            EntityKind.Season,
            "Q200",
            null,
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HierarchyResult
            {
                Rows =
                [
                    new HierarchyRow
                    {
                        EntityKind = EntityKind.Season,
                        Qid = "Q100",
                        Ordinal = 1,
                        Title = "Season 1"
                    }
                ],
                Truncated = false
            });
        var subject = new ContentMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            queryExecutor.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataProvider>.Instance);
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                ContentType = "tv",
                Level = "show",
                Slug = "a-show",
                Name = "A Show"
            },
            ExistingMetadata = [],
            DefaultLanguagePreference = "en"
        };

        // Act
        var result = await subject.TryGetMetadataAsync(request, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Contribution?.Title, Is.EqualTo("A Show"));
            Assert.That(result.Children, Has.Count.EqualTo(1));
            Assert.That(result.Children[0].Level, Is.EqualTo("season"));
            Assert.That(result.Children[0].Slug, Is.EqualTo("season-1"));
        }

        cache.Verify(
            resolver => resolver.GetEntitiesAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task TryGetMetadataAsync_FetchesAwardBodyEntities_WhenAwardConferrerIsUnresolved()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q300",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "A Movie" }
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
                                    "{\"id\":\"" + Constants.MovieQid + "\"}").RootElement.Clone()
                            }
                        }
                    }
                ],
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
                                Value = JsonDocument.Parse("{\"id\":\"Q500\"}").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };
        var award = new EntityDto
        {
            Id = "Q500",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto
                {
                    Language = "en",
                    Value = "Saturn Award for Best Science Fiction Film"
                }
            },
            Claims = new Dictionary<string, List<StatementDto>>
            {
                [Constants.ConferredByPropertyId] =
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
        };
        var body = new EntityDto
        {
            Id = "Q600",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto
                {
                    Language = "en",
                    Value = "Academy of Science Fiction Fantasy and Horror Films"
                }
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
            .ReturnsAsync(new EntityResolution { Kind = EntityResolutionKind.Resolved, Qid = "Q300" });
        cache.Setup(resolver => resolver.GetEntityAsync(
            "Q300",
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        cache.Setup(resolver => resolver.GetEntitiesAsync(
            It.Is<IReadOnlyCollection<string>>(qids => qids.Contains("Q500")),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>
            {
                ["Q500"] = award
            });
        cache.Setup(resolver => resolver.GetEntitiesAsync(
            It.Is<IReadOnlyCollection<string>>(qids => qids.Contains("Q600")),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>
            {
                ["Q600"] = body
            });
        var queryExecutor = new Mock<IQueryExecutor>();
        var subject = new ContentMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            queryExecutor.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataProvider>.Instance);
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                ContentType = "movie",
                Level = "movie",
                Slug = "a-movie",
                Name = "A Movie"
            },
            ExistingMetadata = [],
            DefaultLanguagePreference = "en"
        };

        // Act
        var result = await subject.TryGetMetadataAsync(request, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Contribution?.Awards.Single().Name,
                Is.EqualTo("Saturn Award for Best Science Fiction Film"));
            Assert.That(
                result.Contribution!.Awards.Single().Body,
                Is.EqualTo("Academy of Science Fiction Fantasy and Horror Films"));
        }

        cache.Verify(
            resolver => resolver.GetEntitiesAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        queryExecutor.Verify(
            executor => executor.GetHierarchyAsync(
                It.IsAny<EntityKind>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task TryGetMetadataAsync_ReturnsEmptyChildrenAndSkipsHierarchy_WhenSeasonHasNoSequenceNumber()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q100",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "Season 1" }
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
                                    "{\"id\":\"" + Constants.SeasonQid + "\"}").RootElement.Clone()
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
                                Value = JsonDocument.Parse("{\"id\":\"Q200\"}").RootElement.Clone()
                            }
                        }
                    }
                ]
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
            EntityKind.Season,
            It.IsAny<ContentHierarchyItem>(),
            It.IsAny<IReadOnlyList<MetadataContribution>>(),
            It.IsAny<IReadOnlyList<MetadataContribution>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityResolution { Kind = EntityResolutionKind.Resolved, Qid = "Q100" });
        cache.Setup(resolver => resolver.GetEntityAsync(
            "Q100",
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        cache.Setup(resolver => resolver.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>());
        var queryExecutor = new Mock<IQueryExecutor>();
        var subject = new ContentMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            queryExecutor.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataProvider>.Instance);
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                ContentType = "tv",
                Level = "season",
                Slug = "season-1",
                Name = "Season 1"
            },
            ExistingMetadata = [],
            DefaultLanguagePreference = "en"
        };

        // Act
        var result = await subject.TryGetMetadataAsync(request, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Children, Is.Empty);
        queryExecutor.Verify(
            executor => executor.GetHierarchyAsync(
                It.IsAny<EntityKind>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
