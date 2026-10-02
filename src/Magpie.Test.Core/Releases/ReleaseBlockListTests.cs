using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Releases;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Releases;

[TestFixture]
public sealed class ReleaseBlockListTests
{
    private static readonly Guid LeafContentHierarchyItemId =
        Guid.Parse("53cf05a8-b01e-4cc9-b9b1-c364d3b86187");

    private static readonly Guid FirstReleaseId = Guid.Parse("162eb527-a8da-49d0-a6e0-be4bf4b3ec22");

    private static readonly Guid SecondReleaseId = Guid.Parse("42c7da8f-1696-41fe-adcd-513dd69b76e0");

    private static readonly Guid ThirdReleaseId = Guid.Parse("2570a39c-923a-43f4-872d-fdf1eb7c97f0");

    private static readonly Guid FourthReleaseId = Guid.Parse("66e5e7f6-ad42-4984-929f-e6f2f3a24050");

    private static readonly Guid StoredReleaseId = Guid.Parse("e90b25c6-3a4a-4b25-a707-0470eba5cd5f");

    private static readonly Guid IndexerId = Guid.Parse("029b9454-ce64-4243-8cdd-ed2688c2551e");

    private static readonly Guid DownloadId = Guid.Parse("50c7278e-3b43-4ee8-a370-ddf06e680d5c");

    private static readonly Guid ImportedDownloadId = Guid.Parse("7d0b1f7e-54c4-4b43-9d8f-0e6a3c2b91a4");

    private static readonly Guid DownloadClientId = Guid.Parse("649d3cad-7f50-4513-ace2-4ce2434d424e");

    private Mock<IDownloadRepository> _downloads = null!;

    private ReleaseBlockList _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _downloads = new Mock<IDownloadRepository>();
        _downloads
            .Setup(repository => repository.ListByReleaseAsync(
                It.IsAny<IReadOnlyCollection<Release>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _subject = new ReleaseBlockList(_downloads.Object);
    }

    [TestCase(DownloadStatus.Failed, true)]
    [TestCase(DownloadStatus.Faulted, false)]
    public async Task ResolveStatusesAsync_ReportsTheFailure_WhenAFailureStoppedADownloadOfTheKeylessRelease(
        DownloadStatus status,
        bool expectedBlocked)
    {
        // Arrange
        var rejected = new Release
        {
            Id = FirstReleaseId,
            Protocol = "torrent",
            Title = "Pilot.1080p-GRP",
            ExternalId = "first"
        };
        var unrelated = new Release
        {
            Id = SecondReleaseId,
            Protocol = "torrent",
            Title = "Pilot.720p-GRP",
            ExternalId = "second"
        };
        _downloads
            .Setup(repository => repository.ListByReleaseAsync(
                It.IsAny<IReadOnlyCollection<Release>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Download
                {
                    Id = DownloadId,
                    ReleaseId = FirstReleaseId,
                    DownloadClientId = DownloadClientId,
                    ExternalId = "rejected",
                    Title = "rejected grab",
                    Status = status,
                    FailureReason = DownloadFailureReason.ClientRejected
                }
            ]);

        // Act
        var statuses = await _subject
            .ResolveStatusesAsync([rejected, unrelated], LeafContentHierarchyItemId, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statuses.Keys, Is.EquivalentTo([FirstReleaseId]));
            Assert.That(
                statuses[FirstReleaseId],
                Is.EqualTo((status, (DownloadFailureReason?)DownloadFailureReason.ClientRejected)));
            Assert.That(ReleaseBlockList.Blocked(statuses).Contains(FirstReleaseId), Is.EqualTo(expectedBlocked));
        }
    }

