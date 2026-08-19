using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata;
using Magpie.Extensions.Wikidata.Mapping;
using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Data;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Wikidata;

[TestFixture]
internal sealed class SubjectMetadataProviderTests
{
    [Test]
    public async Task TryGetMetadataAsync_ReturnsNull_WhenSubjectKindIsUnsupported()
    {
        // Arrange
        var entityResolver = new Mock<IEntityResolver>();
        var cache = new Mock<IQueryCoordinator>();
        var subject = new SubjectMetadataProvider(
            null!,
            entityResolver.Object,
            cache.Object,
            TimeProvider.System);

        // Act
        var result = await subject.TryGetMetadataAsync(
            new Subject { Kind = "vehicle" },
            [],
            "en",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
        entityResolver.Verify(
            resolver => resolver.ResolveSubjectAsync(
                It.IsAny<EntityKind>(),
                It.IsAny<IEnumerable<MetadataContribution>>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCase(EntityResolutionKind.NotFound)]
    [TestCase(EntityResolutionKind.Ambiguous)]
    public async Task TryGetMetadataAsync_ReturnsNull_WhenResolutionIsUnresolved(EntityResolutionKind kind)
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
        entityResolver.Setup(resolver => resolver.ResolveSubjectAsync(
            EntityKind.Person,
            It.IsAny<IEnumerable<MetadataContribution>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityResolution { Kind = kind });
        var subject = new SubjectMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            TimeProvider.System);

        // Act
        var result = await subject.TryGetMetadataAsync(
            new Subject { Kind = "person" },
            [],
            "en",
            default).ConfigureAwait(false);

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
        entityResolver.Setup(resolver => resolver.ResolveSubjectAsync(
            EntityKind.Studio,
            It.IsAny<IEnumerable<MetadataContribution>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityResolution { Kind = EntityResolutionKind.Resolved, Qid = "Q99" });
        cache.Setup(resolver => resolver.GetEntityAsync(
            "Q99",
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityDto?)null);
        var subject = new SubjectMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            TimeProvider.System);

        // Act
        var result = await subject.TryGetMetadataAsync(
            new Subject { Kind = "studio" },
            [],
            "en",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task TryGetMetadataAsync_MapsEntity_WhenSubjectResolves()
    {
        // Arrange
        var entity = new EntityDto
        {
            Id = "Q77",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "Jane Doe" }
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
        settingsResolver.Setup(value => value.ResolveAsync("en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext
            {
                Language = "en",
                FallbackLanguages = ["en"],
                UserAgent = "Magpie/test"
            });
        var entityResolver = new Mock<IEntityResolver>();
        var cache = new Mock<IQueryCoordinator>();
        entityResolver.Setup(resolver => resolver.ResolveSubjectAsync(
            EntityKind.Person,
            It.IsAny<IEnumerable<MetadataContribution>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityResolution { Kind = EntityResolutionKind.Resolved, Qid = "Q77" });
        cache.Setup(resolver => resolver.GetEntityAsync(
            "Q77",
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        cache.Setup(resolver => resolver.GetEntitiesAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>());
        var subject = new SubjectMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            TimeProvider.System);

        // Act
        var result = await subject.TryGetMetadataAsync(
            new Subject { Kind = "person" },
            [],
            "en",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Title, Is.EqualTo("Jane Doe"));
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
            Id = "Q77",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "Jane Doe" }
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
                                Value = JsonDocument.Parse("{\"id\":\"Q530\"}").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };
        var award = new EntityDto
        {
            Id = "Q530",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto { Language = "en", Value = "Academy Award for Best Actress" }
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
                                Value = JsonDocument.Parse("{\"id\":\"Q640\"}").RootElement.Clone()
                            }
                        }
                    }
                ]
            }
        };
        var body = new EntityDto
        {
            Id = "Q640",
            Labels = new Dictionary<string, MonolingualTextDto>
            {
                ["en"] = new MonolingualTextDto
                {
                    Language = "en",
                    Value = "Academy of Motion Picture Arts and Sciences"
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
        entityResolver.Setup(resolver => resolver.ResolveSubjectAsync(
            EntityKind.Person,
            It.IsAny<IEnumerable<MetadataContribution>>(),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityResolution { Kind = EntityResolutionKind.Resolved, Qid = "Q77" });
        cache.Setup(resolver => resolver.GetEntityAsync(
            "Q77",
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        cache.Setup(resolver => resolver.GetEntitiesAsync(
            It.Is<IReadOnlyCollection<string>>(qids => qids.Contains("Q530")),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>
            {
                ["Q530"] = award
            });
        cache.Setup(resolver => resolver.GetEntitiesAsync(
            It.Is<IReadOnlyCollection<string>>(qids => qids.Contains("Q640")),
            It.IsAny<RequestContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EntityDto>
            {
                ["Q640"] = body
            });
        var subject = new SubjectMetadataProvider(
            settingsResolver.Object,
            entityResolver.Object,
            cache.Object,
            TimeProvider.System);

        // Act
        var result = await subject.TryGetMetadataAsync(
            new Subject { Kind = "person" },
            [],
            "en",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Awards.Single().Name, Is.EqualTo("Academy Award for Best Actress"));
            Assert.That(
                result.Awards.Single().Body,
                Is.EqualTo("Academy of Motion Picture Arts and Sciences"));
        }

        cache.Verify(
            resolver => resolver.GetEntitiesAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<RequestContext>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
