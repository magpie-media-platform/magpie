using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Releases;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.Extensions.Time.Testing;

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

    private static readonly Guid DownloadId = Guid.Parse("50c7278e-3b43-4ee8-a370-ddf06e680d5c");

    private static readonly Guid DownloadClientId = Guid.Parse("649d3cad-7f50-4513-ace2-4ce2434d424e");

    private Mock<IDownloadRepository> _downloads = null!;

    private ReleaseBlockList _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _downloads = new Mock<IDownloadRepository>();
        _downloads
            .Setup(repository => repository.ListFailedByReleaseIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<ReleaseIdentityKey>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _downloads
            .Setup(repository => repository.ListImportedByContentHierarchyItemAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _subject = new ReleaseBlockList(
            _downloads.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public async Task ResolveAsync_BlocksTheRelease_WhenAFailedDownloadSharesAnIdentityKey()
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
            .Setup(repository => repository.ListFailedByReleaseIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<ReleaseIdentityKey>>(),
                It.IsAny<Guid?>(),
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
        var result = await _subject
            .ResolveAsync([blocked, clean], LeafContentHierarchyItemId, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.EquivalentTo([FirstReleaseId]));
    }

    [TestCase("2026-09-18T00:00:00Z", true)]
    [TestCase("2026-09-17T23:59:59Z", false)]
    public async Task ResolveAsync_BlocksTheRelease_WhenALeafImportIsInsideTheWindow(
        string completedAt,
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
        var completed = DateTime.Parse(
            completedAt,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        _downloads
            .Setup(repository => repository.ListImportedByContentHierarchyItemAsync(
                LeafContentHierarchyItemId,
                It.Is<DateTime>(completedAfter => completed >= completedAfter),
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
                    DateCompleted = completed,
                    ReleaseIdentityKeys =
                    {
                        new ReleaseIdentityKey { Authority = "provider", Value = "imported-key" }
                    }
                }
            ]);

        // Act
        var result = await _subject
            .ResolveAsync([release], LeafContentHierarchyItemId, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Contains(FirstReleaseId), Is.EqualTo(expectedBlocked));
    }

    [Test]
    public async Task ResolveAsync_SendsOneTaintQueryWithEveryKey_WhenSeveralReleasesAreGiven()
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
            .ResolveAsync([first, second], LeafContentHierarchyItemId, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        _downloads.Verify(
            repository => repository.ListFailedByReleaseIdentitiesAsync(
                It.Is<IReadOnlyCollection<ReleaseIdentityKey>>(keys =>
                    keys.Any(key => key.Value == "first-key") &&
                    keys.Any(key => key.Value == "second-key")),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _downloads.Verify(
            repository => repository.ListFailedByReleaseIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<ReleaseIdentityKey>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
