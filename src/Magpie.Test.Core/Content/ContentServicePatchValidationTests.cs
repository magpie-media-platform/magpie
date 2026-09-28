using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Content;
using Magpie.Models.Arguments;
using Magpie.Models.Bulk;
using Magpie.Models.Data;
using Magpie.Models.Patch;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class ContentServicePatchValidationTests
{
    private static readonly Guid MissingReferenceId = new("11111111-1111-1111-1111-111111111111");

    private Mock<IContentHierarchyItemRepository> _hierarchyItems = null!;
    private ContentService _subject = null!;

    public static IEnumerable<TestCaseData> RejectedPatches()
    {
        yield return new TestCaseData(
            new ContentHierarchyItemPatch { Slug = new ScalarPatch<string> { Value = "stolen-slug" } },
            typeof(ArgumentException),
            "Content slug changes are not supported. (Parameter 'patch')");
        yield return new TestCaseData(
            new ContentHierarchyItemPatch { RootFolderId = new ScalarPatch<Guid?> { Value = MissingReferenceId } },
            typeof(MagpieException),
            "Root folder 11111111-1111-1111-1111-111111111111 does not exist.");
        yield return new TestCaseData(
            new ContentHierarchyItemPatch
            {
                QualityTierListId = new ScalarPatch<Guid?> { Value = MissingReferenceId }
            },
            typeof(MagpieException),
            "Quality tier list 11111111-1111-1111-1111-111111111111 does not exist.");
    }

    public static IEnumerable<TestCaseData> RejectedBulkPatches()
    {
        yield return new TestCaseData(
            new ContentHierarchyItemPatch { Slug = new ScalarPatch<string> { Value = "stolen-slug" } },
            typeof(ArgumentException),
            "Bulk content slug changes are not supported. (Parameter 'patch')");
        yield return new TestCaseData(
            new ContentHierarchyItemPatch { RootFolderId = new ScalarPatch<Guid?> { Value = MissingReferenceId } },
            typeof(MagpieException),
            "Root folder 11111111-1111-1111-1111-111111111111 does not exist.");
        yield return new TestCaseData(
            new ContentHierarchyItemPatch
            {
                QualityTierListId = new ScalarPatch<Guid?> { Value = MissingReferenceId }
            },
            typeof(MagpieException),
            "Quality tier list 11111111-1111-1111-1111-111111111111 does not exist.");
    }

    [SetUp]
    public void SetUp()
    {
        _hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        _subject = new ContentService(
            _hierarchyItems.Object,
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
    }

    [TestCaseSource(nameof(RejectedPatches))]
    public void PatchAsync_ThrowsAndSkipsTheRepository_WhenThePatchIsRejected(
        ContentHierarchyItemPatch patch,
        Type expectedException,
        string expectedMessage)
    {
        // Act
        var exception = Assert.ThrowsAsync(
            expectedException,
            () => _subject.PatchAsync(Guid.NewGuid(), patch, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo(expectedMessage));
            _hierarchyItems.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<ContentHierarchyItemPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCaseSource(nameof(RejectedBulkPatches))]
    public void BulkPatchAsync_ThrowsAndSkipsTheRepository_WhenThePatchIsRejected(
        ContentHierarchyItemPatch patch,
        Type expectedException,
        string expectedMessage)
    {
        // Arrange
        var bulkPatch = new BulkPatch<ContentFilterCriteria, ContentHierarchyItemPatch>
        {
            Ids = [Guid.NewGuid()],
            Patch = patch
        };

        // Act
        var exception = Assert.ThrowsAsync(expectedException, () => _subject.BulkPatchAsync(bulkPatch, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo(expectedMessage));
            _hierarchyItems.Verify(
                repository => repository.BulkPatchAsync(
                    It.IsAny<BulkPatch<ContentHierarchyItemCriteria, ContentHierarchyItemPatch>>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public void BulkDeleteAsync_ThrowsAndSkipsTheRepository_WhenFilterUsesLegacyMediaFiles()
    {
        // Arrange
        var action = new BulkAction<ContentFilterCriteria>
        {
            Filter = new ContentFilterCriteria { MediaFiles = new() { And = [new MediaFileCriteria()] } }
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.BulkDeleteAsync(action, true, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                exception!.Message,
                Is.EqualTo("The filter uses the removed top-level media file criteria. (Parameter 'filter')"));
            _hierarchyItems.Verify(
                repository => repository.BulkDeleteAsync(
                    It.IsAny<BulkAction<ContentHierarchyItemCriteria>>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public void QueryAsync_ThrowsWithGuidance_WhenFilterUsesLegacyMediaFiles()
    {
        // Arrange
        var query = new ContentQuery
        {
            Criteria = new ContentFilterCriteria { MediaFiles = new() { And = [new MediaFileCriteria()] } }
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(
            () => _subject.QueryAsync(query, new() { PageNumber = 1, PageSize = 10 }, null));

        // Assert
        Assert.That(
            exception!.Message,
            Is.EqualTo("The filter uses the removed top-level media file criteria. (Parameter 'filter')"));
    }
}
