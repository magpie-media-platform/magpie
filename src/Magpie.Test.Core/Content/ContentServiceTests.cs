using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Content;
using Magpie.Models.Api;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class ContentServiceTests
{
    [Test]
    public async Task AttachFileAsync_RollsUpFulfillmentInSameTransaction_WhenFulfillmentChanges()
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var inAttachTransaction = false;
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
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
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
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
        Assert.That(result, Is.True);
        transactionManager.Verify(manager => manager.ExecuteWithTransactionAsync(
            It.IsAny<Func<CancellationToken, Task<bool>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