    [Test]
    public async Task Blocked_BlocksOnlyTheReleaseWithTheSameIndexerAndExternalId_WhenAnotherRowOfItFailed()
    {
        // Arrange
        var sameSource = new Release
        {
            Id = FirstReleaseId,
            IndexerId = IndexerId,
            Protocol = "torrent",
            Title = "Pilot.1080p-GRP",
            ExternalId = "shared"
        };
        var noIndexer = new Release
        {
            Id = SecondReleaseId,
            Protocol = "torrent",
            Title = "Pilot.1080p-GRP",
            ExternalId = "shared"
        };
        var noExternalId = new Release
        {
            Id = ThirdReleaseId,
            IndexerId = IndexerId,
            Protocol = "torrent",
            Title = "Pilot.1080p-GRP"
        };
        var otherExternalId = new Release
        {
            Id = FourthReleaseId,
            IndexerId = IndexerId,
            Protocol = "torrent",
            Title = "Pilot.720p-GRP",
            ExternalId = "other"
        };
        _downloads
            .Setup(repository => repository.ListByReleaseAsync(
                It.IsAny<IReadOnlyCollection<Release>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Download
                {
                    Id = DownloadId,
                    ReleaseId = StoredReleaseId,
                    Release = new Release
                    {
                        Id = StoredReleaseId,
                        IndexerId = IndexerId,
                        Protocol = "torrent",
                        Title = "Pilot.1080p-GRP",
                        ExternalId = "shared"
                    },
                    DownloadClientId = DownloadClientId,
                    ExternalId = "rejected",
                    Title = "rejected grab",
                    Status = DownloadStatus.Failed,
                    FailureReason = DownloadFailureReason.ClientRejected
                }
            ]);

        // Act
        var result = ReleaseBlockList.Blocked(
            await _subject
                .ResolveStatusesAsync(
                    [sameSource, noIndexer, noExternalId, otherExternalId],
                    LeafContentHierarchyItemId,
                    CancellationToken.None)
                .ConfigureAwait(false));

        // Assert
        Assert.That(result, Is.EquivalentTo([FirstReleaseId]));
    }

