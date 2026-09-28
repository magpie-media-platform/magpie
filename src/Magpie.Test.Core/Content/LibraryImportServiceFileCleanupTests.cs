using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Content;
using Magpie.Models;
using Magpie.Models.Api;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Models.Releases;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class LibraryImportServiceFileCleanupTests
{
    private static readonly ContentHierarchyLevelDefinition EpisodeLevel = new()
    {
        Slug = "episode",
        ParentSlug = "season",
        DisplayName = "Episode",
        CustomMetadataFields = [],
        AcceptedFileTypes = ["video/*"],
        AcceptedSidecarTypes = [".srt"],
    };

    private readonly List<ContentHierarchyItem> _items = [];
    private readonly Dictionary<string, Guid> _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _ranks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missingOnDisk = new(StringComparer.Ordinal);
    private Mock<IContentService> _contentService = null!;
    private Mock<IContentFileService> _contentFiles = null!;
    private UpgradeFileHandling _upgradeFileHandling;
    private List<string> _seasonFileTypes = null!;
    private ContentHierarchyItem _show = null!;
    private ContentHierarchyItem _season = null!;
    private ContentHierarchyItem _episode = null!;
    private ContentHierarchyItem _secondEpisode = null!;

    private static bool Matches(string filePath, string? mimeType, IReadOnlyList<string>? fileTypes) =>
        fileTypes?.Any(rule => rule.EndsWith("/*", StringComparison.Ordinal)
            ? mimeType?.StartsWith(rule[..^1], StringComparison.Ordinal) == true
            : filePath.EndsWith(rule, StringComparison.OrdinalIgnoreCase)) == true;

    private static MediaFile AddFile(ContentHierarchyItem owner, string fileName, string? mimeType)
    {
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = owner.ContentItem!.Id,
            FileKind = mimeType is null ? "Subtitle" : "Video",
            MimeType = mimeType,
            FilePath = $"/tv/{owner.Id:N}/{fileName}",
        };
        owner.ContentItem.MediaFiles.Add(file);
        return file;
    }

    [SetUp]
    public void SetUp()
    {
        _items.Clear();
        _bindings.Clear();
        _ranks.Clear();
        _missingOnDisk.Clear();
        _upgradeFileHandling = UpgradeFileHandling.Replace;
        _seasonFileTypes = [];
        _contentService = new Mock<IContentService>();
        _contentFiles = new Mock<IContentFileService>();
        _contentFiles
            .Setup(service => service.DeleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteContentFileResult { Deleted = true });
        _contentService
            .Setup(service => service.UnlinkFileAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _contentService
            .Setup(service => service.ReassignFileAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<ReassignContentFileRequest>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                Guid _,
                Guid mediaFileId,
                ReassignContentFileRequest request,
                Guid? _,
                CancellationToken _) => Move(mediaFileId, request.TargetContentHierarchyItemId));
        _show = AddItem("show", "Show", null);
        _season = AddItem("season", "Season 1", _show.Id);
        _episode = AddItem("episode", "Pilot", _season.Id);
        _secondEpisode = AddItem("episode", "Second", _season.Id);
    }

    [Test]
    public async Task PlanAsync_ReassignsBoundContainerFilesAndReviewsUnboundOnes()
    {
        // Arrange
        var video = AddFile(_season, "Show - S01E01.mkv", "video/x-matroska");
        var subtitle = AddFile(_season, "Show - S01E01.en.srt", null);
        var multiEpisode = AddFile(_season, "Show - S01E01-E02.mkv", "video/x-matroska");
        _bindings[video.FilePath] = _episode.Id;
        _bindings[subtitle.FilePath] = _episode.Id;

        // Act
        var plan = await CreateSubject().PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Reassign.Select(row => (row.MediaFileId, row.TargetItemId, row.Primary)),
                Is.EquivalentTo(
                [
                    (video.Id, (Guid?)_episode.Id, true),
                    (subtitle.Id, (Guid?)_episode.Id, false)
                ]));
            Assert.That(
                plan.Reassign.Select(row => row.CurrentItem).Distinct(),
                Is.EqualTo(["Show / Season 1"]));
            Assert.That(
                plan.Reassign.Select(row => row.TargetItem).Distinct(),
                Is.EqualTo(["Show / Season 1 / Pilot"]));
            Assert.That(plan.Review.Single().MediaFileId, Is.EqualTo(multiEpisode.Id));
            Assert.That(plan.Review.Single().Reason, Does.Contain("no single item below season 'Season 1'"));
            Assert.That(plan.Unlink, Is.Empty);
        }
    }

    [Test]
    public async Task PlanAsync_LeavesAFileThatTheContainerLevelAccepts()
    {
        // Arrange
        _seasonFileTypes = ["video/*"];
        var video = AddFile(_season, "Show - S01E01.mkv", "video/x-matroska");
        _bindings[video.FilePath] = _episode.Id;

        // Act
        var plan = await CreateSubject().PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Reassign, Is.Empty);
            Assert.That(plan.Review, Is.Empty);
            Assert.That(plan.Unlink, Is.Empty);
        }
    }

    [Test]
    public async Task PlanAsync_PlansOnlyTheRootItemAndItsDescendants()
    {
        // Arrange
        var inside = AddFile(_season, "Show - S01E01.mkv", "video/x-matroska");
        _bindings[inside.FilePath] = _episode.Id;
        var otherShow = AddItem("show", "Other", null);
        var otherSeason = AddItem("season", "Season 1", otherShow.Id);
        var otherEpisode = AddItem("episode", "Other Pilot", otherSeason.Id);
        var outside = AddFile(otherSeason, "Other - S01E01.mkv", "video/x-matroska");
        _bindings[outside.FilePath] = otherEpisode.Id;
        var subject = CreateSubject();

        // Act
        var fromShow = await subject.PlanAsync(_show.Id).ConfigureAwait(false);
        var fromSeason = await subject.PlanAsync(_season.Id).ConfigureAwait(false);
        var whole = await subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fromShow.Reassign.Select(row => row.MediaFileId), Is.EqualTo([inside.Id]));
            Assert.That(fromSeason.Reassign.Select(row => row.MediaFileId), Is.EqualTo([inside.Id]));
            Assert.That(
                whole.Reassign.Select(row => row.MediaFileId),
                Is.EquivalentTo([inside.Id, outside.Id]));
        }
    }

    [TestCase(UpgradeFileHandling.Replace)]
    [TestCase(UpgradeFileHandling.KeepBoth)]
    public async Task PlanAsync_SupersedesAWorseCopyOnlyWhenTheRootFolderReplacesUpgrades(UpgradeFileHandling handling)
    {
        // Arrange
        _upgradeFileHandling = handling;
        var worse = AddFile(_episode, "Show - S01E01 480p.mkv", "video/x-matroska");
        var better = AddFile(_season, "Show - S01E01 1080p.mkv", "video/x-matroska");
        _bindings[better.FilePath] = _episode.Id;
        _ranks[worse.FilePath] = 3;
        _ranks[better.FilePath] = 1;

        // Act
        var plan = await CreateSubject().PlanAsync(null).ConfigureAwait(false);

        // Assert
        var superseded = handling == UpgradeFileHandling.Replace ? plan.Unlink.Single() : plan.Review.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Reassign.Single().MediaFileId, Is.EqualTo(better.Id));
            Assert.That(superseded.MediaFileId, Is.EqualTo(worse.Id));
            Assert.That(superseded.CurrentItemId, Is.EqualTo(_episode.Id));
            Assert.That(superseded.Rank, Is.EqualTo(3));
            Assert.That(superseded.KeepMediaFileId, Is.EqualTo(better.Id));
            Assert.That(
                plan.Reassign.Concat(plan.Unlink).Concat(plan.Review).Select(row => row.Action),
                Has.None.EqualTo(MediaFileCleanupAction.Delete));
        }
    }

    [Test]
    public async Task PlanAsync_ReviewsCopiesThatShareTheBestRank()
    {
        // Arrange
        var first = AddFile(_episode, "Show - S01E01.mkv", "video/x-matroska");
        var second = AddFile(_episode, "Show - S01E01.mp4", "video/mp4");
        _ranks[first.FilePath] = 1;
        _ranks[second.FilePath] = 1;

        // Act
        var plan = await CreateSubject().PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Review.Select(row => row.MediaFileId), Is.EquivalentTo([first.Id, second.Id]));
            Assert.That(plan.Unlink, Is.Empty);
        }
    }

    [Test]
    public async Task PlanAsync_ReviewsEveryCopy_WhenNoCopyHasARank()
    {
        // Arrange
        var first = AddFile(_episode, "Show - S01E01.mkv", "video/x-matroska");
        var second = AddFile(_episode, "Show - S01E01.mp4", "video/mp4");

        // Act
        var plan = await CreateSubject().PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Review.Select(row => row.MediaFileId), Is.EquivalentTo([first.Id, second.Id]));
            Assert.That(
                plan.Review.Select(row => row.Reason),
                Has.All.Contains("No copy of this item has a quality rank"));
            Assert.That(plan.Unlink, Is.Empty);
        }
    }

    [Test]
    public async Task PlanAsync_UnlinksTheSidecarOfASupersededCopyButNotOfTheKeptCopy()
    {
        // Arrange
        var worse = AddFile(_episode, "Old.mkv", "video/x-matroska");
        var worseSubtitle = AddFile(_episode, "Old.en.srt", null);
        var better = AddFile(_episode, "New.mkv", "video/x-matroska");
        AddFile(_episode, "New.en.srt", null);
        _ranks[worse.FilePath] = 2;
        _ranks[better.FilePath] = 1;

        // Act
        var plan = await CreateSubject().PlanAsync(null).ConfigureAwait(false);

        // Assert
        Assert.That(
            plan.Unlink.Select(row => (row.MediaFileId, row.Primary)),
            Is.EquivalentTo([(worse.Id, true), (worseSubtitle.Id, false)]));
    }

    [Test]
    public async Task PlanAsync_KeepsASidecarThatAlsoBelongsToTheKeptRecode()
    {
        // Arrange
        var worse = AddFile(_episode, "Show - S01E01.avi", "video/x-msvideo");
        var better = AddFile(_episode, "Show - S01E01.mkv", "video/x-matroska");
        AddFile(_episode, "Show - S01E01.en.srt", null);
        _ranks[worse.FilePath] = 2;
        _ranks[better.FilePath] = 1;

        // Act
        var plan = await CreateSubject().PlanAsync(null).ConfigureAwait(false);

        // Assert
        Assert.That(plan.Unlink.Select(row => row.MediaFileId), Is.EqualTo([worse.Id]));
    }

    [TestCase(1)]
    [TestCase(null)]
    public async Task PlanAsync_KeepsASidecarThatBelongsToACopyUnderReview(int? reviewedRank)
    {
        // Arrange
        var keep = AddFile(_episode, "A.mkv", "video/x-matroska");
        var reviewed = AddFile(_episode, "Show - S01E01.mp4", "video/mp4");
        var worse = AddFile(_episode, "Show - S01E01.avi", "video/x-msvideo");
        AddFile(_episode, "Show - S01E01.en.srt", null);
        _ranks[keep.FilePath] = 1;
        _ranks[worse.FilePath] = 2;
        if (reviewedRank is { } rank)
        {
            _ranks[reviewed.FilePath] = rank;
        }

        // Act
        var plan = await CreateSubject().PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Unlink.Select(row => row.MediaFileId), Is.EqualTo([worse.Id]));
            Assert.That(plan.Review.Select(row => row.MediaFileId), Does.Contain(reviewed.Id));
        }
    }

    [Test]
    public async Task PlanAsync_ListsASidecarOnce_WhenTwoUnlinkedCopiesShareItsStem()
    {
        // Arrange
        var keep = AddFile(_episode, "New.mkv", "video/x-matroska");
        var worse = AddFile(_episode, "Old.avi", "video/x-msvideo");
        var worst = AddFile(_episode, "Old.mp4", "video/mp4");
        var subtitle = AddFile(_episode, "Old.en.srt", null);
        _ranks[keep.FilePath] = 1;
        _ranks[worse.FilePath] = 2;
        _ranks[worst.FilePath] = 3;

        // Act
        var plan = await CreateSubject().PlanAsync(null).ConfigureAwait(false);

        // Assert
        Assert.That(
            plan.Unlink.Select(row => row.MediaFileId),
            Is.EquivalentTo([worse.Id, worst.Id, subtitle.Id]));
    }

    [Test]
    public async Task ApplyAsync_RunsOnlyApprovedRowsThatStillMatchThePlan()
    {
        // Arrange
        var worse = AddFile(_episode, "Show - S01E01 480p.mkv", "video/x-matroska");
        var better = AddFile(_season, "Show - S01E01 1080p.mkv", "video/x-matroska");
        _bindings[better.FilePath] = _episode.Id;
        _ranks[worse.FilePath] = 3;
        _ranks[better.FilePath] = 1;
        var subject = CreateSubject();
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);
        var request = new MediaFileCleanupApplyRequest
        {
            Rows =
            [
                plan.Reassign.Single(),
                plan.Unlink.Single() with { Action = MediaFileCleanupAction.Delete },
                plan.Reassign.Single() with { Action = MediaFileCleanupAction.Delete },
                plan.Unlink.Single() with { FilePath = "stale.mkv", Action = MediaFileCleanupAction.Unlink },
            ]
        };

        // Act
        var results = await subject.ApplyAsync(request, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(result => result.Applied), Is.EqualTo([true, true, false, false]));
            Assert.That(results[2].Message, Does.Contain("not deleted"));
            _contentService.Verify(
                service => service.ReassignFileAsync(
                    _season.Id,
                    better.Id,
                    It.Is<ReassignContentFileRequest>(reassign =>
                        reassign.TargetContentHierarchyItemId == _episode.Id &&
                        reassign.ExpectedFilePath == better.FilePath),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentFiles.Verify(
                service => service.DeleteAsync(
                    _episode.Id,
                    worse.Id,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentFiles.Verify(
                service => service.DeleteAsync(
                    It.IsAny<Guid>(),
                    better.Id,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _contentService.Verify(
                service => service.UnlinkFileAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase(MediaFileCleanupAction.Delete)]
    [TestCase(MediaFileCleanupAction.Unlink)]
    public async Task ApplyAsync_KeepsTheItemsCopy_WhenTheReassignOfTheKeptCopyWasDeclined(
        MediaFileCleanupAction action)
    {
        // Arrange
        var worse = AddFile(_episode, "Show - S01E01 480p.mkv", "video/x-matroska");
        var better = AddFile(_season, "Show - S01E01 1080p.mkv", "video/x-matroska");
        _bindings[better.FilePath] = _episode.Id;
        _ranks[worse.FilePath] = 3;
        _ranks[better.FilePath] = 1;
        var subject = CreateSubject();
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);

        // Act
        var results = await subject
            .ApplyAsync(
                new() { Rows = [plan.Unlink.Single() with { Action = action }] },
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Single().Applied, Is.False);
            VerifyNothingRemoved();
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ApplyAsync_KeepsTheItemsCopy_WhenTheReassignOfTheKeptCopyFails(bool throws)
    {
        // Arrange
        var worse = AddFile(_episode, "Show - S01E01 480p.mkv", "video/x-matroska");
        var better = AddFile(_season, "Show - S01E01 1080p.mkv", "video/x-matroska");
        _bindings[better.FilePath] = _episode.Id;
        _ranks[worse.FilePath] = 3;
        _ranks[better.FilePath] = 1;
        var subject = CreateSubject();
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);
        var reassign = _contentService.Setup(service => service.ReassignFileAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<ReassignContentFileRequest>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()));
        if (throws)
        {
            reassign.ThrowsAsync(new ArgumentException("The file path or association changed."));
        }
        else
        {
            reassign.ReturnsAsync(false);
        }

        // Act
        var results = await subject
            .ApplyAsync(
                new()
                {
                    Rows =
                    [
                        plan.Reassign.Single(),
                        plan.Unlink.Single() with { Action = MediaFileCleanupAction.Delete }
                    ]
                },
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(result => result.Applied), Is.EqualTo([false, false]));
            VerifyNothingRemoved();
        }
    }

    [Test]
    public async Task ApplyAsync_RefusesADelete_WhenTheKeptCopyIsNotOnDisk()
    {
        // Arrange
        var worse = AddFile(_episode, "Show - S01E01 480p.mkv", "video/x-matroska");
        var better = AddFile(_episode, "Show - S01E01 1080p.mkv", "video/x-matroska");
        _ranks[worse.FilePath] = 3;
        _ranks[better.FilePath] = 1;
        _missingOnDisk.Add(better.FilePath);
        var subject = CreateSubject();
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);

        // Act
        var results = await subject
            .ApplyAsync(
                new() { Rows = [plan.Unlink.Single() with { Action = MediaFileCleanupAction.Delete }] },
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Single().Applied, Is.False);
            Assert.That(results.Single().Message, Does.Contain("not on disk"));
            VerifyNothingRemoved();
        }
    }

    [Test]
    public async Task ApplyAsync_TreatsARemovalRowAsStale_WhenTheKeptCopyChanged()
    {
        // Arrange
        var worse = AddFile(_episode, "Show - S01E01 480p.mkv", "video/x-matroska");
        var better = AddFile(_episode, "Show - S01E01 1080p.mkv", "video/x-matroska");
        _ranks[worse.FilePath] = 3;
        _ranks[better.FilePath] = 2;
        var subject = CreateSubject();
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);
        var best = AddFile(_episode, "Show - S01E01 2160p.mkv", "video/x-matroska");
        _ranks[best.FilePath] = 1;

        // Act
        var results = await subject
            .ApplyAsync(
                new()
                {
                    Rows = [plan.Unlink.Single(row => row.MediaFileId == worse.Id) with
                    {
                        Action = MediaFileCleanupAction.Delete
                    }]
                },
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Single().Applied, Is.False);
            VerifyNothingRemoved();
        }
    }

    [Test]
    public async Task Probe_RemovalPassAcceptsASidecarRowTheDryRunDidNotList()
    {
        // Arrange
        var keep = AddFile(_episode, "A.mkv", "video/x-matroska");
        var worse = AddFile(_episode, "Show - S01E01.avi", "video/x-msvideo");
        var subtitle = AddFile(_episode, "Show - S01E01.en.srt", null);
        _ranks[keep.FilePath] = 1;
        _ranks[worse.FilePath] = 3;
        var subject = CreateSubject();
        var approved = (await subject.PlanAsync(null).ConfigureAwait(false)).Unlink
            .Single(row => row.MediaFileId == subtitle.Id);
        var arriving = AddFile(_season, "Show - S01E01.mkv", "video/x-matroska");
        _bindings[arriving.FilePath] = _episode.Id;
        _ranks[arriving.FilePath] = 1;
        var dryRun = await subject.PlanAsync(null).ConfigureAwait(false);

        // Act
        var results = await subject
            .ApplyAsync(
                new() { Rows = [approved with { Action = MediaFileCleanupAction.Delete }] },
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dryRun.Unlink.Select(row => row.MediaFileId), Does.Not.Contain(subtitle.Id));
            Assert.That(results.Single().Applied, Is.False);
            _contentFiles.Verify(
                service => service.DeleteAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ApplyAsync_SkipsAReassignRow_WhenItsTargetChanged()
    {
        // Arrange
        var video = AddFile(_season, "Show - S01E01.mkv", "video/x-matroska");
        _bindings[video.FilePath] = _episode.Id;
        var subject = CreateSubject();
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);

        // Act
        var results = await subject
            .ApplyAsync(
                new() { Rows = [plan.Reassign.Single() with { TargetItemId = _secondEpisode.Id }] },
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Single().Applied, Is.False);
            _contentService.Verify(
                service => service.ReassignFileAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<ReassignContentFileRequest>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ApplyAsync_SkipsAReviewRow()
    {
        // Arrange
        var first = AddFile(_episode, "Show - S01E01.mkv", "video/x-matroska");
        var second = AddFile(_episode, "Show - S01E01.mp4", "video/mp4");
        _ranks[first.FilePath] = 1;
        _ranks[second.FilePath] = 1;
        var subject = CreateSubject();
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);

        // Act
        var results = await subject
            .ApplyAsync(
                new() { Rows = [plan.Review[0]] },
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Single().Applied, Is.False);
            Assert.That(results.Single().Message, Does.Contain("review row"));
            VerifyNothingRemoved();
        }
    }

    private void VerifyNothingRemoved()
    {
        _contentFiles.Verify(
            service => service.DeleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _contentService.Verify(
            service => service.UnlinkFileAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private bool Move(Guid mediaFileId, Guid targetItemId)
    {
        var owner = _items.First(item => item.ContentItem!.MediaFiles.Any(file => file.Id == mediaFileId));
        var file = owner.ContentItem!.MediaFiles.First(candidate => candidate.Id == mediaFileId);
        var target = _items.First(item => item.Id == targetItemId);
        owner.ContentItem.MediaFiles.Remove(file);
        file.ContentItemId = target.ContentItem!.Id;
        target.ContentItem.MediaFiles.Add(file);
        return true;
    }

    private ContentHierarchyItem AddItem(string level, string name, Guid? parentId)
    {
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            ContentType = "tv",
            Level = level,
            Slug = name.ToLowerInvariant(),
            Name = name,
            EffectiveRootFolderId = Guid.NewGuid(),
        };
        item.ContentItem = new ContentItem { Id = Guid.NewGuid(), ContentHierarchyItemId = item.Id };
        _items.Add(item);
        return item;
    }

    private IReadOnlyList<ContentHierarchyItem> Lineage(Guid id)
    {
        var lineage = new List<ContentHierarchyItem>();
        for (var item = _items.Find(candidate => candidate.Id == id);
             item is not null;
             item = _items.Find(candidate => candidate.Id == item.ParentId))
        {
            lineage.Add(item);
        }

        return lineage;
    }

    private List<ContentHierarchyItem> ItemsWithFiles(Guid? ancestorId) =>
        _items
            .Where(item => item.ContentItem!.MediaFiles.Count > 0)
            .Where(item => ancestorId is not { } id || Lineage(item.Id).Skip(1).Any(parent => parent.Id == id))
            .ToList();

    private LibraryImportService CreateSubject()
    {
        var hierarchy = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchy
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                ContentHierarchyItemCriteria criteria,
                PagingParameter _,
                SortParameter? _,
                CancellationToken _) =>
            {
                var parentId = criteria.ParentId.And.FirstOrDefault()?.Value;
                var items = parentId is { } id
                    ? _items.Where(item => item.ParentId == id).ToList()
                    : ItemsWithFiles(criteria.AncestorId);
                return new PagedList<ContentHierarchyItem> { Items = items, TotalItems = items.Count };
            });
        hierarchy
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _items.Find(item => item.Id == id));
        hierarchy
            .Setup(repository => repository.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Lineage(id));

        var rootFolders = new Mock<IRootFolderRepository>();
        rootFolders
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new RootFolder
            {
                Name = "TV",
                Path = "/tv",
                ContentType = "tv",
                UpgradeFileHandling = _upgradeFileHandling,
            });

        var seasonLevel = new ContentHierarchyLevelDefinition
        {
            Slug = "season",
            ParentSlug = "show",
            DisplayName = "Season",
            CustomMetadataFields = [],
            AcceptedFileTypes = _seasonFileTypes,
        };
        var registry = new Mock<IContentTypeRegistry>();
        registry.Setup(r => r.GetChildLevels("tv", "show")).Returns([seasonLevel]);
        registry.Setup(r => r.GetChildLevels("tv", "season")).Returns([EpisodeLevel]);
        registry.Setup(r => r.GetChildLevels("tv", "episode")).Returns([]);
        registry
            .Setup(r => r.GetDefinitionFor("tv"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "tv",
                DisplayName = "TV",
                LevelDefinitions =
                [
                    new() { Slug = "show", DisplayName = "Show", CustomMetadataFields = [] },
                    seasonLevel,
                    EpisodeLevel,
                ]
            });

        var evaluation = new Mock<IReleaseEvaluationService>();
        evaluation
            .Setup(service => service.AssignFilesToLeavesAsync(
                It.IsAny<Release>(),
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyList<ReleaseFileRef>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                Release _,
                ContentHierarchyItem parent,
                IReadOnlyList<ContentHierarchyItem> children,
                IReadOnlyList<ReleaseFileRef> files,
                CancellationToken _) =>
                new List<ReleaseFileAssignment>
                {
                    new()
                    {
                        FileKey = files[0].Key,
                        LeafContentHierarchyItemId = parent.Level == "show"
                            ? children[0].Id
                            : _bindings.TryGetValue(files[0].Key, out var leaf)
                                ? leaf
                                : null
                    }
                });
        evaluation
            .Setup(service => service.ScoreFileForContentAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                MediaFile file,
                Guid _,
                CancellationToken _) =>
                _ranks.TryGetValue(file.FilePath, out var rank)
                    ? new ReleaseAssessment { MatchResult = ReleaseMatchResult.Confident, MatchedRankOrdinal = rank }
                    : null);

        var fileSystem = new Mock<IFileSystem>();
        fileSystem
            .Setup(system => system.MatchesAnyFileType(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyList<string>?>()))
            .Returns((
                string path,
                string? mime,
                IReadOnlyList<string>? types) => Matches(path, mime, types));
        fileSystem
            .Setup(system => system.FileExists(It.IsAny<string>()))
            .Returns((string path) => !_missingOnDisk.Contains(path));

        return new LibraryImportService(
            Mock.Of<ILibraryImportCandidateRepository>(),
            _contentService.Object,
            _contentFiles.Object,
            registry.Object,
            hierarchy.Object,
            fileSystem.Object,
            Mock.Of<ITransactionManager>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            evaluation.Object,
            [],
            TimeProvider.System,
            rootFolders.Object,
            NullLogger<LibraryImportService>.Instance);
    }
}
