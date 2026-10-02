using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Content;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Content;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class ContentServiceTests
{
    [TestCase(false, 1, 0)]
    [TestCase(true, 0, 1)]
    public async Task DeleteAsync_CompletesCleanup_WhenTheRequestIsCancelledAfterTheRowDelete(
        bool bulk, int singleDeleteCalls, int bulkDeleteCalls)
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var contentId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var item = new ContentHierarchyItem
        {
            Id = contentId,
            ContentType = "movies",
            Level = "movie",
            Slug = "movie",
            Name = "Movie",
            ContentItem = new() { Id = Guid.NewGuid(), ContentHierarchyItemId = contentId }
        };
        var download = new Download { ExternalId = "download", Title = "Movie", DownloadClientId = clientId };
        download.Scope.Add(new() { ContentHierarchyItemId = contentId });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems.Setup(repository => repository.GetByIdAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems.Setup(repository => repository.DeleteSubtreeAsync(
                contentId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback(cancellation.Cancel)
            .ReturnsAsync([contentId]);
        hierarchyItems.Setup(repository => repository.BulkDeleteAsync(
                It.IsAny<BulkAction<ContentHierarchyItemCriteria>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback(cancellation.Cancel)
            .ReturnsAsync((new BulkResult { Count = 1 }, (IReadOnlyList<Guid>)[contentId]));
        var downloads = new Mock<IDownloadRepository> { CallBase = true };
        downloads.Setup(repository => repository.ListClientResidentByContentItemsAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([download]);
        var clients = new Mock<IDownloadClientRepository> { CallBase = true };
        clients.Setup(repository => repository.GetByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadClient { Id = clientId, Name = "Client", ProviderType = "test" });
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(provider => provider.ProviderType).Returns("test");
        var contentFiles = new Mock<IContentFileService>();
        var subject = new ContentService(
            hierarchyItems.Object, Mock.Of<IContentRepository>(), Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(), Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(), [], downloads.Object, clients.Object, [integration.Object],
            Mock.Of<ITransactionManager>(), Mock.Of<IContentTypeRegistry>(), contentFiles.Object,
            TimeProvider.System, NullLogger<ContentService>.Instance);

        // Act
        if (bulk)
        {
            await subject.BulkDeleteAsync(new() { Ids = [contentId] }, true, null, cancellation.Token)
                .ConfigureAwait(false);
        }
        else
        {
            await subject.DeleteAsync(contentId, true, null, cancellation.Token).ConfigureAwait(false);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            hierarchyItems.Verify(repository => repository.DeleteSubtreeAsync(
                contentId, null, CancellationToken.None), Times.Exactly(singleDeleteCalls));
            hierarchyItems.Verify(repository => repository.BulkDeleteAsync(
                It.IsAny<BulkAction<ContentHierarchyItemCriteria>>(), null, CancellationToken.None),
                Times.Exactly(bulkDeleteCalls));
            clients.Verify(repository => repository.GetByIdAsync(clientId, CancellationToken.None), Times.Once);
            integration.Verify(provider => provider.RemoveDownloadAsync(
                "download", true, It.IsAny<IReadOnlyDictionary<string, string>>(), CancellationToken.None), Times.Once);
            contentFiles.Verify(service => service.DeleteFilesAsync(
                It.Is<IReadOnlyCollection<ContentHierarchyItem>>(items => items.Count == 1), CancellationToken.None),
                Times.Once);
        }
    }

    [Test]
    public async Task BulkDeleteAsync_KeepsSurvivingDownloads_WhenTheFilterChangesOrADownloadIsShared()
    {
        // Arrange
        var removedId = Guid.NewGuid();
        var survivingId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var removed = new ContentHierarchyItem
        {
            Id = removedId,
            ContentType = "movies",
            Level = "movie",
            Slug = "removed",
            Name = "Removed",
            ContentItem = new() { Id = Guid.NewGuid(), ContentHierarchyItemId = removedId }
        };
        var surviving = new ContentHierarchyItem
        {
            Id = survivingId,
            ContentType = "movies",
            Level = "movie",
            Slug = "surviving",
            Name = "Surviving",
            ContentItem = new() { Id = Guid.NewGuid(), ContentHierarchyItemId = survivingId }
        };
        var removedDownload = new Download { ExternalId = "removed", Title = "Removed", DownloadClientId = clientId };
        removedDownload.Scope.Add(new() { ContentHierarchyItemId = removedId });
        var survivingDownload = new Download
        {
            ExternalId = "surviving",
            Title = "Surviving",
            DownloadClientId = clientId,
        };
        survivingDownload.Scope.Add(new() { ContentHierarchyItemId = survivingId });
        var sharedDownload = new Download { ExternalId = "shared", Title = "Shared", DownloadClientId = clientId };
        sharedDownload.Scope.Add(new() { ContentHierarchyItemId = removedId });
        sharedDownload.Scope.Add(new() { ContentHierarchyItemId = survivingId });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems.Setup(repository => repository.GetByIdAsync(removedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(removed);
        hierarchyItems.Setup(repository => repository.GetByIdAsync(survivingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(surviving);
        hierarchyItems.Setup(repository => repository.BulkDeleteAsync(
                It.IsAny<BulkAction<ContentHierarchyItemCriteria>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new BulkResult { Count = 1 }, (IReadOnlyList<Guid>)[removedId]));
        var downloads = new Mock<IDownloadRepository> { CallBase = true };
        downloads.Setup(repository => repository.ListClientResidentByContentItemsAsync(
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([removedDownload, survivingDownload, sharedDownload]);
        var clients = new Mock<IDownloadClientRepository> { CallBase = true };
        clients.Setup(repository => repository.GetByIdAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadClient { Id = clientId, Name = "Client", ProviderType = "test" });
        var integration = new Mock<IDownloadClientIntegration>();
        integration.SetupGet(provider => provider.ProviderType).Returns("test");
        var contentFiles = new Mock<IContentFileService>();
        var subject = new ContentService(
            hierarchyItems.Object, Mock.Of<IContentRepository>(), Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(), Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(), [], downloads.Object, clients.Object, [integration.Object],
            Mock.Of<ITransactionManager>(), Mock.Of<IContentTypeRegistry>(), contentFiles.Object,
            TimeProvider.System, NullLogger<ContentService>.Instance);

        // Act
        await subject.BulkDeleteAsync(new() { Ids = [removedId, survivingId] }, true, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            integration.Verify(provider => provider.RemoveDownloadAsync(
                "removed", true, It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
            integration.Verify(provider => provider.RemoveDownloadAsync(
                It.IsAny<string>(), true, It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
            contentFiles.Verify(service => service.DeleteFilesAsync(
                It.Is<IReadOnlyList<ContentHierarchyItem>>(items => items.Count == 1 && items[0].Id == removedId),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task BulkDeleteAsync_DeletesFiles_WhenDeleteFromLibrary(bool deleteFromLibrary, int deleteFileCalls)
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var contentItemId = Guid.NewGuid();
        var item = new ContentHierarchyItem
        {
            Id = contentId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "movies",
            Level = "movie",
            Slug = "movie",
            Name = "Movie",
            CreatedAt = DateTime.UtcNow,
            ContentItem = new()
            {
                Id = contentItemId,
                ContentHierarchyItemId = contentId,
                CreatedAt = DateTime.UtcNow
            }
        };
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems.Setup(repository => repository.GetByIdAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems.Setup(repository => repository.BulkDeleteAsync(
                It.IsAny<BulkAction<ContentHierarchyItemCriteria>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new BulkResult { Count = 1 }, (IReadOnlyList<Guid>)[contentId]));
        var contentFiles = new Mock<IContentFileService>();
        var downloads = new Mock<IDownloadRepository> { CallBase = true };
        downloads.Setup(repository => repository.ListClientResidentByContentItemsAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var subject = new ContentService(
            hierarchyItems.Object,
            Mock.Of<IContentRepository>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [],
            downloads.Object,
            Mock.Of<IDownloadClientRepository>(),
            [],
            Mock.Of<ITransactionManager>(),
            Mock.Of<IContentTypeRegistry>(),
            contentFiles.Object,
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        var result = await subject.BulkDeleteAsync(
            new() { Ids = [contentId] },
            deleteFromLibrary,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(1));
            contentFiles.Verify(service => service.DeleteFilesAsync(
                It.Is<IReadOnlyList<ContentHierarchyItem>>(items => items.Count == 1 && items[0].Id == contentId),
                It.IsAny<CancellationToken>()), Times.Exactly(deleteFileCalls));
            downloads.Verify(repository => repository.ListClientResidentByContentItemsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == contentItemId),
                It.IsAny<CancellationToken>()), Times.Exactly(deleteFileCalls));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesTheFilesOfEveryPage_WhenAPageIsShorterThanThePageSize()
    {
        // Arrange
        var rootId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var root = new ContentHierarchyItem
        {
            Id = rootId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "tv",
            Level = "series",
            Slug = "series",
            Name = "Series",
            CreatedAt = DateTime.UtcNow
        };
        var first = new ContentHierarchyItem
        {
            Id = firstId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "tv",
            Level = "episode",
            Slug = "first",
            Name = "First",
            CreatedAt = DateTime.UtcNow
        };
        var second = new ContentHierarchyItem
        {
            Id = secondId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "tv",
            Level = "episode",
            Slug = "second",
            Name = "Second",
            CreatedAt = DateTime.UtcNow
        };
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems.Setup(repository => repository.GetByIdAsync(rootId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(root);
        hierarchyItems.SetupSequence(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [first], TotalItems = 501 })
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [second], TotalItems = 501 });
        hierarchyItems.Setup(repository => repository.DeleteSubtreeAsync(
                rootId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([rootId, firstId, secondId]);
        var contentFiles = new Mock<IContentFileService>();
        var subject = new ContentService(
            hierarchyItems.Object,
            Mock.Of<IContentRepository>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [],
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(),
            [],
            Mock.Of<ITransactionManager>(),
            Mock.Of<IContentTypeRegistry>(),
            contentFiles.Object,
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        var deleted = await subject.DeleteAsync(rootId, true, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            contentFiles.Verify(service => service.DeleteFilesAsync(
                It.Is<IReadOnlyList<ContentHierarchyItem>>(items => items.Count == 3),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task BulkDeleteAsync_DeletesTheFilesOfTheRemovedRowsOnly_WhenTheDeleteRemovesFewerRows()
    {
        // Arrange
        var removedId = Guid.NewGuid();
        var keptId = Guid.NewGuid();
        var removed = new ContentHierarchyItem
        {
            Id = removedId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "movies",
            Level = "movie",
            Slug = "removed",
            Name = "Removed",
            CreatedAt = DateTime.UtcNow
        };
        var kept = new ContentHierarchyItem
        {
            Id = keptId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "movies",
            Level = "movie",
            Slug = "kept",
            Name = "Kept",
            CreatedAt = DateTime.UtcNow
        };
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems.Setup(repository => repository.GetByIdAsync(removedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(removed);
        hierarchyItems.Setup(repository => repository.GetByIdAsync(keptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(kept);
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems.Setup(repository => repository.BulkDeleteAsync(
                It.IsAny<BulkAction<ContentHierarchyItemCriteria>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new BulkResult { Count = 1 }, (IReadOnlyList<Guid>)[removedId]));
        var contentFiles = new Mock<IContentFileService>();
        var subject = new ContentService(
            hierarchyItems.Object,
            Mock.Of<IContentRepository>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [],
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(),
            [],
            Mock.Of<ITransactionManager>(),
            Mock.Of<IContentTypeRegistry>(),
            contentFiles.Object,
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        var result = await subject.BulkDeleteAsync(
            new() { Ids = [removedId, keptId] },
            true,
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(1));
            contentFiles.Verify(service => service.DeleteFilesAsync(
                It.Is<IReadOnlyList<ContentHierarchyItem>>(items =>
                    items.Count == 1 && items[0].Id == removedId),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public void BulkDeleteAsync_KeepsTheFiles_WhenTheRowDeleteFails()
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var item = new ContentHierarchyItem
        {
            Id = contentId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "movies",
            Level = "movie",
            Slug = "movie",
            Name = "Movie",
            CreatedAt = DateTime.UtcNow,
            ContentItem = new()
            {
                Id = Guid.NewGuid(),
                ContentHierarchyItemId = contentId,
                CreatedAt = DateTime.UtcNow
            }
        };
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems.Setup(repository => repository.GetByIdAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems.Setup(repository => repository.BulkDeleteAsync(
                It.IsAny<BulkAction<ContentHierarchyItemCriteria>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The bulk delete failed."));
        var contentFiles = new Mock<IContentFileService>();
        var downloads = new Mock<IDownloadRepository> { CallBase = true };
        downloads.Setup(repository => repository.ListClientResidentByContentItemsAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var subject = new ContentService(
            hierarchyItems.Object,
            Mock.Of<IContentRepository>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [],
            downloads.Object,
            Mock.Of<IDownloadClientRepository>(),
            [],
            Mock.Of<ITransactionManager>(),
            Mock.Of<IContentTypeRegistry>(),
            contentFiles.Object,
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => subject.BulkDeleteAsync(new() { Ids = [contentId] }, true, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("The bulk delete failed."));
            contentFiles.Verify(service => service.DeleteFilesAsync(
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task DeleteAsync_RemovesDownloadsFromTheClient_WhenDeletingFromLibrary(
        bool deleteFromLibrary,
        int clientRemovalCalls)
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var contentItemId = Guid.NewGuid();
        var item = new ContentHierarchyItem
        {
            Id = contentId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "movies",
            Level = "movie",
            Slug = "movie",
            Name = "Movie",
            CreatedAt = DateTime.UtcNow,
            ContentItem = new()
            {
                Id = contentItemId,
                ContentHierarchyItemId = contentId,
                CreatedAt = DateTime.UtcNow
            }
        };
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems.Setup(repository => repository.GetByIdAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems.Setup(repository => repository.DeleteSubtreeAsync(
                contentId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([contentId]);
        var contentFiles = new Mock<IContentFileService>();
        var downloads = new Mock<IDownloadRepository> { CallBase = true };
        downloads.Setup(repository => repository.ListClientResidentByContentItemsAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var subject = new ContentService(
            hierarchyItems.Object,
            Mock.Of<IContentRepository>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [],
            downloads.Object,
            Mock.Of<IDownloadClientRepository>(),
            [],
            Mock.Of<ITransactionManager>(),
            Mock.Of<IContentTypeRegistry>(),
            contentFiles.Object,
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        var result = await subject.DeleteAsync(contentId, deleteFromLibrary, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            downloads.Verify(repository => repository.ListClientResidentByContentItemsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == contentItemId),
                It.IsAny<CancellationToken>()), Times.Exactly(clientRemovalCalls));
            contentFiles.Verify(service => service.DeleteFilesAsync(
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<CancellationToken>()), Times.Exactly(clientRemovalCalls));
        }
    }

    [Test]
    public async Task DeleteAsync_KeepsTheFiles_WhenTheRowDeleteFails()
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var item = new ContentHierarchyItem
        {
            Id = contentId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "movies",
            Level = "movie",
            Slug = "movie",
            Name = "Movie",
            CreatedAt = DateTime.UtcNow,
            ContentItem = new()
            {
                Id = Guid.NewGuid(),
                ContentHierarchyItemId = contentId,
                CreatedAt = DateTime.UtcNow
            }
        };
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems.Setup(repository => repository.GetByIdAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems.Setup(repository => repository.DeleteSubtreeAsync(
                contentId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var contentFiles = new Mock<IContentFileService>();
        var downloads = new Mock<IDownloadRepository> { CallBase = true };
        downloads.Setup(repository => repository.ListClientResidentByContentItemsAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var subject = new ContentService(
            hierarchyItems.Object,
            Mock.Of<IContentRepository>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [],
            downloads.Object,
            Mock.Of<IDownloadClientRepository>(),
            [],
            Mock.Of<ITransactionManager>(),
            Mock.Of<IContentTypeRegistry>(),
            contentFiles.Object,
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        var result = await subject.DeleteAsync(contentId, true, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            contentFiles.Verify(service => service.DeleteFilesAsync(
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task AttachFileAsync_RollsUpFulfillmentInSameTransaction_WhenFulfillmentChanges()
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var inAttachTransaction = false;
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems.Setup(repository => repository.GetLineageAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Assert.That(inAttachTransaction, Is.True);
                return [];
            });
        var contentFiles = new Mock<IContentFileService>();
        contentFiles.Setup(service => service.AttachAsync(
                contentId,
                "movie.mkv",
                It.IsAny<Guid?>(),
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                Guid _,
                string _,
                Guid? _,
                Func<CancellationToken, Task> materializeFulfillmentAsync,
                CancellationToken cancellationToken) =>
            {
                inAttachTransaction = true;
                try
                {
                    await materializeFulfillmentAsync(cancellationToken).ConfigureAwait(false);
                    return new AttachContentFileResult { FulfillmentChanged = true };
                }
                finally
                {
                    inAttachTransaction = false;
                }
            });
        var transactionManager = new Mock<ITransactionManager>();
        var subject = new ContentService(
            hierarchyItems.Object,
            Mock.Of<IContentRepository>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [],
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(),
            [],
            transactionManager.Object,
            Mock.Of<IContentTypeRegistry>(),
            contentFiles.Object,
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        await subject.AttachFileAsync(contentId, "movie.mkv", null).ConfigureAwait(false);

        // Assert
        contentFiles.Verify(service => service.AttachAsync(
            contentId,
            "movie.mkv",
            It.IsAny<Guid?>(),
            It.IsAny<Func<CancellationToken, Task>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task UnlinkFileAsync_RollsUpFulfillmentInSameTransaction_WhenFulfillmentChanges()
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var mediaFileId = Guid.NewGuid();
        var inTransaction = false;
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems.Setup(repository => repository.GetLineageAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Assert.That(inTransaction, Is.True);
                return [];
            });
        var contentFiles = new Mock<IContentFileService>();
        contentFiles.Setup(service => service.UnlinkAsync(
                contentId,
                mediaFileId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Assert.That(inTransaction, Is.True);
                return new UnlinkContentFileResult { Removed = true, FulfillmentChanged = true };
            });
        var transactionManager = new Mock<ITransactionManager>();
        transactionManager.Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task<bool>> operation, CancellationToken cancellationToken) =>
            {
                inTransaction = true;
                try
                {
                    return await operation(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    inTransaction = false;
                }
            });
        var subject = new ContentService(
            hierarchyItems.Object,
            Mock.Of<IContentRepository>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [],
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(),
            [],
            transactionManager.Object,
            Mock.Of<IContentTypeRegistry>(),
            contentFiles.Object,
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        var result = await subject.UnlinkFileAsync(contentId, mediaFileId, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            transactionManager.Verify(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task GetAsync_ReturnsItemWithEffectivePath_WhenItemExists()
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var item = new ContentHierarchyItem
        {
            Id = contentId,
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "movies",
            Level = "movie",
            Slug = "example-movie-2025",
            Name = "Example Movie",
            Path = "Example Movie (2025)",
            CreatedAt = DateTime.UtcNow
        };
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems.Setup(repository => repository.GetByIdAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        var contentFiles = new Mock<IContentFileService>();
        contentFiles.Setup(service => service.GetEffectivePathAsync(contentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(@"\\media\Movies\Example Movie (2025)");
        var subject = new ContentService(
            hierarchyItems.Object,
            Mock.Of<IContentRepository>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [],
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(),
            [],
            Mock.Of<ITransactionManager>(),
            Mock.Of<IContentTypeRegistry>(),
            contentFiles.Object,
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        var result = await subject.GetAsync(contentId).ConfigureAwait(false);

        // Assert
        Assert.That(result!.Path, Is.EqualTo(@"\\media\Movies\Example Movie (2025)"));
    }

    [Test]
    public async Task
        HydrateWithDescendantsFromExternalIdAsync_CreatesTheWholeTree_WhenChildrenAreNested()
    {
        // Arrange
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetRootLevelSlug("tv", null)).Returns("show");
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("tv", "show")).Returns(
        [
            new ContentHierarchyLevelDefinition
            {
                Slug = "season", ParentSlug = "show", DisplayName = "Season", CustomMetadataFields = []
            }
        ]);
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("tv", "season")).Returns(
        [
            new ContentHierarchyLevelDefinition
            {
                Slug = "episode", ParentSlug = "season", DisplayName = "Episode", CustomMetadataFields = []
            }
        ]);

        var provider = new Mock<IContentHierarchyMetadataProvider>();
        provider.Setup(p => p.ProviderType).Returns("provider-a");
        provider.Setup(p => p.SupportedContentTypes).Returns(["tv"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = "provider-a", Title = "Bluey" },
                Children =
                [
                    new ChildHierarchyDefinition
                    {
                        Level = "season",
                        Name = "Season 1",
                        Slug = "season-1",
                        Ordinal = 1,
                        AlignmentKey = "season:1",
                        InitialContribution = new MetadataContribution
                        {
                            ProviderType = "provider-a", Title = "Season 1"
                        },
                        Children =
                        [
                            new ChildHierarchyDefinition
                            {
                                Level = "episode",
                                Name = "Magic Xylophone",
                                Slug = "s01e01",
                                Ordinal = 1,
                                AlignmentKey = "episode:1:1",
                                InitialContribution = new MetadataContribution
                                {
                                    ProviderType = "provider-a", Title = "Magic Xylophone"
                                }
                            }
                        ]
                    }
                ]
            });

        var createdChildren = new List<ContentHierarchyItem>();
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<ContentHierarchyItem, Guid?, CancellationToken>((item, _, _) => createdChildren.Add(item))
            .ReturnsAsync(true);
        hierarchyItems
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>(
                (id, _) => Task.FromResult(createdChildren.FirstOrDefault(item => item.Id == id)));

        var containerRepository = new Mock<IMetadataContainerRepository> { CallBase = true };
        containerRepository
            .Setup(repository => repository.AddContributionAsync(
                It.IsAny<Guid>(), It.IsAny<MetadataContribution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, MetadataContribution contribution, Guid? _, CancellationToken _) => contribution);

        var contentRepository = new Mock<IContentRepository> { CallBase = true };
        contentRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem>());
        contentRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<ContentItem>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentItem item, Guid? _, CancellationToken _) => item);

        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken token) => operation(token));

        var subject = new ContentService(
            hierarchyItems.Object,
            contentRepository.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            containerRepository.Object,
            [provider.Object],
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(),
            [],
            transactionManager.Object,
            contentTypeRegistry.Object,
            Mock.Of<IContentFileService>(),
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        var result = await subject.HydrateWithDescendantsFromExternalIdAsync(
            "provider-a", "provider-a", "123", "tv", null, null, false, null, null, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(
                result!.Value.CreatedHierarchyItemIds,
                Is.EquivalentTo(createdChildren.Select(item => item.Id)));
            Assert.That(createdChildren, Has.Count.EqualTo(3));
            Assert.That(createdChildren.All(item => item.Monitoring == MonitoringMode.Unmonitored), Is.True);
            Assert.That(createdChildren.Any(item => item.Level == "show" && item.ParentId == null), Is.True);
            var season = createdChildren.Single(item => item.Level == "season");
            Assert.That(season.Name, Is.EqualTo("Season 1"));
            Assert.That(season.AlignmentKey, Is.EqualTo("season:1"));
            var episode = createdChildren.Single(item => item.Level == "episode");
            Assert.That(episode.ParentId, Is.EqualTo(season.Id));
            Assert.That(episode.Name, Is.EqualTo("Magic Xylophone"));
            Assert.That(episode.AlignmentKey, Is.EqualTo("episode:1:1"));
        }
    }

    [Test]
    public async Task
        HydrateWithDescendantsFromExternalIdAsync_ReturnsTheExistingRoot_WhenTheStructureExists()
    {
        // Arrange
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetRootLevelSlug("tv", null)).Returns("show");
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("tv", "show")).Returns(
        [
            new ContentHierarchyLevelDefinition
            {
                Slug = "season", ParentSlug = "show", DisplayName = "Season", CustomMetadataFields = []
            }
        ]);

        var existingRootId = Guid.NewGuid();
        var existingContainerId = Guid.NewGuid();
        var existingRoot = new ContentHierarchyItem
        {
            Id = existingRootId,
            MetadataContainerId = existingContainerId,
            MetadataContainer = new MetadataContainer { Id = existingContainerId },
            ParentId = null,
            ContentType = "tv",
            Level = "show",
            Name = "Bluey",
            Slug = "provider-a-123"
        };
        var existingSeason = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = Guid.NewGuid(),
            ParentId = existingRootId,
            ContentType = "tv",
            Level = "season",
            Name = "Season 1",
            Slug = "season-1"
        };

        var provider = new Mock<IContentHierarchyMetadataProvider>();
        provider.Setup(p => p.ProviderType).Returns("provider-a");
        provider.Setup(p => p.SupportedContentTypes).Returns(["tv"]);

        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => !criteria.ParentId.And.Any()),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [existingRoot], TotalItems = 1 });
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.ParentId.And.Any()),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [existingSeason], TotalItems = 1 });

        var subject = new ContentService(
            hierarchyItems.Object,
            Mock.Of<IContentRepository>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [provider.Object],
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(),
            [],
            Mock.Of<ITransactionManager>(),
            contentTypeRegistry.Object,
            Mock.Of<IContentFileService>(),
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        var result = await subject.HydrateWithDescendantsFromExternalIdAsync(
            "provider-a", "provider-a", "123", "tv", null, null, null, null, null, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result?.Root.Id, Is.EqualTo(existingRootId));
            Assert.That(result?.CreatedHierarchyItemIds, Is.Empty);
            provider.Verify(
                p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
            hierarchyItems.Verify(
                repository => repository.TryCreateAsync(
                    It.IsAny<ContentHierarchyItem>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task
        HydrateWithDescendantsFromExternalIdAsync_HydratesTheRootOnly_WhenNoChildLevelsExist()
    {
        // Arrange
        var contentTypeRegistry = new Mock<IContentTypeRegistry>();
        contentTypeRegistry.Setup(registry => registry.GetRootLevelSlug("movie", null)).Returns("movie");
        contentTypeRegistry.Setup(registry => registry.GetChildLevels("movie", "movie")).Returns([]);

        var provider = new Mock<IContentHierarchyMetadataProvider>();
        provider.Setup(p => p.ProviderType).Returns("provider-b");
        provider.Setup(p => p.SupportedContentTypes).Returns(["movie"]);
        provider
            .Setup(p => p.TryGetMetadataAsync(It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = "provider-b", Title = "The Matrix" }
            });

        var createdItems = new List<ContentHierarchyItem>();
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchyItems
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        hierarchyItems
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<ContentHierarchyItem, Guid?, CancellationToken>((item, _, _) => createdItems.Add(item))
            .ReturnsAsync(true);
        hierarchyItems
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>(
                (id, _) => Task.FromResult(createdItems.FirstOrDefault(item => item.Id == id)));

        var containerRepository = new Mock<IMetadataContainerRepository> { CallBase = true };
        containerRepository
            .Setup(repository => repository.AddContributionAsync(
                It.IsAny<Guid>(), It.IsAny<MetadataContribution>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, MetadataContribution contribution, Guid? _, CancellationToken _) => contribution);

        var contentRepository = new Mock<IContentRepository> { CallBase = true };
        contentRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentItemQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentItem>());
        contentRepository
            .Setup(repository => repository.CreateAsync(
                It.IsAny<ContentItem>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentItem item, Guid? _, CancellationToken _) => item);

        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Guid>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Guid>> operation, CancellationToken token) => operation(token));

        var subject = new ContentService(
            hierarchyItems.Object,
            contentRepository.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            containerRepository.Object,
            [provider.Object],
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(),
            [],
            transactionManager.Object,
            contentTypeRegistry.Object,
            Mock.Of<IContentFileService>(),
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        var result = await subject.HydrateWithDescendantsFromExternalIdAsync(
            "provider-b", "provider-b", "603", "movie", null, null, null, null, null, null, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(createdItems, Has.Count.EqualTo(1));
            Assert.That(createdItems[0].Level, Is.EqualTo("movie"));
        }
    }

    [Test]
    public async Task MaterializeFulfillmentStatusAsync_ReconcilesStoredMedia_WhenRequested()
    {
        // Arrange
        var leafId = Guid.NewGuid();
        var leaf = new ContentHierarchyItem
        {
            Id = leafId,
            ContentType = "movies",
            Level = "movie",
            Slug = "leaf",
            Name = "Leaf"
        };
        var hierarchy = new Mock<IContentHierarchyItemRepository>();
        hierarchy.Setup(repository => repository.GetByIdAsync(leafId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaf);
        var content = new Mock<IContentRepository>();
        var subject = new ContentService(
            hierarchy.Object, content.Object, Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(), Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(), [], Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(), [], Mock.Of<ITransactionManager>(),
            Mock.Of<IContentTypeRegistry>(), Mock.Of<IContentFileService>(),
            TimeProvider.System, NullLogger<ContentService>.Instance);

        // Act
        var result = await subject.MaterializeFulfillmentStatusAsync(leafId, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            content.Verify(repository => repository.ReconcileMediaStateAsync(
                leafId, null, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [TestCaseSource(nameof(UnrepresentableFilters))]
    public void BulkDeleteAsync_ThrowsWithoutReachingTheRepository_WhenTheFilterIsNotRepresentable(
        ContentFilterCriteria filter)
    {
        // Arrange
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        var subject = new ContentService(
            hierarchyItems.Object,
            Mock.Of<IContentRepository>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [],
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(),
            [],
            Mock.Of<ITransactionManager>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IContentFileService>(),
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        var act = () => subject.BulkDeleteAsync(new() { Filter = filter }, true, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(act, Throws.ArgumentException);
            hierarchyItems.Verify(
                repository => repository.BulkDeleteAsync(
                    It.IsAny<BulkAction<ContentHierarchyItemCriteria>>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCaseSource(nameof(UnrepresentableFilters))]
    public void QueryAsync_ThrowsWithoutReachingTheRepository_WhenTheFilterIsNotRepresentable(
        ContentFilterCriteria filter)
    {
        // Arrange
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        var subject = new ContentService(
            hierarchyItems.Object,
            Mock.Of<IContentRepository>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [],
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(),
            [],
            Mock.Of<ITransactionManager>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IContentFileService>(),
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        var act = () => subject.QueryAsync(new() { Criteria = filter }, new() { PageNumber = 1, PageSize = 10 }, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(act, Throws.ArgumentException);
            hierarchyItems.Verify(
                repository => repository.QueryAsync(
                    It.IsAny<ContentHierarchyItemCriteria>(),
                    It.IsAny<PagingParameter>(),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task QueryAsync_PreservesFilterConstraints_WhenQueryConstraintsAreAdded()
    {
        // Arrange
        var contentItemId = Guid.NewGuid();
        var hierarchyItemId = Guid.NewGuid();
        var rootFolderId = Guid.NewGuid();
        var filter = new ContentHierarchyItemCriteria
        {
            ContentItemId = contentItemId,
            LastRefreshedBefore = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            HasMonitoredParent = false,
            Ids = [hierarchyItemId],
            EffectiveRootFolderId = rootFolderId,
            Name = new()
            {
                And = [new() { Operator = CriteriaValueOperator.ContainsString, Value = "first" }],
            },
        };
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        var received = new ContentHierarchyItemCriteria();
        hierarchyItems.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(), null,
                It.IsAny<CancellationToken>()))
            .Callback((
                ContentHierarchyItemCriteria criteria, PagingParameter _, SortParameter? _, CancellationToken _) =>
                    received = criteria)
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var subject = new ContentService(
            hierarchyItems.Object,
            Mock.Of<IContentRepository>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [],
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(),
            [],
            Mock.Of<ITransactionManager>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IContentFileService>(),
            TimeProvider.System,
            NullLogger<ContentService>.Instance);

        // Act
        await subject.QueryAsync(
            new()
            {
                Criteria = new() { ContentHierarchyItem = new() { And = [filter] } },
                RootFolderId = rootFolderId,
                Search = "second",
            },
            new() { PageNumber = 1, PageSize = 10 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(received.ContentItemId, Is.EqualTo(contentItemId));
            Assert.That(received.LastRefreshedBefore, Is.EqualTo(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(received.HasMonitoredParent, Is.False);
            Assert.That(received.Ids, Is.EqualTo([hierarchyItemId]));
            Assert.That(received.EffectiveRootFolderId, Is.EqualTo(rootFolderId));
            Assert.That(
                received.Name.And.Select(criterion => criterion.Value), Is.EqualTo(["first", "second"]));
            Assert.That(received.Name.And.Select(criterion => criterion.Operator), Is.EqualTo(
            [
                CriteriaValueOperator.ContainsString, CriteriaValueOperator.ContainsString,
            ]));
            Assert.That(filter.Name.And.Select(criterion => criterion.Value), Is.EqualTo(["first"]));
        }
    }

    [Test]
    public void QueryAsync_ThrowsWithoutReachingTheRepository_WhenRootFolderConstraintsConflict()
    {
        // Arrange
        var hierarchyItems = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        var subject = new ContentService(
            hierarchyItems.Object,
            Mock.Of<IContentRepository>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IRootFolderRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            Mock.Of<IMetadataContainerRepository>(),
            [],
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IDownloadClientRepository>(),
            [],
            Mock.Of<ITransactionManager>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IContentFileService>(),
            TimeProvider.System,
            NullLogger<ContentService>.Instance);
        var query = new ContentQuery
        {
            RootFolderId = Guid.NewGuid(),
            Criteria = new()
            {
                ContentHierarchyItem = new() { And = [new() { EffectiveRootFolderId = Guid.NewGuid() }] },
            },
        };

        // Act
        var act = () => subject.QueryAsync(query, new() { PageNumber = 1, PageSize = 10 }, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(act, Throws.ArgumentException);
            hierarchyItems.Verify(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(), It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    private static IEnumerable<TestCaseData> UnrepresentableFilters()
    {
        yield return new TestCaseData(new ContentFilterCriteria()).SetName("EmptyAnd");
        yield return new TestCaseData(new ContentFilterCriteria
        {
            ContentHierarchyItem = new() { And = [new ContentHierarchyItemCriteria()] },
            MediaFiles = new() { And = [new MediaFileCriteria()] },
        }).SetName("MediaFileCriteria");
        yield return new TestCaseData(new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And = [new ContentHierarchyItemCriteria(), new ContentHierarchyItemCriteria()],
            },
        }).SetName("MultipleAndEntries");
    }
}
