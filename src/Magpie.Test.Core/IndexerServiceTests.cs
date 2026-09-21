using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Config;
using Magpie.Core.Releases;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;
using Magpie.Sdk.Caching;
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
    private Mock<IRuntimeIndexerIntegrationFactory> _runtimeIndexerFactory = null!;
    private IIndexerService _subject = null!;
    private IIndexerService _feedSubject = null!;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();

        _indexerRepository = new Mock<IIndexerRepository> { CallBase = true };
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
            .Setup(repository => repository.PatchAsync(
                It.IsAny<IndexerPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _indexerRepository
            .Setup(repository => repository.DeleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _indexerConnectionRepository = new Mock<IIndexerConnectionRepository> { CallBase = true };
        _indexerConnectionRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
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

        _indexerFeedRepository = new Mock<IIndexerFeedRepository> { CallBase = true };
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
        _runtimeIndexerFactory = new Mock<IRuntimeIndexerIntegrationFactory>();
        _runtimeIndexerFactory.SetupGet(factory => factory.DefinitionType).Returns("cardigann");
        _runtimeIndexerFactory
            .Setup(factory => factory.Create(It.Is<Indexer>(indexer =>
                indexer.DefinitionType == "cardigann")))
            .Returns(runtimeIndexer.Object);

        _subject = new IndexerService(
            _indexerRepository.Object,
            _indexerConnectionRepository.Object,
            _indexerFeedRepository.Object,
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IReleaseRepository>(),
            Mock.Of<IReleaseScopeRepository>(),
            [indexerZ.Object, indexerA.Object],
            [_runtimeIndexerFactory.Object],
            [],
            Mock.Of<ISettingsService>(),
            _transactionManager.Object,
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseSearchQueryExecutor>(),
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
    public async Task ListCapabilitiesAsync_DoesNotCreateIntegration_WhenIndexerHasNoConnection()
    {
        // Arrange
        _indexerRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Indexer
                {
                    Id = Guid.NewGuid(),
                    ProviderType = "cardigann",
                    Name = "Catalogue only",
                    DefinitionType = "cardigann",
                    SchemaData = "definition",
                },
            ]);

        // Act
        var capabilities = await _subject.ListCapabilitiesAsync().ConfigureAwait(false);

        // Assert
        Assert.That(capabilities, Is.Empty);
        _runtimeIndexerFactory.Verify(factory => factory.Create(It.IsAny<Indexer>()), Times.Never);
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
                new IndexerPatch { ProviderType = new() { Value = "missing" } },
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
                new IndexerPatch { Name = new() { Value = " " } },
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
            new IndexerPatch
            {
                ProviderType = new() { Value = "torznab" },
                Name = new() { Value = "Renamed indexer" }
            },
            _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _indexerRepository.Verify(
                repository => repository.PatchAsync(
                    It.Is<IndexerPatch>(applied =>
                        applied.Id == indexerId &&
                        applied.ProviderType!.Value == "torznab" &&
                        applied.Name!.Value == "Renamed indexer"),
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
                new IndexerPatch { ProviderType = new() { Value = providerType } },
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
            new IndexerPatch { ProviderType = new() { Value = "runtime-provider" } },
            _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _indexerRepository.Verify(
                repository => repository.PatchAsync(
                    It.Is<IndexerPatch>(applied =>
                        applied.Id == indexerId &&
                        applied.ProviderType!.Value == "runtime-provider"),
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
    public async Task PatchFeedAsync_ForwardsTheSettingsPatch_WhenThePatchNamesOneKey()
    {
        // Arrange
        var feedId = Guid.NewGuid();
        IndexerFeedPatch? applied = null;
        _indexerFeedRepository
            .Setup(repository => repository.PatchAsync(
                It.IsAny<IndexerFeedPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback((IndexerFeedPatch feedPatch, Guid? _, CancellationToken _) => applied = feedPatch)
            .ReturnsAsync(true);
        var patch = new IndexerFeedPatch
        {
            Settings = new()
            {
                Add = new Dictionary<string, string>
                {
                    ["url"] = "https://feeds.example/other"
                }
            }
        };

        // Act
        var patched = await _feedSubject.PatchFeedAsync(feedId, patch, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(patched, Is.True);
            Assert.That(applied!.Id, Is.EqualTo(feedId));
            Assert.That(applied.Settings!.Add!["url"], Is.EqualTo("https://feeds.example/other"));
            Assert.That(applied.Settings.Remove, Is.Null);
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
    public async Task SyncFeedIndexersAsync_KeepsRemainingOrphans_WhenSeveralShareOneProviderType()
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
                repository => repository.DeleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task SyncFeedIndexersAsync_DeletesNothing_WhenTheSyncDiscoveredNoIndexers()
    {
        // Arrange
        var feed = new IndexerFeed
        {
            Id = Guid.NewGuid(),
            ProviderType = "cardigann",
            Name = "Cardigann"
        };
        var attached = new Indexer
        {
            Id = Guid.NewGuid(),
            FeedId = feed.Id,
            ProviderType = "tracker-a",
            Name = "Tracker A",
            DefinitionType = "cardigann",
            CreatedAt = new DateTime(2026, 1, 1)
        };
        _indexerRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([attached]);

        // Act
        await _feedSubject.SyncFeedIndexersAsync(feed, [], _userId).ConfigureAwait(false);

        // Assert
        _indexerRepository.Verify(
            repository => repository.DeleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
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

    [TestFixture]
    public sealed class IndexerServiceSearchTests
    {
        private static readonly Guid IndexerAId = new("11111111-1111-1111-1111-111111111111");
        private static readonly Guid IndexerBId = new("22222222-2222-2222-2222-222222222222");
        private static readonly Guid ConnectionAId = new("33333333-3333-3333-3333-333333333333");
        private static readonly Guid ConnectionBId = new("44444444-4444-4444-4444-444444444444");
        private static readonly Guid DisabledConnectionId = new("55555555-5555-5555-5555-555555555555");
        private static readonly Guid ContentHierarchyItemId = new("66666666-6666-6666-6666-666666666666");

        private Mock<IIndexerRepository> _indexers = null!;
        private Mock<IIndexerConnectionRepository> _indexerConnections = null!;
        private Mock<IContentHierarchyItemRepository> _contentHierarchyItems = null!;
        private Mock<IReleaseRepository> _releases = null!;
        private Mock<IReleaseScopeRepository> _releaseScopes = null!;
        private Mock<IIndexerIntegration> _integrationA = null!;
        private Mock<IIndexerIntegration> _integrationB = null!;
        private IIndexerService _subject = null!;

        [SetUp]
        public void SetUp()
        {
            _indexers = new Mock<IIndexerRepository> { CallBase = true };
            _indexers
                .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new Indexer
                    {
                        Id = IndexerAId,
                        ProviderType = "indexer-a",
                        Name = "Indexer A",
                        CreatedAt = DateTime.UtcNow,
                    },
                    new Indexer
                    {
                        Id = IndexerBId,
                        ProviderType = "indexer-b",
                        Name = "Indexer B",
                        CreatedAt = DateTime.UtcNow,
                    },
                ]);

            _indexerConnections = new Mock<IIndexerConnectionRepository> { CallBase = true };
            _indexerConnections
                .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new IndexerConnection
                    {
                        Id = ConnectionAId,
                        IndexerId = IndexerAId,
                        Name = "Connection A",
                        Enabled = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                    new IndexerConnection
                    {
                        Id = ConnectionBId,
                        IndexerId = IndexerBId,
                        Name = "Connection B",
                        Enabled = true,
                        CreatedAt = DateTime.UtcNow,
                    },
                    new IndexerConnection
                    {
                        Id = DisabledConnectionId,
                        IndexerId = IndexerBId,
                        Name = "Connection C",
                        Enabled = false,
                        CreatedAt = DateTime.UtcNow,
                    },
                ]);

            _contentHierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
            _contentHierarchyItems
                .Setup(repository => repository.GetByIdAsync(ContentHierarchyItemId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ContentHierarchyItem
                {
                    Id = ContentHierarchyItemId,
                    ContentType = "movie",
                    Level = "movie",
                    Slug = "the-salt-roads",
                    Name = "The Salt Roads",
                });

            _releases = new Mock<IReleaseRepository> { CallBase = true };
            _releases
                .Setup(repository => repository.UpdateAsync(
                    It.IsAny<Release>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            _releases
                .Setup(repository => repository.CreateAsync(
                    It.IsAny<Release>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Release release, Guid? _, CancellationToken _) => release);

            _releaseScopes = new Mock<IReleaseScopeRepository> { CallBase = true };
            _releaseScopes
                .Setup(repository => repository.QueryAsync(
                    It.IsAny<ReleaseScopeQuery>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PagedList<ReleaseScope>());

            _integrationA = new Mock<IIndexerIntegration>();
            _integrationA.SetupGet(integration => integration.ProviderType).Returns("indexer-a");
            _integrationA.SetupGet(integration => integration.SupportedContentTypes).Returns([]);
            _integrationA
                .SetupGet(integration => integration.SearchFacets)
                .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "q", "imdbid" }]);
            _integrationA.SetupGet(integration => integration.SupportsKeywordSearch).Returns(true);
            _integrationA
                .Setup(integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            _integrationB = new Mock<IIndexerIntegration>();
            _integrationB.SetupGet(integration => integration.ProviderType).Returns("indexer-b");
            _integrationB.SetupGet(integration => integration.SupportedContentTypes).Returns([]);
            _integrationB
                .SetupGet(integration => integration.SearchFacets)
                .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "q", "imdbid" }]);
            _integrationB.SetupGet(integration => integration.SupportsKeywordSearch).Returns(true);
            _integrationB
                .Setup(integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            var transactions = new Mock<ITransactionManager>();
            transactions
                .Setup(manager => manager.ExecuteWithTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<Guid?>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<CancellationToken, Task<Guid?>> operation, CancellationToken token) => operation(token));

            var executor = new ReleaseSearchQueryExecutor(
                _indexers.Object,
                _indexerConnections.Object,
                Mock.Of<IEventBus>(),
                [_integrationA.Object, _integrationB.Object],
                [],
                TimeProvider.System,
                NullLogger<ReleaseSearchQueryExecutor>.Instance,
            releaseEvaluationService: Mock.Of<IReleaseEvaluationService>(),
            cacheFactory: Mock.Of<ICacheFactory>(),
            releaseComparers: [],
            searchOptions: new SearchOptions());

            _subject = new IndexerService(
                _indexers.Object,
                _indexerConnections.Object,
                Mock.Of<IIndexerFeedRepository>(),
                _contentHierarchyItems.Object,
                _releases.Object,
                _releaseScopes.Object,
                [_integrationA.Object, _integrationB.Object],
                [],
                [],
                Mock.Of<ISettingsService>(),
                transactions.Object,
                Mock.Of<IEventBus>(),
                executor,
                TimeProvider.System,
                NullLogger<IndexerService>.Instance);
        }

        [Test]
        public async Task ListCapabilitiesAsync_ReturnsAdvertisedFacets_WhenIntegrationIsRegistered()
        {
            // Act
            var capabilities = await _subject.ListCapabilitiesAsync().ConfigureAwait(false);

            // Assert
            var indexerA = capabilities.Single(capability => capability.IndexerId == IndexerAId);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(capabilities, Has.Count.EqualTo(2));
                Assert.That(indexerA.SupportsKeywordSearch, Is.True);
                Assert.That(indexerA.SearchFacets, Has.Count.EqualTo(1));
                Assert.That(indexerA.SearchFacets[0], Is.EquivalentTo(["q", "imdbid"]));
            }
        }

        [Test]
        public async Task ListCapabilitiesAsync_ReturnsEmptyCapabilities_WhenOneDefinitionIsInvalid()
        {
            // Arrange
            _integrationA.SetupGet(integration => integration.SearchFacets).Throws(new InvalidOperationException());

            // Act
            var capabilities = await _subject.ListCapabilitiesAsync().ConfigureAwait(false);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(capabilities, Has.Count.EqualTo(2));
                Assert.That(
                    capabilities.Single(capability => capability.IndexerId == IndexerAId).SearchFacets,
                    Is.Empty);
                Assert.That(
                    capabilities.Single(capability => capability.IndexerId == IndexerBId).SearchFacets,
                    Is.Not.Empty);
            }
        }

        [Test]
        public async Task SearchAsync_ReturnsNoCandidates_WhenSelectedIndexersDoNotSupportTheQuery()
        {
            // Arrange
            _integrationA.SetupGet(integration => integration.SupportsKeywordSearch).Returns(false);
            _integrationB.SetupGet(integration => integration.SupportsKeywordSearch).Returns(false);
            var request = new IndexerSearchRequest
            {
                ConnectionIds = [ConnectionAId, ConnectionBId],
                Query = new ReleaseSearchRequest { Title = "the salt roads" },
            };

            // Act
            var response = await _subject.SearchAsync(request).ConfigureAwait(false);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(response!.Candidates, Is.Empty);
                Assert.That(response.ConnectionsAttempted, Is.EqualTo(0));
                Assert.That(response.ConnectionsSkipped, Is.EqualTo(2));
            }
        }

        [Test]
        public async Task SearchAsync_KeepsResponse_WhenOneIndexerDoesNotSupportTheQuery()
        {
            // Arrange
            _integrationA.SetupGet(integration => integration.SupportsKeywordSearch).Returns(false);
            _integrationB
                .Setup(integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([new Release { Protocol = "torrent", Title = "Release B", ExternalId = "b1" }]);
            var request = new IndexerSearchRequest
            {
                ConnectionIds = [ConnectionAId, ConnectionBId],
                Query = new ReleaseSearchRequest { Title = "the salt roads" },
            };

            // Act
            var response = await _subject.SearchAsync(request).ConfigureAwait(false);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(response, Is.Not.Null);
                Assert.That(response!.Candidates[0].Release.Title, Is.EqualTo("Release B"));
                Assert.That(response.ConnectionsAttempted, Is.EqualTo(1));
                Assert.That(response.ConnectionsSkipped, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task SearchAsync_KeepsResponse_WhenSelectionIncludesDisabledAndUnsupportedConnections()
        {
            // Arrange
            _integrationA.SetupGet(integration => integration.SupportsKeywordSearch).Returns(false);
            var request = new IndexerSearchRequest
            {
                ConnectionIds = [ConnectionAId, DisabledConnectionId],
                Query = new ReleaseSearchRequest { Title = "the salt roads" },
            };

            // Act
            var response = await _subject.SearchAsync(request).ConfigureAwait(false);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(response, Is.Not.Null);
                Assert.That(response!.ConnectionsAttempted, Is.EqualTo(0));
                Assert.That(response.ConnectionsSkipped, Is.EqualTo(2));
            }
        }

        [Test]
        public async Task SearchAsync_KeepsSuccessfulResults_WhenOneDefinitionIsInvalid()
        {
            // Arrange
            _integrationA
                .SetupGet(integration => integration.SearchFacets)
                .Throws(new InvalidOperationException("Invalid definition."));
            _integrationB
                .Setup(integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([new Release { Protocol = "torrent", Title = "Release B", ExternalId = "b1" }]);
            var request = new IndexerSearchRequest
            {
                ConnectionIds = [ConnectionAId, ConnectionBId],
                Query = new ReleaseSearchRequest { Title = "the salt roads" },
            };

            // Act
            var response = await _subject.SearchAsync(request).ConfigureAwait(false);

            // Assert
            var failed = response!.Connections.Single(connection => connection.ConnectionId == ConnectionAId);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(response.Candidates[0].Release.Title, Is.EqualTo("Release B"));
                Assert.That(response.ConnectionsAttempted, Is.EqualTo(1));
                Assert.That(response.ConnectionsSkipped, Is.EqualTo(0));
                Assert.That(failed.Warning, Is.EqualTo("provider-error:InvalidOperationException"));
            }
        }

        [Test]
        public async Task SearchAsync_ReturnsNoCandidates_WhenTheQueryIsEmpty()
        {
            // Arrange
            var request = new IndexerSearchRequest
            {
                ConnectionIds = [ConnectionAId],
                Query = new ReleaseSearchRequest(),
            };

            // Act
            var response = await _subject.SearchAsync(request).ConfigureAwait(false);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(response!.Candidates, Is.Empty);
                Assert.That(response.ConnectionsAttempted, Is.EqualTo(0));
                Assert.That(response.ConnectionsSkipped, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task SearchAsync_QueriesOnlyTheSelectedConnection_WhenOneConnectionIsSelected()
        {
            // Arrange
            var request = new IndexerSearchRequest
            {
                ConnectionIds = [ConnectionAId],
                Query = new ReleaseSearchRequest { Title = "the salt roads" },
            };

            // Act
            var response = await _subject.SearchAsync(request).ConfigureAwait(false);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(response!.Connections, Has.Count.EqualTo(1));
                Assert.That(response.ConnectionsAttempted, Is.EqualTo(1));
                Assert.That(response.ConnectionsSkipped, Is.EqualTo(0));
            }

            _integrationB.Verify(
                integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task SearchAsync_SkipsTheDisabledConnection_WhenTheSelectionIncludesIt()
        {
            // Arrange
            var request = new IndexerSearchRequest
            {
                ConnectionIds = [ConnectionAId, DisabledConnectionId],
                Query = new ReleaseSearchRequest { Title = "the salt roads" },
            };

            // Act
            var response = await _subject.SearchAsync(request).ConfigureAwait(false);

            // Assert
            var disabled = response!.Connections.Single(connection => connection.ConnectionId == DisabledConnectionId);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(response.Connections, Has.Count.EqualTo(2));
                Assert.That(response.ConnectionsAttempted, Is.EqualTo(1));
                Assert.That(response.ConnectionsSkipped, Is.EqualTo(1));
                Assert.That(disabled.Enabled, Is.False);
                Assert.That(disabled.Attempted, Is.False);
                Assert.That(disabled.SkippedReason, Is.EqualTo("disabled"));
            }
        }

        [Test]
        public async Task SearchAsync_ReturnsNoCandidates_WhenEverySelectedConnectionIsDisabled()
        {
            // Arrange
            var request = new IndexerSearchRequest
            {
                ConnectionIds = [DisabledConnectionId],
                Query = new ReleaseSearchRequest { Title = "the salt roads" },
            };

            // Act
            var response = await _subject.SearchAsync(request).ConfigureAwait(false);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(response, Is.Not.Null);
                Assert.That(response!.Candidates, Is.Empty);
                Assert.That(response.ConnectionsAttempted, Is.EqualTo(0));
                Assert.That(response.ConnectionsSkipped, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task SearchAsync_KeepsTheSuccessfulResults_WhenOneConnectionFails()
        {
            // Arrange
            _integrationA
                .Setup(integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([new Release { Protocol = "torrent", Title = "Release A", ExternalId = "a1" }]);
            _integrationB
                .Setup(integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("The indexer refused the query."));
            var request = new IndexerSearchRequest
            {
                ConnectionIds = [ConnectionAId, ConnectionBId],
                Query = new ReleaseSearchRequest { Title = "the salt roads" },
            };

            // Act
            var response = await _subject.SearchAsync(request).ConfigureAwait(false);

            // Assert
            var failed = response!.Connections.Single(connection => connection.ConnectionId == ConnectionBId);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(response.Candidates, Has.Count.EqualTo(1));
                Assert.That(response.Candidates[0].Release.Title, Is.EqualTo("Release A"));
                Assert.That(failed.Warning, Is.EqualTo("provider-error:InvalidOperationException"));
            }
        }

        [Test]
        public async Task SearchAsync_CoalescesTheRelease_WhenTwoConnectionsReportTheSameIdentityKey()
        {
            // Arrange
            _integrationA
                .Setup(integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new Release
                    {
                        Protocol = "torrent",
                        Title = "The.Salt.Roads.2019.1080p",
                        ExternalId = "a1",
                        IdentityKeys = { new ReleaseIdentityKey { Authority = "infohash", Value = "abc" } },
                    },
                ]);
            _integrationB
                .Setup(integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new Release
                    {
                        Protocol = "torrent",
                        Title = "The.Salt.Roads.2019.1080p",
                        ExternalId = "b1",
                        IdentityKeys = { new ReleaseIdentityKey { Authority = "infohash", Value = "abc" } },
                    },
                ]);
            var request = new IndexerSearchRequest
            {
                ConnectionIds = [ConnectionAId, ConnectionBId],
                Query = new ReleaseSearchRequest { Title = "the salt roads" },
            };

            // Act
            var response = await _subject.SearchAsync(request).ConfigureAwait(false);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(response!.Candidates, Has.Count.EqualTo(1));
                Assert.That(
                    response.Candidates[0].ConnectionIds,
                    Is.EquivalentTo([ConnectionAId, ConnectionBId]));
                Assert.That(response.Candidates[0].Key, Is.EqualTo("[\"identity\",\"infohash\",\"abc\"]"));
            }
        }

        [Test]
        public async Task SearchAsync_ReturnsDistinctKeys_WhenUsenetIndexersReportTheSamePost()
        {
            // Arrange
            var publishDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
            _integrationA
                .Setup(integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new Release
                    {
                        Protocol = "usenet",
                        Title = "Same post",
                        ExternalId = "same-post",
                        PublishDate = publishDate,
                        Size = 1_000,
                    },
                ]);
            _integrationB
                .Setup(integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new Release
                    {
                        Protocol = "usenet",
                        Title = "Same post",
                        ExternalId = "same-post",
                        PublishDate = publishDate,
                        Size = 1_000,
                    },
                ]);
            var request = new IndexerSearchRequest
            {
                ConnectionIds = [ConnectionAId, ConnectionBId],
                Query = new ReleaseSearchRequest { Title = "same post" },
            };

            // Act
            var response = await _subject.SearchAsync(request).ConfigureAwait(false);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(response!.Candidates, Has.Count.EqualTo(2));
                Assert.That(response.Candidates.Select(candidate => candidate.Key), Is.EquivalentTo(
                [
                    "[\"source\",\"11111111-1111-1111-1111-111111111111\",\"same-post\"]",
                    "[\"source\",\"22222222-2222-2222-2222-222222222222\",\"same-post\"]",
                ]));
            }
        }

        [Test]
        public async Task SearchAsync_OrdersCandidates_WhenPublishDatesDiffer()
        {
            // Arrange
            _integrationA
                .Setup(integration => integration.SearchAsync(
                    It.IsAny<ReleaseSearchRequest>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new Release
                    {
                        Protocol = "torrent",
                        Title = "Older",
                        ExternalId = "a1",
                        PublishDate = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                    new Release
                    {
                        Protocol = "torrent",
                        Title = "Newer",
                        ExternalId = "a2",
                        PublishDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                ]);
            var request = new IndexerSearchRequest
            {
                ConnectionIds = [ConnectionAId],
                Query = new ReleaseSearchRequest { Title = "the salt roads" },
            };

            // Act
            var response = await _subject.SearchAsync(request).ConfigureAwait(false);

            // Assert
            Assert.That(response!.Candidates.Select(candidate => candidate.Release.Title), Is.EqualTo(
            [
                "Newer",
                "Older",
            ]));
        }

        [Test]
        public async Task MatchAsync_ReturnsNull_WhenTheContentItemDoesNotExist()
        {
            // Arrange
            var release = new Release { Protocol = "torrent", Title = "The.Salt.Roads.2019.1080p" };

            // Act
            var releaseId = await _subject.MatchAsync(Guid.NewGuid(), release, null).ConfigureAwait(false);

            // Assert
            Assert.That(releaseId, Is.Null);
        }

        [Test]
        public void MatchAsync_ThrowsArgumentException_WhenTitleIsEmpty()
        {
            // Arrange
            var release = new Release { Protocol = "torrent", Title = "" };

            // Act
            AsyncTestDelegate act = async () => await _subject.MatchAsync(ContentHierarchyItemId, release, null)
                .ConfigureAwait(false);

            // Assert
            Assert.ThrowsAsync<ArgumentException>(act);
        }

        [Test]
        public async Task MatchAsync_CreatesTheReleaseAndTheScopeRow_WhenTheReleaseIsNew()
        {
            // Arrange
            var release = new Release { Protocol = "torrent", Title = "The.Salt.Roads.2019.1080p" };

            // Act
            var releaseId = await _subject.MatchAsync(ContentHierarchyItemId, release, null).ConfigureAwait(false);

            // Assert
            Assert.That(releaseId, Is.Not.Null.And.Not.EqualTo(Guid.Empty));
            _releases.Verify(
                repository => repository.CreateAsync(release, null, It.IsAny<CancellationToken>()),
                Times.Once);
            _releaseScopes.Verify(
                repository => repository.CreateAsync(
                    It.Is<ReleaseScope>(scope =>
                        scope.ReleaseId == release.Id && scope.ContentHierarchyItemId == ContentHierarchyItemId),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task MatchAsync_DoesNotCreateTheRelease_WhenTheReleaseAlreadyExists()
        {
            // Arrange
            var existingReleaseId = Guid.NewGuid();
            _releases
                .Setup(repository => repository.UpdateAsync(
                    It.IsAny<Release>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Release model, Guid? _, CancellationToken _) =>
                {
                    model.Id = existingReleaseId;
                    return true;
                });
            var release = new Release { Protocol = "torrent", Title = "The.Salt.Roads.2019.1080p" };

            // Act
            var releaseId = await _subject.MatchAsync(ContentHierarchyItemId, release, null).ConfigureAwait(false);

            // Assert
            Assert.That(releaseId, Is.EqualTo(existingReleaseId));
            _releases.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<Release>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