    [TestCase("53cf05a8-b01e-4cc9-b9b1-c364d3b86187", true)]
    [TestCase("0f3e2d1c-5b4a-4c9d-8e7f-6a5b4c3d2e1f", false)]
    public async Task Blocked_ReportsTheBlock_WhenTheKeylessReleaseWasImportedAndTheScopeVaries(
        string scopeItemId,
        bool expectedBlocked)
    {
        // Arrange
        var release = new Release
        {
            Id = FirstReleaseId,
            Protocol = "torrent",
            Title = "Pilot.1080p-GRP",
            ExternalId = "first"
        };
        var download = new Download
        {
            Id = ImportedDownloadId,
            ReleaseId = FirstReleaseId,
            DownloadClientId = DownloadClientId,
            ExternalId = "imported",
            Title = "earlier import",
            Status = DownloadStatus.Imported,
            Scope =
            {
                new DownloadScope
                {
                    DownloadId = ImportedDownloadId,
                    ContentHierarchyItemId = Guid.Parse(scopeItemId)
                }
            }
        };
        _downloads
            .Setup(repository => repository.ListByReleaseAsync(
                It.IsAny<IReadOnlyCollection<Release>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([download]);

        // Act
        var result = ReleaseBlockList.Blocked(
            await _subject
                .ResolveStatusesAsync([release], LeafContentHierarchyItemId, CancellationToken.None)
                .ConfigureAwait(false));

        // Assert
        Assert.That(result.Contains(FirstReleaseId), Is.EqualTo(expectedBlocked));
    }

    [Test]
    public async Task Blocked_BlocksTheRelease_WhenAFailedDownloadSharesAnIdentityKey()
    {
        // Arrange
        var blocked = new Release
        {
            Id = FirstReleaseId,
            Protocol = "torrent",
            Title = "Pilot.1080p-GRP",
            ExternalId = "first",
            IdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "failed-key" } }
        };
        var clean = new Release
        {
            Id = SecondReleaseId,
            Protocol = "torrent",
            Title = "Pilot.720p-GRP",
            ExternalId = "second",
            IdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "clean-key" } }
        };
        _downloads
            .Setup(repository => repository.ListByReleaseAsync(
                It.IsAny<IReadOnlyCollection<Release>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Download
                {
                    Id = DownloadId,
                    DownloadClientId = DownloadClientId,
                    ExternalId = "failed",
                    Title = "failed grab",
                    Status = DownloadStatus.Failed,
                    ReleaseIdentityKeys =
                    {
                        new ReleaseIdentityKey { Authority = "provider", Value = "failed-key" }
                    }
                }
            ]);

        // Act
        var result = ReleaseBlockList.Blocked(
            await _subject
                .ResolveStatusesAsync([blocked, clean], LeafContentHierarchyItemId, CancellationToken.None)
                .ConfigureAwait(false));

        // Assert
        Assert.That(result, Is.EquivalentTo([FirstReleaseId]));
    }

    [Test]
    public async Task ResolveStatusesAsync_ReportsFaultedWithoutBlocking_WhenOnlyAFaultedDownloadSharesAnIdentityKey()
    {
        // Arrange
        var faulted = new Release
        {
            Id = FirstReleaseId,
            Protocol = "torrent",
            Title = "Pilot.1080p-GRP",
            ExternalId = "first",
            IdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "faulted-key" } }
        };
        _downloads
            .Setup(repository => repository.ListByReleaseAsync(
                It.IsAny<IReadOnlyCollection<Release>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Download
                {
                    Id = DownloadId,
                    DownloadClientId = DownloadClientId,
                    ExternalId = "faulted",
                    Title = "faulted grab",
                    Status = DownloadStatus.Faulted,
                    FailureReason = DownloadFailureReason.DownloadStuck,
                    ReleaseIdentityKeys =
                    {
                        new ReleaseIdentityKey { Authority = "provider", Value = "faulted-key" }
                    }
                }
            ]);

        // Act
        var statuses = await _subject
            .ResolveStatusesAsync([faulted], LeafContentHierarchyItemId, CancellationToken.None)
            .ConfigureAwait(false);
        var blocked = ReleaseBlockList.Blocked(
            await _subject
                .ResolveStatusesAsync([faulted], LeafContentHierarchyItemId, CancellationToken.None)
                .ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                statuses[FirstReleaseId],
                Is.EqualTo((DownloadStatus.Faulted, (DownloadFailureReason?)DownloadFailureReason.DownloadStuck)));
            Assert.That(blocked, Is.Empty);
        }
    }

    [Test]
    public async Task Blocked_BlocksOnlyTheImportedRelease_WhenTheImportCompletedLongAgo()
    {
        // Arrange
        var imported = new Release
        {
            Id = FirstReleaseId,
            Protocol = "torrent",
            Title = "Pilot.1080p-GRP",
            ExternalId = "first",
            IdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "imported-key" } }
        };
        var other = new Release
        {
            Id = SecondReleaseId,
            Protocol = "torrent",
            Title = "Pilot.720p-GRP",
            ExternalId = "second",
            IdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "other-key" } }
        };
        _downloads
            .Setup(repository => repository.ListByReleaseAsync(
                It.IsAny<IReadOnlyCollection<Release>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Download
                {
                    Id = DownloadId,
                    DownloadClientId = DownloadClientId,
                    ExternalId = "imported",
                    Title = "import that covered this leaf",
                    Status = DownloadStatus.Imported,
                    DateCompleted = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    ReleaseIdentityKeys =
                    {
                        new ReleaseIdentityKey { Authority = "provider", Value = "imported-key" }
                    }
                }
            ]);

        // Act
        var result = ReleaseBlockList.Blocked(
            await _subject
                .ResolveStatusesAsync([imported, other], LeafContentHierarchyItemId, CancellationToken.None)
                .ConfigureAwait(false));

        // Assert
        Assert.That(result, Is.EquivalentTo([FirstReleaseId]));
    }

    [Test]
    public async Task ResolveStatusesAsync_ReturnsTheBlockingStatus_WhenReleasesWereImportedOrFailed()
    {
        // Arrange
        var imported = new Release
        {
            Id = FirstReleaseId,
            Protocol = "torrent",
            Title = "Pilot.1080p-GRP",
            ExternalId = "first",
            IdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "shared-key" } }
        };
        var failed = new Release
        {
            Id = SecondReleaseId,
            Protocol = "torrent",
            Title = "Pilot.720p-GRP",
            ExternalId = "second",
            IdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "failed-key" } }
        };
        _downloads
            .Setup(repository => repository.ListByReleaseAsync(
                It.IsAny<IReadOnlyCollection<Release>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Download
                {
                    Id = DownloadId,
                    DownloadClientId = DownloadClientId,
                    ExternalId = "failed",
                    Title = "failed grab",
                    Status = DownloadStatus.Failed,
                    FailureReason = DownloadFailureReason.ImportFailed,
                    FailedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                    ReleaseIdentityKeys =
                    {
                        new ReleaseIdentityKey { Authority = "provider", Value = "shared-key" },
                        new ReleaseIdentityKey { Authority = "provider", Value = "failed-key" }
                    }
                },
                new Download
                {
                    Id = Guid.Parse("7a1c5e2f-9b3d-4e6a-8c0f-1d2e3f4a5b6c"),
                    DownloadClientId = DownloadClientId,
                    ExternalId = "failed-again",
                    Title = "later failed grab",
                    Status = DownloadStatus.Failed,
                    FailureReason = DownloadFailureReason.BlockedFileType,
                    FailedAt = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
                    ReleaseIdentityKeys =
                    {
                        new ReleaseIdentityKey { Authority = "provider", Value = "failed-key" }
                    }
                },
                new Download
                {
                    Id = ImportedDownloadId,
                    DownloadClientId = DownloadClientId,
                    ExternalId = "imported",
                    Title = "import that covered this leaf",
                    Status = DownloadStatus.Imported,
                    ReleaseIdentityKeys =
                    {
                        new ReleaseIdentityKey { Authority = "provider", Value = "shared-key" }
                    }
                }
            ]);

        // Act
        var result = await _subject
            .ResolveStatusesAsync([imported, failed], LeafContentHierarchyItemId, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[FirstReleaseId], Is.EqualTo((DownloadStatus.Imported, (DownloadFailureReason?)null)));
            Assert.That(
                result[SecondReleaseId],
                Is.EqualTo((DownloadStatus.Failed, (DownloadFailureReason?)DownloadFailureReason.BlockedFileType)));
        }
    }

    [Test]
    public async Task ResolveStatusesAsync_ReadsTheDownloadsOnce_WhenSeveralReleasesAreGiven()
    {
        // Arrange
        var first = new Release
        {
            Id = FirstReleaseId,
            Protocol = "torrent",
            Title = "Pilot.1080p-GRP",
            ExternalId = "first",
            IdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "first-key" } }
        };
        var second = new Release
        {
            Id = SecondReleaseId,
            Protocol = "torrent",
            Title = "Pilot.720p-GRP",
            ExternalId = "second",
            IdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "second-key" } }
        };

        // Act
        await _subject
            .ResolveStatusesAsync([first, second], LeafContentHierarchyItemId, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        _downloads.Verify(
            repository => repository.ListByReleaseAsync(
                It.Is<IReadOnlyCollection<Release>>(releases => releases.SequenceEqual(new[] { first, second })),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _downloads.VerifyNoOtherCalls();
    }

    [TestCase(DownloadStatus.Imported, true, false, true)]
    [TestCase(DownloadStatus.Imported, false, false, true)]
    [TestCase(DownloadStatus.Imported, false, true, false)]
    [TestCase(DownloadStatus.Cancelled, true, false, false)]
    public async Task Blocked_ReportsTheBlock_WhenImportStatusAndScopeVary(
        DownloadStatus status,
        bool scopedToItem,
        bool scopedToOtherItem,
        bool expectedBlocked)
    {
        // Arrange
        var release = new Release
        {
            Id = FirstReleaseId,
            Protocol = "torrent",
            Title = "Pilot.1080p-GRP",
            ExternalId = "first",
            IdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "imported-key" } }
        };
        var download = new Download
        {
            Id = ImportedDownloadId,
            DownloadClientId = DownloadClientId,
            ExternalId = "imported",
            Title = "earlier import",
            Status = status,
            ReleaseIdentityKeys = { new ReleaseIdentityKey { Authority = "provider", Value = "imported-key" } }
        };
        if (scopedToItem)
        {
            download.Scope.Add(
                new DownloadScope
                {
                    DownloadId = ImportedDownloadId,
                    ContentHierarchyItemId = LeafContentHierarchyItemId
                });
        }

        if (scopedToOtherItem)
        {
            download.Scope.Add(
                new DownloadScope
                {
                    DownloadId = ImportedDownloadId,
                    ContentHierarchyItemId = Guid.Parse("0f3e2d1c-5b4a-4c9d-8e7f-6a5b4c3d2e1f")
                });
        }

        _downloads
            .Setup(repository => repository.ListByReleaseAsync(
                It.IsAny<IReadOnlyCollection<Release>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([download]);

        // Act
        var result = ReleaseBlockList.Blocked(
            await _subject
                .ResolveStatusesAsync([release], LeafContentHierarchyItemId, CancellationToken.None)
                .ConfigureAwait(false));

        // Assert
        Assert.That(result.Contains(FirstReleaseId), Is.EqualTo(expectedBlocked));
    }
}
