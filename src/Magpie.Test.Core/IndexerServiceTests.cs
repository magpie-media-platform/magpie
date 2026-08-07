using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class IndexerServiceTests
{
    private Mock<IIndexerRepository> _indexerRepository = null!;
    private Mock<IIndexerConnectionRepository> _indexerConnectionRepository = null!;
    private Mock<IIndexerFeedRepository> _indexerFeedRepository = null!;
    private Mock<ITransactionManager> _transactionManager = null!;
    private IIndexerService _subject = null!;
    private IIndexerService _feedSubject = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();

        _indexerRepository = new Mock<IIndexerRepository>();
        _indexerRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _indexerRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<IndexerQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Indexer>());
        _indexerRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<Indexer>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Indexer indexer, Guid? _, CancellationToken _) => indexer);
        _indexerRepository
            .Setup(repository => repository.UpdateAsync(
                It.IsAny<Indexer>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indexerRepository
            .Setup(repository => repository.DeleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _indexerConnectionRepository = new Mock<IIndexerConnectionRepository>();
        _indexerConnectionRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<IndexerConnection>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IndexerConnection connection, Guid? _, CancellationToken _) => connection);
        _indexerConnectionRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<IndexerConnectionQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<IndexerConnection>());
        _indexerConnectionRepository
            .Setup(repository => repository.DeleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indexerConnectionRepository
            .Setup(repository => repository.PatchAsync(
                It.IsAny<IndexerConnectionPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _indexerFeedRepository = new Mock<IIndexerFeedRepository>();
        _indexerFeedRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _indexerFeedRepository
            .Setup(repository => repository.DeleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _transactionManager = new Mock<ITransactionManager>();
        _transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        _transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        _transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<(Indexer, IndexerConnection)>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<(Indexer, IndexerConnection)>> operation,
                CancellationToken cancellationToken) => operation(cancellationToken));

        var indexerA = new Mock<IIndexerIntegration>();
        indexerA.SetupGet(integration => integration.ProviderType).Returns("provider-a");
        indexerA
            .Setup(integration => integration.TestConnectionAsync(
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigurationTestResult { Success = true, Message = "Connected." });
        var indexerZ = new Mock<IIndexerIntegration>();
        indexerZ.SetupGet(integration => integration.ProviderType).Returns("provider-z");
        var runtimeIndexer = new Mock<IIndexerIntegration>();
        runtimeIndexer.SetupGet(integration => integration.ProviderType).Returns("runtime-provider");
        var runtimeIndexerFactory = new Mock<IRuntimeIndexerIntegrationFactory>();
        runtimeIndexerFactory.SetupGet(factory => factory.DefinitionType).Returns("cardigann");
        runtimeIndexerFactory
            .Setup(factory => factory.Create(It.Is<Indexer>(indexer =>
                indexer.DefinitionType == "cardigann")))
            .Returns(runtimeIndexer.Object);

        _subject = new IndexerService(
            _indexerRepository.Object,
            _indexerConnectionRepository.Object,
            _indexerFeedRepository.Object,
            [indexerZ.Object, indexerA.Object],
            [runtimeIndexerFactory.Object],
            [],
            Mock.Of<ISettingsService>(),
            _transactionManager.Object,
            Mock.Of<IEventBus>(),
            TimeProvider.System,
            NullLogger<IndexerService>.Instance);
        _feedSubject = _subject;
    }

    [Test]
    public async Task CreateAsync_ReturnsIndexer_WhenProviderTypeIsAvailable()
    {
        // Arrange
        var create = new IndexerCreate { ProviderType = "provider-a", Name = "Indexer" };

        // Act
        var result = await _subject.CreateAsync(create, null).ConfigureAwait(false);

        // Assert
        Assert.That(result.ProviderType, Is.EqualTo("provider-a"));
    }

    [Test]
    public async Task CreateAsync_ReturnsIndexer_WhenProviderTypeIsRuntimeFactory()
    {
        // Arrange
        var create = new IndexerCreate { ProviderType = "cardigann", Name = "Indexer" };

        // Act
        var result = await _subject.CreateAsync(create, null).ConfigureAwait(false);

        // Assert
        Assert.That(result.ProviderType, Is.EqualTo("cardigann"));
    }

    [Test]
    public void PatchAsync_ThrowsArgumentException_WhenProviderTypeIsUnknown()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        _indexerRepository
            .Setup(repository => repository.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Indexer
            {
                Id = indexerId,
                ProviderType = "provider-a",
                Name = "Indexer"
            });

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(async () =>
            await _subject.PatchAsync(
                indexerId,
                new IndexerPatch { ProviderType = "missing" },
                null).ConfigureAwait(false));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo("No indexer integration is available for provider type missing."));
    }

    [Test]
    public void PatchAsync_ThrowsArgumentException_WhenNameIsBlank()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        _indexerRepository
            .Setup(repository => repository.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Indexer
            {
                Id = indexerId,
                ProviderType = "provider-a",
                Name = "Indexer"
            });

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(async () =>
            await _subject.PatchAsync(
                indexerId,
                new IndexerPatch { Name = " " },
                null).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Name is required."));
    }

    [Test]
    public async Task PatchAsync_UpdatesIndexer_WhenProviderTypeIsUnchangedAndIntegrationIsUnavailable()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        _indexerRepository
            .Setup(repository => repository.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Indexer
            {
                Id = indexerId,
                ProviderType = "torznab",
                Name = "Configured indexer"
            });

        // Act
        var result = await _subject.PatchAsync(
            indexerId,
            new IndexerPatch { ProviderType = "torznab", Name = "Renamed indexer" },
            _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _indexerRepository.Verify(
                repository => repository.UpdateAsync(
                    It.Is<Indexer>(indexer =>
                        indexer.Id == indexerId &&
                        indexer.ProviderType == "torznab" &&
                        indexer.Name == "Renamed indexer"),
                    _userId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [TestCase("", "Provider type is required.")]
    [TestCase(
        "nope",
        "No indexer integration is available for provider type nope.")]
    [TestCase(
        "cardigann",
        "No indexer integration is available for provider type cardigann.")]
    public void PatchAsync_ThrowsArgumentException_WhenRuntimeProviderTypeIsInvalid(
        string providerType,
        string expectedMessage)
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        _indexerRepository
            .Setup(repository => repository.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Indexer
            {
                Id = indexerId,
                ProviderType = "runtime-provider",
                Name = "Runtime indexer",
                DefinitionType = "cardigann",
                SchemaData = "{}"
            });

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(async () =>
            await _subject.PatchAsync(
                indexerId,
                new IndexerPatch { ProviderType = providerType },
                null).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo(expectedMessage));
    }

    [Test]
    public async Task PatchAsync_UpdatesIndexer_WhenRuntimeProviderTypeMatchesDefinition()
    {
        // Arrange
        var indexerId = Guid.NewGuid();
        _indexerRepository
            .Setup(repository => repository.GetByIdAsync(indexerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Indexer
            {
                Id = indexerId,
                ProviderType = "stale-provider",
                Name = "Runtime indexer",
                DefinitionType = "cardigann",
                SchemaData = "{}"
            });

        // Act
        var result = await _subject.PatchAsync(
            indexerId,
            new IndexerPatch { ProviderType = "runtime-provider" },
            _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _indexerRepository.Verify(
                repository => repository.UpdateAsync(
                    It.Is<Indexer>(indexer =>
                        indexer.Id == indexerId &&
                        indexer.ProviderType == "runtime-provider"),
                    _userId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task DeleteFeedAsync_DeletesFeedDefinitionsAndConnections_WhenDependentsExist()
    {
        // Arrange
        var feedId = Guid.NewGuid();
        var indexerId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var indexer = new Indexer
        {
            Id = indexerId,
            FeedId = feedId,
            ProviderType = "tracker-a",
            Name = "Tracker A"
        };
        var connection = new IndexerConnection
        {
            Id = connectionId,
            IndexerId = indexerId,
            Name = "Configured tracker"
        };
        _indexerRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<IndexerQuery>(query => query.FeedId == feedId),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Indexer> { Items = [indexer], TotalItems = 1 });
        _indexerConnectionRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<IndexerConnectionQuery>(query => query.IndexerId == indexerId),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<IndexerConnection> { Items = [connection], TotalItems = 1 });

        // Act
        var result = await _feedSubject.DeleteFeedAsync(feedId, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _indexerConnectionRepository.Verify(
                repository => repository.DeleteAsync(connectionId, _userId, It.IsAny<CancellationToken>()),
                Times.Once);
            _indexerRepository.Verify(
                repository => repository.DeleteAsync(indexerId, _userId, It.IsAny<CancellationToken>()),
                Times.Once);
            _indexerFeedRepository.Verify(
                repository => repository.DeleteAsync(feedId, _userId, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task DeleteFeedAsync_DeletesOnlyFeed_WhenNoDependentsExist()
    {
        // Arrange
        var feedId = Guid.NewGuid();

        // Act
        var result = await _feedSubject.DeleteFeedAsync(feedId, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _indexerConnectionRepository.Verify(
                repository => repository.DeleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _indexerRepository.Verify(
                repository => repository.DeleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _indexerFeedRepository.Verify(
                repository => repository.DeleteAsync(feedId, _userId, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task SyncFeedIndexersAsync_CreatesAttachedIndexer_WhenFeedIsFresh()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            ExtensionId = Guid.NewGuid(),
            ProviderType = "cardigann",
            Name = "Cardigann"
        };
        var incoming = new Indexer
        {
            ProviderType = "tracker-a",
            Name = "Tracker A"
        };

        // Act
        await _feedSubject.SyncFeedIndexersAsync(feed, [incoming], _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(incoming.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(incoming.FeedId, Is.EqualTo(feed.Id));
            Assert.That(incoming.ExtensionId, Is.EqualTo(feed.ExtensionId));
            _indexerRepository.Verify(
                repository => repository.CreateAsync(incoming, _userId, It.IsAny<CancellationToken>()),
                Times.Once);
            _indexerRepository.Verify(
                repository => repository.UpdateAsync(
                    It.IsAny<Indexer>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SyncFeedIndexersAsync_ReattachesOrphanAndPreservesConnection_WhenProviderIdentityMatches()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            ProviderType = "cardigann",
            Name = "Recreated Cardigann"
        };
        var orphanId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var orphan = new Indexer
        {
            Id = orphanId,
            FeedId = null,
            ProviderType = "tracker-a",
            Name = "Old Tracker A",
            DefinitionType = "cardigann",
            CreatedAt = new DateTime(2026, 1, 1)
        };
        var connection = new IndexerConnection
        {
            Id = connectionId,
            IndexerId = orphanId,
            Name = "Configured tracker"
        };
        var incoming = new Indexer
        {
            ProviderType = "TRACKER-A",
            Name = "Updated Tracker A",
            Description = "Updated definition"
        };
        _indexerRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([orphan]);
        _indexerConnectionRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<IndexerConnectionQuery>(query => query.IndexerId == orphanId),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<IndexerConnection> { Items = [connection], TotalItems = 1 });

        // Act
        await _feedSubject.SyncFeedIndexersAsync(feed, [incoming], _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(orphan.Id, Is.EqualTo(orphanId));
            Assert.That(orphan.FeedId, Is.EqualTo(feed.Id));
            Assert.That(orphan.ProviderType, Is.EqualTo("TRACKER-A"));
            Assert.That(orphan.Name, Is.EqualTo("Updated Tracker A"));
            Assert.That(orphan.Description, Is.EqualTo("Updated definition"));
            _indexerRepository.Verify(
                repository => repository.UpdateAsync(
                    It.Is<Indexer>(indexer => indexer.Id == orphanId && indexer.FeedId == feed.Id),
                    _userId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _indexerRepository.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<Indexer>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _indexerRepository.Verify(
                repository => repository.DeleteAsync(orphanId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _indexerConnectionRepository.Verify(
                repository => repository.DeleteAsync(connectionId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SyncFeedIndexersAsync_ReattachesOrphanAndDeletesAttachedDuplicate_WhenBothMatch()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            ProviderType = "cardigann",
            Name = "Recreated Cardigann"
        };
        var orphanId = Guid.NewGuid();
        var duplicateId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var orphan = new Indexer
        {
            Id = orphanId,
            ProviderType = "tracker-a",
            Name = "Original Tracker A",
            DefinitionType = "cardigann",
            CreatedAt = new DateTime(2026, 1, 1)
        };
        var duplicate = new Indexer
        {
            Id = duplicateId,
            FeedId = feed.Id,
            ProviderType = "tracker-a",
            Name = "Duplicate Tracker A",
            DefinitionType = "cardigann",
            CreatedAt = new DateTime(2026, 2, 1)
        };
        var connection = new IndexerConnection
        {
            Id = connectionId,
            IndexerId = orphanId,
            Name = "Configured tracker"
        };
        var incoming = new Indexer
        {
            ProviderType = "tracker-a",
            Name = "Updated Tracker A"
        };
        _indexerRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([duplicate, orphan]);
        _indexerConnectionRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<IndexerConnectionQuery>(query => query.IndexerId == orphanId),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<IndexerConnection> { Items = [connection], TotalItems = 1 });

        // Act
        await _feedSubject.SyncFeedIndexersAsync(feed, [incoming], _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(orphan.FeedId, Is.EqualTo(feed.Id));
            _indexerRepository.Verify(
                repository => repository.UpdateAsync(
                    It.Is<Indexer>(indexer => indexer.Id == orphanId && indexer.FeedId == feed.Id),
                    _userId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _indexerRepository.Verify(
                repository => repository.DeleteAsync(duplicateId, _userId, It.IsAny<CancellationToken>()),
                Times.Once);
            _indexerRepository.Verify(
                repository => repository.DeleteAsync(orphanId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _indexerConnectionRepository.Verify(
                repository => repository.DeleteAsync(connectionId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _indexerRepository.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<Indexer>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SyncFeedIndexersAsync_KeepsOrphanAndAttachedIndexer_WhenAnotherFeedExists()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            ProviderType = "cardigann",
            Name = "Cardigann A"
        };
        var otherFeed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            ProviderType = "cardigann",
            Name = "Cardigann B"
        };
        var attachedId = Guid.NewGuid();
        var orphanId = Guid.NewGuid();
        var attachedConnectionId = Guid.NewGuid();
        var orphanConnectionId = Guid.NewGuid();
        var attached = new Indexer
        {
            Id = attachedId,
            FeedId = feed.Id,
            ProviderType = "tracker-a",
            Name = "Tracker A",
            DefinitionType = "cardigann",
            CreatedAt = new DateTime(2026, 2, 1)
        };
        var orphan = new Indexer
        {
            Id = orphanId,
            FeedId = null,
            ProviderType = "tracker-a",
            Name = "Tracker A from the other feed",
            DefinitionType = "cardigann",
            CreatedAt = new DateTime(2026, 1, 1)
        };
        var attachedConnection = new IndexerConnection
        {
            Id = attachedConnectionId,
            IndexerId = attachedId,
            Name = "Configured tracker"
        };
        var orphanConnection = new IndexerConnection
        {
            Id = orphanConnectionId,
            IndexerId = orphanId,
            Name = "Configured tracker from the other feed"
        };
        var incoming = new Indexer
        {
            ProviderType = "tracker-a",
            Name = "Updated Tracker A"
        };
        _indexerFeedRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([feed, otherFeed]);
        _indexerRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([attached, orphan]);
        _indexerConnectionRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<IndexerConnectionQuery>(query => query.IndexerId == attachedId),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<IndexerConnection> { Items = [attachedConnection], TotalItems = 1 });
        _indexerConnectionRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<IndexerConnectionQuery>(query => query.IndexerId == orphanId),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<IndexerConnection> { Items = [orphanConnection], TotalItems = 1 });

        // Act
        await _feedSubject.SyncFeedIndexersAsync(feed, [incoming], _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(orphan.FeedId, Is.Null);
            Assert.That(orphan.Name, Is.EqualTo("Tracker A from the other feed"));
            Assert.That(attached.Name, Is.EqualTo("Updated Tracker A"));
            _indexerRepository.Verify(
                repository => repository.UpdateAsync(
                    It.Is<Indexer>(indexer => indexer.Id == attachedId),
                    _userId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _indexerRepository.Verify(
                repository => repository.UpdateAsync(
                    It.Is<Indexer>(indexer => indexer.Id == orphanId),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _indexerRepository.Verify(
                repository => repository.DeleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _indexerConnectionRepository.Verify(
                repository => repository.DeleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SyncFeedIndexersAsync_DeletesRemainingOrphans_WhenSeveralShareOneProviderType()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            ProviderType = "cardigann",
            Name = "Recreated Cardigann"
        };
        var oldestId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        var oldest = new Indexer
        {
            Id = oldestId,
            FeedId = null,
            ProviderType = "tracker-a",
            Name = "Tracker A",
            DefinitionType = "cardigann",
            CreatedAt = new DateTime(2026, 1, 1)
        };
        var second = new Indexer
        {
            Id = secondId,
            FeedId = null,
            ProviderType = "tracker-a",
            Name = "Tracker A",
            DefinitionType = "cardigann",
            CreatedAt = new DateTime(2026, 2, 1)
        };
        var third = new Indexer
        {
            Id = thirdId,
            FeedId = null,
            ProviderType = "tracker-a",
            Name = "Tracker A",
            DefinitionType = "cardigann",
            CreatedAt = new DateTime(2026, 3, 1)
        };
        var incoming = new Indexer
        {
            ProviderType = "tracker-a",
            Name = "Updated Tracker A"
        };
        _indexerRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([third, oldest, second]);

        // Act
        await _feedSubject.SyncFeedIndexersAsync(feed, [incoming], _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldest.FeedId, Is.EqualTo(feed.Id));
            _indexerRepository.Verify(
                repository => repository.UpdateAsync(
                    It.Is<Indexer>(indexer => indexer.Id == oldestId),
                    _userId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _indexerRepository.Verify(
                repository => repository.DeleteAsync(secondId, _userId, It.IsAny<CancellationToken>()),
                Times.Once);
            _indexerRepository.Verify(
                repository => repository.DeleteAsync(thirdId, _userId, It.IsAny<CancellationToken>()),
                Times.Once);
            _indexerRepository.Verify(
                repository => repository.DeleteAsync(oldestId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SyncFeedIndexersAsync_KeepsIndexer_WhenItCarriesNoFeedDefinition()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            ProviderType = "cardigann",
            Name = "Cardigann"
        };
        var manualId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var manual = new Indexer
        {
            Id = manualId,
            FeedId = null,
            ProviderType = "tracker-a",
            Name = "Hand configured Tracker A",
            CreatedAt = new DateTime(2026, 1, 1)
        };
        var connection = new IndexerConnection
        {
            Id = connectionId,
            IndexerId = manualId,
            Name = "Configured tracker"
        };
        var incoming = new Indexer
        {
            ProviderType = "tracker-a",
            Name = "Feed Tracker A"
        };
        _indexerRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([manual]);
        _indexerConnectionRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<IndexerConnectionQuery>(query => query.IndexerId == manualId),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<IndexerConnection> { Items = [connection], TotalItems = 1 });

        // Act
        await _feedSubject.SyncFeedIndexersAsync(feed, [incoming], _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(manual.FeedId, Is.Null);
            Assert.That(manual.Name, Is.EqualTo("Hand configured Tracker A"));
            _indexerRepository.Verify(
                repository => repository.CreateAsync(incoming, _userId, It.IsAny<CancellationToken>()),
                Times.Once);
            _indexerRepository.Verify(
                repository => repository.UpdateAsync(
                    It.IsAny<Indexer>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _indexerRepository.Verify(
                repository => repository.DeleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _indexerConnectionRepository.Verify(
                repository => repository.DeleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SyncFeedIndexersAsync_UpdatesSingleIndexer_WhenRunTwice()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            ProviderType = "cardigann",
            Name = "Cardigann"
        };
        var first = new Indexer
        {
            ProviderType = "tracker-a",
            Name = "Tracker A"
        };
        var second = new Indexer
        {
            ProviderType = "tracker-a",
            Name = "Updated Tracker A"
        };
        _indexerRepository
            .SetupSequence(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([first]);

        // Act
        await _feedSubject.SyncFeedIndexersAsync(feed, [first], _userId).ConfigureAwait(false);
        await _feedSubject.SyncFeedIndexersAsync(feed, [second], _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(first.FeedId, Is.EqualTo(feed.Id));
            Assert.That(first.Name, Is.EqualTo("Updated Tracker A"));
            _indexerRepository.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<Indexer>(),
                    _userId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _indexerRepository.Verify(
                repository => repository.UpdateAsync(
                    It.Is<Indexer>(indexer => indexer.Id == first.Id),
                    _userId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _indexerRepository.Verify(
                repository => repository.DeleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
