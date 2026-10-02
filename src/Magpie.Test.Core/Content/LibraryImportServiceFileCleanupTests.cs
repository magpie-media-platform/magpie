using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
using Magpie.Sdk.Content;
using Magpie.Sdk.Releases;
using Magpie.Sdk.Services;

using Microsoft.AspNetCore.Builder;
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

    private static readonly ContentHierarchyLevelDefinition MovieLevel = new()
    {
        Slug = "movie",
        DisplayName = "Movie",
        CustomMetadataFields = [],
        AcceptedFileTypes = ["video/*"],
        AcceptedSidecarTypes = [".srt"],
    };

    private static readonly ContentHierarchyLevelDefinition RecordingLevel = new()
    {
        Slug = "recording",
        DisplayName = "Recording",
        CustomMetadataFields = [],
        AcceptedFileTypes = ["audio/*", "video/*"],
    };

    private readonly List<ContentHierarchyItem> _items = [];
    private readonly Dictionary<string, Guid> _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _ranks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missingOnDisk = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _folders = [];
    private readonly Dictionary<string, List<string>> _signalValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid[]> _routes = new(StringComparer.Ordinal);
    private readonly List<(string Operation, Guid ItemId, int? Transaction)> _operations = [];
    private readonly List<int> _rolledBack = [];
    private int _transactions;
    private int? _transaction;
    private Mock<IContentService> _contentService = null!;
    private Mock<IFileRoutingContentTypeDefinitionProvider> _router = null!;
    private IContentTypeDefinitionProvider? _provider;
    private Mock<IContentFileService> _contentFiles = null!;
    private UpgradeFileHandling _upgradeFileHandling;
    private List<string> _seasonFileTypes = null!;
    private ContentHierarchyItem _show = null!;
    private ContentHierarchyItem _season = null!;
    private ContentHierarchyItem _episode = null!;
    private ContentHierarchyItem _secondEpisode = null!;
    private ContentHierarchyItem _thirdEpisode = null!;
    private ContentHierarchyItem _movie = null!;
    private ContentHierarchyItem _recording = null!;
    private LibraryImportService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _items.Clear();
        _bindings.Clear();
        _ranks.Clear();
        _missingOnDisk.Clear();
        _folders.Clear();
        _signalValues.Clear();
        _routes.Clear();
        _operations.Clear();
        _rolledBack.Clear();
        _transactions = 0;
        _transaction = null;
        _provider = null;
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
        _contentFiles
            .Setup(service => service.GetEffectivePathAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _folders.TryGetValue(id, out var folder)
                ? folder
                : $"/tv/{id:N}");
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
            .Callback((Guid _, Guid _, ReassignContentFileRequest request, Guid? _, CancellationToken _) =>
                _operations.Add(("reassign", request.TargetContentHierarchyItemId, _transaction)))
            .ReturnsAsync((
                Guid _,
                Guid mediaFileId,
                ReassignContentFileRequest request,
                Guid? _,
                CancellationToken _) =>
            {
                var owner = _items.First(item => item.ContentItem!.MediaFiles.Any(file => file.Id == mediaFileId));
                var file = owner.ContentItem!.MediaFiles.First(candidate => candidate.Id == mediaFileId);
                var target = _items.First(item => item.Id == request.TargetContentHierarchyItemId);
                owner.ContentItem.MediaFiles.Remove(file);
                file.ContentItemId = target.ContentItem!.Id;
                target.ContentItem.MediaFiles.Add(file);
                return true;
            });
        _contentService
            .Setup(service => service.AttachFileAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback((Guid itemId, string filePath, Guid? _, CancellationToken _) =>
            {
                var item = _items.Single(candidate => candidate.Id == itemId).ContentItem!;
                var file = _items.SelectMany(candidate => candidate.ContentItem!.MediaFiles)
                    .Single(candidate => candidate.FilePath == filePath);
                item.CoveringMediaFiles.Add(file);
                file.Coverage.Add(new() { MediaFileId = file.Id, ContentItemId = item.Id });
                _operations.Add(("attach", itemId, _transaction));
            })
            .ReturnsAsync((ContentFileAttachmentResult?)null);
        _router = new Mock<IFileRoutingContentTypeDefinitionProvider>();
        _router
            .Setup(router => router.Parse(It.IsAny<string>(), out It.Ref<string?>.IsAny))
            .Returns((string path, out string? levelSlug) =>
            {
                levelSlug = null;
                return _signalValues.TryGetValue(path, out var values)
                    ? new Dictionary<string, IReadOnlyList<string>> { ["file"] = [path], ["episode"] = values }
                    : null;
            });
        _router
            .Setup(router => router.ResolveFileChildren(
                It.IsAny<ContentHierarchyItem>(),
                It.IsAny<IReadOnlyList<ContentHierarchyItem>>(),
                It.IsAny<IReadOnlyDictionary<string, List<string>>>()))
            .Returns((
                ContentHierarchyItem _,
                IReadOnlyList<ContentHierarchyItem> _,
                IReadOnlyDictionary<string, List<string>> signals) =>
                _routes.GetValueOrDefault(signals["file"][0]) ?? []);
        _show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Slug = "show",
            Name = "Show",
            EffectiveRootFolderId = Guid.NewGuid(),
        };
        _show.ContentItem = new ContentItem { Id = Guid.NewGuid(), ContentHierarchyItemId = _show.Id };
        _items.Add(_show);
        _season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _show.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season 1",
            Name = "Season 1",
            EffectiveRootFolderId = Guid.NewGuid(),
        };
        _season.ContentItem = new ContentItem { Id = Guid.NewGuid(), ContentHierarchyItemId = _season.Id };
        _items.Add(_season);
        _episode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "pilot",
            Name = "Pilot",
            EffectiveRootFolderId = Guid.NewGuid(),
        };
        _episode.ContentItem = new ContentItem { Id = Guid.NewGuid(), ContentHierarchyItemId = _episode.Id };
        _items.Add(_episode);
        _secondEpisode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "second",
            Name = "Second",
            EffectiveRootFolderId = Guid.NewGuid(),
        };
        _secondEpisode.ContentItem = new ContentItem
        {
            Id = Guid.NewGuid(),
            ContentHierarchyItemId = _secondEpisode.Id,
        };
        _items.Add(_secondEpisode);
        _thirdEpisode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "third",
            Name = "Third",
            EffectiveRootFolderId = Guid.NewGuid(),
        };
        _thirdEpisode.ContentItem = new ContentItem { Id = Guid.NewGuid(), ContentHierarchyItemId = _thirdEpisode.Id };
        _items.Add(_thirdEpisode);
        _movie = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "movie",
            Level = "movie",
            Slug = "movie",
            Name = "Movie",
            EffectiveRootFolderId = Guid.NewGuid(),
        };
        _movie.ContentItem = new ContentItem { Id = Guid.NewGuid(), ContentHierarchyItemId = _movie.Id };
        _items.Add(_movie);
        _recording = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "recording",
            Level = "recording",
            Slug = "recording",
            Name = "Recording",
            EffectiveRootFolderId = Guid.NewGuid(),
        };
        _recording.ContentItem = new ContentItem { Id = Guid.NewGuid(), ContentHierarchyItemId = _recording.Id };
        _items.Add(_recording);

        Func<Guid, IReadOnlyList<ContentHierarchyItem>> lineage = id =>
        {
            var lineage = new List<ContentHierarchyItem>();
            for (var item = _items.Find(candidate => candidate.Id == id);
                 item is not null;
                 item = _items.Find(candidate => candidate.Id == item.ParentId))
            {
                lineage.Add(item);
            }

            return lineage;
        };
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
                    : _items
                        .Where(item => item.ContentItem!.MediaFiles.Count > 0)
                        .Where(item => criteria.AncestorId == null ||
                            lineage(item.Id).Skip(1).Any(parent => parent.Id == criteria.AncestorId))
                        .ToList();
                return new PagedList<ContentHierarchyItem> { Items = items, TotalItems = items.Count };
            });
        hierarchy
            .Setup(repository => repository.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _items.Find(item => item.Id == id));
        hierarchy
            .Setup(repository => repository.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => lineage(id));

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
        registry.Setup(r => r.FindProviderFor("tv")).Returns(() => _provider);
        registry.Setup(r => r.GetChildLevels("tv", "show")).Returns([seasonLevel]);
        registry.Setup(r => r.GetChildLevels("tv", "season")).Returns([EpisodeLevel]);
        registry.Setup(r => r.GetChildLevels("tv", "episode")).Returns([]);
        registry.Setup(r => r.GetChildLevels("movie", "movie")).Returns([]);
        registry.Setup(r => r.GetChildLevels("recording", "recording")).Returns([]);
        registry
            .Setup(r => r.GetDefinitionFor("movie"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "movie",
                DisplayName = "Movie",
                LevelDefinitions = [MovieLevel]
            });
        registry
            .Setup(r => r.GetDefinitionFor("recording"))
            .Returns(new ContentTypeDefinition
            {
                Slug = "recording",
                DisplayName = "Recording",
                LevelDefinitions = [RecordingLevel]
            });
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
                IReadOnlyList<string>? types) => types?.Any(rule => rule.EndsWith("/*", StringComparison.Ordinal)
                    ? mime?.StartsWith(rule[..^1], StringComparison.Ordinal) == true
                    : path.EndsWith(rule, StringComparison.OrdinalIgnoreCase)) == true);
        fileSystem
            .Setup(system => system.FileExists(It.IsAny<string>()))
            .Returns((string path) => !_missingOnDisk.Contains(path));
        fileSystem
            .Setup(system => system.IsPathWithin(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string root, string path) => path.StartsWith(root + "/", StringComparison.Ordinal));

        var transactions = new Mock<ITransactionManager>();
        transactions
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Func<CancellationToken, Task<bool>> operation, CancellationToken token) =>
            {
                _transaction = ++_transactions;
                try
                {
                    return await operation(token).ConfigureAwait(false);
                }
                catch
                {
                    _rolledBack.Add(_transactions);
                    throw;
                }
                finally
                {
                    _transaction = null;
                }
            });

        _subject = new LibraryImportService(
            Mock.Of<ILibraryImportCandidateRepository>(),
            _contentService.Object,
            _contentFiles.Object,
            registry.Object,
            hierarchy.Object,
            fileSystem.Object,
            transactions.Object,
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IEventBus>(),
            evaluation.Object,
            [],
            TimeProvider.System,
            rootFolders.Object,
            NullLogger<LibraryImportService>.Instance);
    }

    [Test]
    public async Task PlanAsync_ReassignsBoundFilesAndReviewsUnboundFiles_WhenFilesSitOnTheContainer()
    {
        // Arrange
        var video = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01.mkv",
        };
        _season.ContentItem.MediaFiles.Add(video);
        var subtitle = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Subtitle",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01.en.srt",
        };
        _season.ContentItem.MediaFiles.Add(subtitle);
        var multiEpisode = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01-E02.mkv",
        };
        _season.ContentItem.MediaFiles.Add(multiEpisode);
        _bindings[video.FilePath] = _episode.Id;
        _bindings[subtitle.FilePath] = _episode.Id;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

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
    public async Task PlanAsync_LeavesTheFile_WhenTheContainerLevelAcceptsIt()
    {
        // Arrange
        _seasonFileTypes.Add("video/*");
        var video = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01.mkv",
        };
        _season.ContentItem.MediaFiles.Add(video);
        _bindings[video.FilePath] = _episode.Id;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Reassign, Is.Empty);
            Assert.That(plan.Review, Is.Empty);
            Assert.That(plan.Unlink, Is.Empty);
        }
    }

    [Test]
    public async Task PlanAsync_ExcludesFilesOutsideTheRootItem_WhenARootItemIsGiven()
    {
        // Arrange
        var inside = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01.mkv",
        };
        _season.ContentItem.MediaFiles.Add(inside);
        _bindings[inside.FilePath] = _episode.Id;
        var otherShow = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Slug = "other",
            Name = "Other",
            EffectiveRootFolderId = Guid.NewGuid(),
        };
        otherShow.ContentItem = new ContentItem { Id = Guid.NewGuid(), ContentHierarchyItemId = otherShow.Id };
        _items.Add(otherShow);
        var otherSeason = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = otherShow.Id,
            ContentType = "tv",
            Level = "season",
            Slug = "season 1",
            Name = "Season 1",
            EffectiveRootFolderId = Guid.NewGuid(),
        };
        otherSeason.ContentItem = new ContentItem { Id = Guid.NewGuid(), ContentHierarchyItemId = otherSeason.Id };
        _items.Add(otherSeason);
        var otherEpisode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = otherSeason.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "other pilot",
            Name = "Other Pilot",
            EffectiveRootFolderId = Guid.NewGuid(),
        };
        otherEpisode.ContentItem = new ContentItem { Id = Guid.NewGuid(), ContentHierarchyItemId = otherEpisode.Id };
        _items.Add(otherEpisode);
        var outside = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = otherSeason.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{otherSeason.Id:N}/Other - S01E01.mkv",
        };
        otherSeason.ContentItem.MediaFiles.Add(outside);
        _bindings[outside.FilePath] = otherEpisode.Id;
        var subject = _subject;

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
    public async Task PlanAsync_UnlinksOrReviewsTheWorseCopy_WhenUpgradeFileHandlingIsSet(UpgradeFileHandling handling)
    {
        // Arrange
        _upgradeFileHandling = handling;
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01 480p.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(worse);
        var better = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01 1080p.mkv",
        };
        _season.ContentItem.MediaFiles.Add(better);
        _bindings[better.FilePath] = _episode.Id;
        _ranks[worse.FilePath] = 3;
        _ranks[better.FilePath] = 1;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

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
    public async Task PlanAsync_ReviewsTheCopies_WhenCopiesShareTheBestRank()
    {
        // Arrange
        var first = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(first);
        var second = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/mp4",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.mp4",
        };
        _episode.ContentItem.MediaFiles.Add(second);
        _ranks[first.FilePath] = 1;
        _ranks[second.FilePath] = 1;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

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
        var first = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(first);
        var second = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/mp4",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.mp4",
        };
        _episode.ContentItem.MediaFiles.Add(second);

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

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
    public async Task PlanAsync_UnlinksTheSupersededCopySidecar_WhenTheKeptCopyHasItsOwnSidecar()
    {
        // Arrange
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Old.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(worse);
        var worseSubtitle = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Subtitle",
            FilePath = $"/tv/{_episode.Id:N}/Old.en.srt",
        };
        _episode.ContentItem.MediaFiles.Add(worseSubtitle);
        var better = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/New.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(better);
        _episode.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Subtitle",
            FilePath = $"/tv/{_episode.Id:N}/New.en.srt",
        });
        _ranks[worse.FilePath] = 2;
        _ranks[better.FilePath] = 1;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        Assert.That(
            plan.Unlink.Select(row => (row.MediaFileId, row.Primary)),
            Is.EquivalentTo([(worse.Id, true), (worseSubtitle.Id, false)]));
    }

    [Test]
    public async Task PlanAsync_KeepsTheSidecar_WhenTheKeptRecodeAlsoOwnsIt()
    {
        // Arrange
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-msvideo",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.avi",
        };
        _episode.ContentItem.MediaFiles.Add(worse);
        var better = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(better);
        _episode.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Subtitle",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.en.srt",
        });
        _ranks[worse.FilePath] = 2;
        _ranks[better.FilePath] = 1;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        Assert.That(plan.Unlink.Select(row => row.MediaFileId), Is.EqualTo([worse.Id]));
    }

    [TestCase(1)]
    [TestCase(null)]
    public async Task PlanAsync_KeepsTheSidecar_WhenACopyUnderReviewOwnsIt(int? reviewedRank)
    {
        // Arrange
        var keep = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/A.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(keep);
        var reviewed = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/mp4",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.mp4",
        };
        _episode.ContentItem.MediaFiles.Add(reviewed);
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-msvideo",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.avi",
        };
        _episode.ContentItem.MediaFiles.Add(worse);
        _episode.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Subtitle",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.en.srt",
        });
        _ranks[keep.FilePath] = 1;
        _ranks[worse.FilePath] = 2;
        if (reviewedRank is { } rank)
        {
            _ranks[reviewed.FilePath] = rank;
        }

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

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
        var keep = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/New.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(keep);
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-msvideo",
            FilePath = $"/tv/{_episode.Id:N}/Old.avi",
        };
        _episode.ContentItem.MediaFiles.Add(worse);
        var worst = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/mp4",
            FilePath = $"/tv/{_episode.Id:N}/Old.mp4",
        };
        _episode.ContentItem.MediaFiles.Add(worst);
        var subtitle = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Subtitle",
            FilePath = $"/tv/{_episode.Id:N}/Old.en.srt",
        };
        _episode.ContentItem.MediaFiles.Add(subtitle);
        _ranks[keep.FilePath] = 1;
        _ranks[worse.FilePath] = 2;
        _ranks[worst.FilePath] = 3;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        Assert.That(
            plan.Unlink.Select(row => row.MediaFileId),
            Is.EquivalentTo([worse.Id, worst.Id, subtitle.Id]));
    }

    [Test]
    public async Task PlanAsync_UnlinksEveryExtraAndItsSidecar_WhenAMovieHoldsItsFilmAndNineExtras()
    {
        // Arrange
        var film = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Despicable Me 4 (2024) (1080p BluRay x265 Silence).mkv",
        };
        _movie.ContentItem.MediaFiles.Add(film);
        film.MediaInfo = new() { DurationSeconds = 5663 };
        _movie.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Benny's Birthday.mkv",
            MediaInfo = new() { DurationSeconds = 241 },
        });
        _movie.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Deleted Scenes.mkv",
            MediaInfo = new() { DurationSeconds = 865 },
        });
        _movie.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Subtitle",
            FilePath = $"/tv/{_movie.Id:N}/Deleted Scenes.en.srt",
        });
        _movie.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Despicable Dialogue.mkv",
            MediaInfo = new() { DurationSeconds = 217 },
        });
        _movie.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Game Over and Over.mkv",
            MediaInfo = new() { DurationSeconds = 259 },
        });
        _movie.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/How to Drain.mkv",
            MediaInfo = new() { DurationSeconds = 146 },
        });
        _movie.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Making Of.mkv",
            MediaInfo = new() { DurationSeconds = 657 },
        });
        _movie.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Meet the Cast.mkv",
            MediaInfo = new() { DurationSeconds = 205 },
        });
        _movie.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Mega Monion Mayhem.mkv",
            MediaInfo = new() { DurationSeconds = 262 },
        });
        _movie.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Rogues Gallery.mkv",
            MediaInfo = new() { DurationSeconds = 218 },
        });
        _ranks[film.FilePath] = 1;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Unlink.Select(row => Path.GetFileName(row.FilePath)),
                Is.EquivalentTo(
                [
                    "Benny's Birthday.mkv",
                    "Deleted Scenes.mkv",
                    "Deleted Scenes.en.srt",
                    "Despicable Dialogue.mkv",
                    "Game Over and Over.mkv",
                    "How to Drain.mkv",
                    "Making Of.mkv",
                    "Meet the Cast.mkv",
                    "Mega Monion Mayhem.mkv",
                    "Rogues Gallery.mkv",
                ]));
            Assert.That(
                plan.Unlink.Where(row => row.Primary).Select(row => row.Reason),
                Has.All.EqualTo("The file is an extra."));
            Assert.That(plan.Unlink.Select(row => row.KeepMediaFileId), Has.All.Null);
            Assert.That(plan.Unlink.Select(row => row.CurrentItemId), Has.All.EqualTo(_movie.Id));
            Assert.That(plan.Review, Is.Empty);
        }
    }

    [Test]
    public async Task PlanAsync_KeepsTheFilm_WhenAnExtraHasABetterRank()
    {
        // Arrange
        var film = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Movie (2024) 720p.mkv",
        };
        _movie.ContentItem.MediaFiles.Add(film);
        film.MediaInfo = new() { DurationSeconds = 5663 };
        var extra = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Making Of.mkv",
        };
        _movie.ContentItem.MediaFiles.Add(extra);
        extra.MediaInfo = new() { DurationSeconds = 657 };
        _ranks[film.FilePath] = 2;
        _ranks[extra.FilePath] = 1;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Unlink.Select(row => (row.MediaFileId, row.Reason, row.Rank)),
                Is.EqualTo([(extra.Id, "The file is an extra.", (int?)null)]));
            Assert.That(plan.Review, Is.Empty);
        }
    }

    [TestCase(8478d, null)]
    [TestCase(null, 181d)]
    public async Task PlanAsync_ReviewsTheUnrankedFileAsACopy_WhenADurationIsUnknown(
        double? filmDuration,
        double? extraDuration)
    {
        // Arrange
        var film = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/mp4",
            FilePath = $"/tv/{_movie.Id:N}/Movie (2015) 1080p.mp4",
        };
        _movie.ContentItem.MediaFiles.Add(film);
        film.MediaInfo = new() { DurationSeconds = filmDuration };
        var extra = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Global Adventure.mkv",
        };
        _movie.ContentItem.MediaFiles.Add(extra);
        extra.MediaInfo = new() { DurationSeconds = extraDuration };
        _ranks[film.FilePath] = 1;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Unlink, Is.Empty);
            Assert.That(plan.Review.Select(row => row.MediaFileId), Is.EqualTo([extra.Id]));
            Assert.That(
                plan.Review.Select(row => row.Reason),
                Is.EqualTo(["The copy has no quality rank, so the plan cannot compare it with the others."]));
        }
    }

    [Test]
    public async Task PlanAsync_ReviewsAFeatureLengthVideoAsACopy_WhenItRunsAboutAsLongAsTheFilm()
    {
        // Arrange
        var film = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Despicable Me (2010) (1080p BluRay x265 Silence).mkv",
        };
        _movie.ContentItem.MediaFiles.Add(film);
        film.MediaInfo = new() { DurationSeconds = 5688 };
        var featureLength = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Gru-Control.mkv",
        };
        _movie.ContentItem.MediaFiles.Add(featureLength);
        featureLength.MediaInfo = new() { DurationSeconds = 5777 };
        var extra = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Banana.mkv",
        };
        _movie.ContentItem.MediaFiles.Add(extra);
        extra.MediaInfo = new() { DurationSeconds = 225 };
        _ranks[film.FilePath] = 1;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Unlink.Select(row => row.MediaFileId), Is.EqualTo([extra.Id]));
            Assert.That(plan.Review.Select(row => row.MediaFileId), Is.EqualTo([featureLength.Id]));
        }
    }

    [TestCase("Behind The Scenes/Gag Reel.mkv")]
    [TestCase("Bonus Features/Gag Reel.mkv")]
    [TestCase("Deleted Scenes/Gag Reel.mkv")]
    [TestCase("Extras/Gag Reel.mkv")]
    [TestCase("Featurettes/Gag Reel.mkv")]
    [TestCase("Interviews/Gag Reel.mkv")]
    [TestCase("Other/Gag Reel.mkv")]
    [TestCase("Sample/Gag Reel.mkv")]
    [TestCase("Samples/Gag Reel.mkv")]
    [TestCase("Scenes/Gag Reel.mkv")]
    [TestCase("Shorts/Gag Reel.mkv")]
    [TestCase("Special Features/Gag Reel.mkv")]
    [TestCase("Trailers/Gag Reel.mkv")]
    [TestCase("Trailer.mkv")]
    [TestCase("Set Visit-behindthescenes.mkv")]
    [TestCase("Set Visit-deleted.mkv")]
    [TestCase("Set Visit-featurette.mkv")]
    [TestCase("Set Visit-interview.mkv")]
    [TestCase("Set Visit-other.mkv")]
    [TestCase("Set Visit-scene.mkv")]
    [TestCase("Set Visit-short.mkv")]
    [TestCase("Set Visit-trailer.mkv")]
    [TestCase("Movie.sample.mkv")]
    public async Task PlanAsync_UnlinksAnExtra_WhenItsFolderOrNameMarksIt(string fileName)
    {
        // Arrange
        var film = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Movie (2024).mkv",
        };
        _movie.ContentItem.MediaFiles.Add(film);
        var extra = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/{fileName}",
        };
        _movie.ContentItem.MediaFiles.Add(extra);
        _ranks[film.FilePath] = 1;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Unlink.Select(row => row.MediaFileId), Is.EqualTo([extra.Id]));
            Assert.That(plan.Review, Is.Empty);
        }
    }

    [TestCase(
        "/tv/Extras/Season 1",
        "/tv/Extras/Season 1/Extras - S01E01.mkv",
        "/tv/Extras/Season 1/Extras - S01E01.mp4")]
    [TestCase("/tv/Shorts", "/tv/Shorts/Shorts.mkv", "/tv/Shorts/Shorts.mp4")]
    [TestCase(
        "/tv/Other/Season 1",
        "/tv/Other/Season 1/Other - S01E01.mkv",
        "/tv/Other/Season 1/Other - S01E01.mp4")]
    [TestCase("/movies/Shorts", "/movies/Shorts/Shorts 1080p.mkv", "/movies/Shorts/Shorts 720p.mp4")]
    [TestCase("/tv/Show/Season 1", "/tv/Show/Season 1/Show - S01E01.mkv", "/tv/Other/Show - S01E01.mp4")]
    public async Task PlanAsync_SupersedesTheWorseCopy_WhenOnlyAFolderOutsideTheItemIsNamedLikeAnExtrasFolder(
        string itemFolder,
        string betterPath,
        string worsePath)
    {
        // Arrange
        _folders[_episode.Id] = itemFolder;
        var better = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Better.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(better);
        better.FilePath = betterPath;
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/mp4",
            FilePath = $"/tv/{_episode.Id:N}/Worse.mp4",
        };
        _episode.ContentItem.MediaFiles.Add(worse);
        worse.FilePath = worsePath;
        _ranks[betterPath] = 1;
        _ranks[worsePath] = 2;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Unlink.Select(row => (row.MediaFileId, row.Reason, row.KeepMediaFileId)),
                Is.EqualTo([(worse.Id, "Rank 2 is worse than rank 1 of the copy that stays.", (Guid?)better.Id)]));
            Assert.That(plan.Review, Is.Empty);
        }
    }

    [TestCase("Movie-trailer.mkv", null, "Movie-trailer.mp4", null)]
    [TestCase("Movie (2024).mkv", 5000d, "Featurettes/Making Of.mkv", 12000d)]
    public async Task PlanAsync_SupersedesTheWorseCopy_WhenTheRuleMarksEveryPrimaryFile(
        string betterName,
        double? betterDuration,
        string worseName,
        double? worseDuration)
    {
        // Arrange
        var better = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/{betterName}",
        };
        _movie.ContentItem.MediaFiles.Add(better);
        better.MediaInfo = new() { DurationSeconds = betterDuration };
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/{worseName}",
        };
        _movie.ContentItem.MediaFiles.Add(worse);
        worse.MediaInfo = new() { DurationSeconds = worseDuration };
        _ranks[better.FilePath] = 1;
        _ranks[worse.FilePath] = 2;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Unlink.Select(row => (row.MediaFileId, row.Reason, row.KeepMediaFileId)),
                Is.EqualTo([(worse.Id, "Rank 2 is worse than rank 1 of the copy that stays.", (Guid?)better.Id)]));
            Assert.That(plan.Review, Is.Empty);
        }
    }

    [Test]
    public async Task PlanAsync_SupersedesTheWorseCopy_WhenTheRuleMarksEveryPrimaryFileBesideASidecar()
    {
        // Arrange
        var better = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Movie-trailer.mkv",
        };
        _movie.ContentItem.MediaFiles.Add(better);
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/mp4",
            FilePath = $"/tv/{_movie.Id:N}/Movie-trailer.mp4",
        };
        _movie.ContentItem.MediaFiles.Add(worse);
        _movie.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Subtitle",
            FilePath = $"/tv/{_movie.Id:N}/Movie-trailer.en.srt",
        });
        _ranks[better.FilePath] = 1;
        _ranks[worse.FilePath] = 2;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Unlink.Select(row => (row.MediaFileId, row.Reason, row.KeepMediaFileId)),
                Is.EqualTo([(worse.Id, "Rank 2 is worse than rank 1 of the copy that stays.", (Guid?)better.Id)]));
            Assert.That(plan.Review, Is.Empty);
        }
    }

    [TestCase("/movies/Shorts/Making Of.mkv", 200d)]
    [TestCase("/movies/Shorts/Featurettes/Gag Reel.mkv", null)]
    public async Task PlanAsync_UnlinksOnlyTheExtra_WhenTheItemsOwnFolderIsNamedLikeAnExtrasFolder(
        string extraPath,
        double? extraDuration)
    {
        // Arrange
        _folders[_movie.Id] = "/movies/Shorts";
        var film = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Film.mkv",
        };
        _movie.ContentItem.MediaFiles.Add(film);
        film.FilePath = "/movies/Shorts/Shorts (2024).mkv";
        film.MediaInfo = new() { DurationSeconds = 5000 };
        var extra = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Extra.mkv",
        };
        _movie.ContentItem.MediaFiles.Add(extra);
        extra.FilePath = extraPath;
        extra.MediaInfo = new() { DurationSeconds = extraDuration };
        _ranks[film.FilePath] = 1;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plan.Unlink.Select(row => (row.MediaFileId, row.Reason)),
                Is.EqualTo([(extra.Id, "The file is an extra.")]));
            Assert.That(plan.Review, Is.Empty);
        }
    }

    [TestCase(2499d, MediaFileCleanupAction.Unlink)]
    [TestCase(2501d, MediaFileCleanupAction.Review)]
    public async Task PlanAsync_ListsAVideoAsAnExtraOnlyUnderHalfTheLongestVideo_WhenItRunsNearHalf(
        double duration,
        MediaFileCleanupAction expectedAction)
    {
        // Arrange
        var film = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Movie (2024).mkv",
        };
        _movie.ContentItem.MediaFiles.Add(film);
        film.MediaInfo = new() { DurationSeconds = 5000 };
        var video = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Alternate Cut.mkv",
        };
        _movie.ContentItem.MediaFiles.Add(video);
        video.MediaInfo = new() { DurationSeconds = duration };
        _ranks[film.FilePath] = 1;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        Assert.That(
            plan.Unlink.Concat(plan.Review).Select(row => (row.MediaFileId, row.Action)),
            Is.EqualTo([(video.Id, expectedAction)]));
    }

    [TestCase(3600d, 600d)]
    [TestCase(600d, 3600d)]
    public async Task PlanAsync_ComparesAudioAndVideoAsCopies_WhenTheAudioOrTheVideoIsShorter(
        double videoDuration,
        double audioDuration)
    {
        // Arrange
        var video = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _recording.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_recording.Id:N}/Recording.mkv",
        };
        _recording.ContentItem.MediaFiles.Add(video);
        video.MediaInfo = new() { DurationSeconds = videoDuration };
        var audio = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _recording.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "audio/mpeg",
            FilePath = $"/tv/{_recording.Id:N}/Recording.mp3",
        };
        _recording.ContentItem.MediaFiles.Add(audio);
        audio.FileKind = "Audio";
        audio.MediaInfo = new() { DurationSeconds = audioDuration };

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Unlink, Is.Empty);
            Assert.That(plan.Review.Select(row => row.MediaFileId), Is.EquivalentTo([video.Id, audio.Id]));
        }
    }

    [Test]
    public async Task PlanAsync_ReassignsAContainerFileToItsFirstItemAndCoversTheOthers_WhenItsNameCoversSeveralItems()
    {
        // Arrange
        _provider = _router.Object;
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01-E03.mkv",
        };
        _season.ContentItem.MediaFiles.Add(file);
        _signalValues[file.FilePath] = ["1", "2", "3"];
        _routes[file.FilePath] = [_episode.Id, _secondEpisode.Id, _thirdEpisode.Id];

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Reassign.Single().MediaFileId, Is.EqualTo(file.Id));
            Assert.That(plan.Reassign.Single().TargetItemId, Is.EqualTo(_episode.Id));
            Assert.That(plan.Reassign.Single().CoveredItemIds, Is.EqualTo([_secondEpisode.Id, _thirdEpisode.Id]));
            Assert.That(
                plan.Reassign.Single().CoveredItems,
                Is.EqualTo(["Show / Season 1 / Second", "Show / Season 1 / Third"]));
            Assert.That(
                plan.Reassign.Single().Reason,
                Is.EqualTo("The binding matched episode 'Pilot'. The file name also covers episode 'Second', " +
                    "episode 'Third'."));
            Assert.That(plan.Cover, Is.Empty);
            Assert.That(plan.Review, Is.Empty);
        }
    }

    [Test]
    public async Task PlanAsync_CoversTheOtherItems_WhenAFileOnItsFirstItemNamesSeveralItems()
    {
        // Arrange
        _provider = _router.Object;
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01-E03.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(file);
        var subtitle = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Subtitle",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01-E03.en.srt",
        };
        _episode.ContentItem.MediaFiles.Add(subtitle);
        _signalValues[file.FilePath] = ["1", "2", "3"];
        _signalValues[subtitle.FilePath] = ["1", "2", "3"];
        _routes[file.FilePath] = [_episode.Id, _secondEpisode.Id, _thirdEpisode.Id];
        _routes[subtitle.FilePath] = [_episode.Id, _secondEpisode.Id, _thirdEpisode.Id];

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        var row = plan.Cover.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.MediaFileId, Is.EqualTo(file.Id));
            Assert.That(row.Action, Is.EqualTo(MediaFileCleanupAction.Cover));
            Assert.That(row.CurrentItemId, Is.EqualTo(_episode.Id));
            Assert.That(row.TargetItemId, Is.EqualTo(_episode.Id));
            Assert.That(row.TargetItem, Is.EqualTo("Show / Season 1 / Pilot"));
            Assert.That(row.CoveredItemIds, Is.EqualTo([_secondEpisode.Id, _thirdEpisode.Id]));
            Assert.That(row.CoveredItems, Is.EqualTo(["Show / Season 1 / Second", "Show / Season 1 / Third"]));
            Assert.That(row.Reason, Is.EqualTo("The file name also covers episode 'Second', episode 'Third'."));
            Assert.That(plan.Reassign, Is.Empty);
            Assert.That(plan.Review, Is.Empty);
        }
    }

    [Test]
    public async Task PlanAsync_CoversTheUncoveredItems_WhenTheFileCoversSomeItems()
    {
        // Arrange
        _provider = _router.Object;
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01-E03.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(file);
        file.Coverage.Add(new() { MediaFileId = file.Id, ContentItemId = _secondEpisode.ContentItem!.Id });
        _secondEpisode.ContentItem.CoveringMediaFiles.Add(file);
        _signalValues[file.FilePath] = ["1", "2", "3"];
        _routes[file.FilePath] = [_episode.Id, _secondEpisode.Id, _thirdEpisode.Id];

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Cover.Single().CoveredItemIds, Is.EqualTo([_thirdEpisode.Id]));
            Assert.That(plan.Cover.Single().Reason, Is.EqualTo("The file name also covers episode 'Third'."));
            Assert.That(plan.Review, Is.Empty);
        }
    }

    [Test]
    public async Task PlanAsync_ProposesTheLowestItemAsOwner_WhenTheFileSitsOnALaterItem()
    {
        // Arrange
        _provider = _router.Object;
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _secondEpisode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_secondEpisode.Id:N}/Show - S01E01-E03.mkv",
        };
        _secondEpisode.ContentItem.MediaFiles.Add(file);
        _signalValues[file.FilePath] = ["1", "2", "3"];
        _routes[file.FilePath] = [_episode.Id, _secondEpisode.Id, _thirdEpisode.Id];

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        var row = plan.Cover.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.CurrentItemId, Is.EqualTo(_secondEpisode.Id));
            Assert.That(row.TargetItemId, Is.EqualTo(_episode.Id));
            Assert.That(row.CoveredItemIds, Is.EqualTo([_episode.Id, _thirdEpisode.Id]));
        }
    }

    [Test]
    public async Task PlanAsync_KeepsTheOwner_WhenTheLowerItemHasItsOwnPrimaryFile()
    {
        // Arrange
        _provider = _router.Object;
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _secondEpisode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_secondEpisode.Id:N}/Show - S01E01-E03.mkv",
        };
        _secondEpisode.ContentItem.MediaFiles.Add(file);
        _episode.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.mkv",
        });
        _signalValues[file.FilePath] = ["1", "2", "3"];
        _routes[file.FilePath] = [_episode.Id, _secondEpisode.Id, _thirdEpisode.Id];

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        var row = plan.Cover.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.TargetItemId, Is.EqualTo(_secondEpisode.Id));
            Assert.That(row.CoveredItemIds, Is.EqualTo([_thirdEpisode.Id]));
            Assert.That(
                row.Reason,
                Is.EqualTo("The file name also covers episode 'Third'. The file does not cover episode 'Pilot', " +
                    "which already has a primary file."));
        }
    }

    [Test]
    public async Task PlanAsync_LeavesOutAnItem_WhenItAlreadyHasItsOwnPrimaryFile()
    {
        // Arrange
        _provider = _router.Object;
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01-E03.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(file);
        _secondEpisode.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _secondEpisode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_secondEpisode.Id:N}/Show - S01E02.mkv",
        });
        _signalValues[file.FilePath] = ["1", "2", "3"];
        _routes[file.FilePath] = [_episode.Id, _secondEpisode.Id, _thirdEpisode.Id];

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Cover.Single().CoveredItemIds, Is.EqualTo([_thirdEpisode.Id]));
            Assert.That(plan.Cover.Single().CoveredItems, Is.EqualTo(["Show / Season 1 / Third"]));
            Assert.That(
                plan.Cover.Single().Reason,
                Is.EqualTo("The file name also covers episode 'Third'. The file does not cover episode 'Second', " +
                    "which already has a primary file."));
        }
    }

    [Test]
    public async Task PlanAsync_LeavesOutAnItemFromAContainerFile_WhenItAlreadyHasItsOwnPrimaryFile()
    {
        // Arrange
        _provider = _router.Object;
        var container = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01-E03.mkv",
        };
        _season.ContentItem.MediaFiles.Add(container);
        _secondEpisode.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _secondEpisode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_secondEpisode.Id:N}/Show - S01E02.mkv",
        });
        _signalValues[container.FilePath] = ["1", "2", "3"];
        _routes[container.FilePath] = [_episode.Id, _secondEpisode.Id, _thirdEpisode.Id];

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Reassign.Single().CoveredItemIds, Is.EqualTo([_thirdEpisode.Id]));
            Assert.That(plan.Reassign.Single().CoveredItems, Is.EqualTo(["Show / Season 1 / Third"]));
            Assert.That(
                plan.Reassign.Single().Reason,
                Is.EqualTo("The binding matched episode 'Pilot'. The file name also covers episode 'Third'. " +
                    "The file does not cover episode 'Second', which already has a primary file."));
        }
    }

    [Test]
    public async Task PlanAsync_ListsNoCoverRow_WhenEveryOtherNamedItemHasItsOwnPrimaryFile()
    {
        // Arrange
        _provider = _router.Object;
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01-E03.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(file);
        _secondEpisode.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _secondEpisode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_secondEpisode.Id:N}/Show - S01E02.mkv",
        });
        _thirdEpisode.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _thirdEpisode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_thirdEpisode.Id:N}/Show - S01E03.mkv",
        });
        _signalValues[file.FilePath] = ["1", "2", "3"];
        _routes[file.FilePath] = [_episode.Id, _secondEpisode.Id, _thirdEpisode.Id];

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Cover, Is.Empty);
            Assert.That(plan.Review, Is.Empty);
        }
    }

    [Test]
    public async Task PlanAsync_CoversNothingFromAContainerFile_WhenEveryOtherNamedItemHasItsOwnPrimaryFile()
    {
        // Arrange
        _provider = _router.Object;
        var container = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01-E03.mkv",
        };
        _season.ContentItem.MediaFiles.Add(container);
        _secondEpisode.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _secondEpisode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_secondEpisode.Id:N}/Show - S01E02.mkv",
        });
        _thirdEpisode.ContentItem!.MediaFiles.Add(new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _thirdEpisode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_thirdEpisode.Id:N}/Show - S01E03.mkv",
        });
        _signalValues[container.FilePath] = ["1", "2", "3"];
        _routes[container.FilePath] = [_episode.Id, _secondEpisode.Id, _thirdEpisode.Id];

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Reassign.Single().CoveredItemIds, Is.Empty);
            Assert.That(plan.Reassign.Single().CoveredItems, Is.Empty);
            Assert.That(
                plan.Reassign.Single().Reason,
                Is.EqualTo("The binding matched episode 'Pilot'. The file does not cover episode 'Second', " +
                    "episode 'Third'; each already has a primary file."));
        }
    }

    [Test]
    public async Task PlanAsync_ListsNoCoverRow_WhenTheDuplicateCheckReviewsTheFile()
    {
        // Arrange
        _provider = _router.Object;
        _upgradeFileHandling = UpgradeFileHandling.KeepBoth;
        var better = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01-E02 1080p.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(better);
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01-E02 720p.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(worse);
        _signalValues[better.FilePath] = ["1", "2"];
        _signalValues[worse.FilePath] = ["1", "2"];
        _routes[better.FilePath] = [_episode.Id, _secondEpisode.Id];
        _routes[worse.FilePath] = [_episode.Id, _secondEpisode.Id];
        _ranks[better.FilePath] = 1;
        _ranks[worse.FilePath] = 3;

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Review.Single().MediaFileId, Is.EqualTo(worse.Id));
            Assert.That(plan.Cover.Single().MediaFileId, Is.EqualTo(better.Id));
        }
    }

    [Test]
    public async Task PlanAsync_ReviewsAFile_WhenItsNameNamesAnItemThatTheHierarchyLacks()
    {
        // Arrange
        _provider = _router.Object;
        var onEpisode = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01-E04.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(onEpisode);
        var onSeason = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E03-E04.mkv",
        };
        _season.ContentItem.MediaFiles.Add(onSeason);
        var single = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _secondEpisode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_secondEpisode.Id:N}/Show - S01E09.mkv",
        };
        _secondEpisode.ContentItem.MediaFiles.Add(single);
        _signalValues[onEpisode.FilePath] = ["1", "2", "3", "4"];
        _signalValues[onSeason.FilePath] = ["3", "4"];
        _signalValues[single.FilePath] = ["9"];

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Review.Select(row => row.MediaFileId), Is.EquivalentTo([onEpisode.Id, onSeason.Id]));
            Assert.That(
                plan.Review.Select(row => row.Reason),
                Has.All.EqualTo("The binding matched no single item below season 'Season 1'."));
            Assert.That(plan.Cover, Is.Empty);
            Assert.That(plan.Reassign, Is.Empty);
        }
    }

    [Test]
    public async Task PlanAsync_ReviewsAFile_WhenItsNameDoesNotNameTheItemThatOwnsIt()
    {
        // Arrange
        _provider = _router.Object;
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _thirdEpisode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_thirdEpisode.Id:N}/Show - S01E01-E02.mkv",
        };
        _thirdEpisode.ContentItem.MediaFiles.Add(file);
        _signalValues[file.FilePath] = ["1", "2"];
        _routes[file.FilePath] = [_episode.Id, _secondEpisode.Id];

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Review.Single().MediaFileId, Is.EqualTo(file.Id));
            Assert.That(
                plan.Review.Single().Reason,
                Is.EqualTo("The file name does not name episode 'Third', which owns the file."));
            Assert.That(plan.Cover, Is.Empty);
        }
    }

    [Test]
    public async Task PlanAsync_StillUnlinksAnExtra_WhenItsItemAlsoHoldsAFileThatCoversSeveralItems()
    {
        // Arrange
        _provider = _router.Object;
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01-E02.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(file);
        var extra = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Extras/Show - S01E01-E02 Gag Reel.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(extra);
        _signalValues[file.FilePath] = ["1", "2"];
        _signalValues[extra.FilePath] = ["1", "2"];
        _routes[file.FilePath] = [_episode.Id, _secondEpisode.Id];
        _routes[extra.FilePath] = [_episode.Id, _secondEpisode.Id];

        // Act
        var plan = await _subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Unlink.Single().MediaFileId, Is.EqualTo(extra.Id));
            Assert.That(plan.Unlink.Single().Reason, Is.EqualTo("The file is an extra."));
            Assert.That(plan.Cover.Single().MediaFileId, Is.EqualTo(file.Id));
            Assert.That(plan.Review, Is.Empty);
        }
    }

    [Test]
    public async Task ApplyAsync_RunsTheRow_WhenTheRowIsApprovedAndStillMatchesThePlan()
    {
        // Arrange
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01 480p.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(worse);
        var better = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01 1080p.mkv",
        };
        _season.ContentItem.MediaFiles.Add(better);
        _bindings[better.FilePath] = _episode.Id;
        _ranks[worse.FilePath] = 3;
        _ranks[better.FilePath] = 1;
        var subject = _subject;
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
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01 480p.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(worse);
        var better = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01 1080p.mkv",
        };
        _season.ContentItem.MediaFiles.Add(better);
        _bindings[better.FilePath] = _episode.Id;
        _ranks[worse.FilePath] = 3;
        _ranks[better.FilePath] = 1;
        var subject = _subject;
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
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ApplyAsync_KeepsTheItemsCopy_WhenTheReassignOfTheKeptCopyFails(bool throws)
    {
        // Arrange
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01 480p.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(worse);
        var better = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01 1080p.mkv",
        };
        _season.ContentItem.MediaFiles.Add(better);
        _bindings[better.FilePath] = _episode.Id;
        _ranks[worse.FilePath] = 3;
        _ranks[better.FilePath] = 1;
        var subject = _subject;
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
    }

    [Test]
    public async Task ApplyAsync_RefusesADelete_WhenTheKeptCopyIsNotOnDisk()
    {
        // Arrange
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01 480p.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(worse);
        var better = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01 1080p.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(better);
        _ranks[worse.FilePath] = 3;
        _ranks[better.FilePath] = 1;
        _missingOnDisk.Add(better.FilePath);
        var subject = _subject;
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
    }

    [Test]
    public async Task ApplyAsync_TreatsARemovalRowAsStale_WhenTheKeptCopyChanged()
    {
        // Arrange
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01 480p.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(worse);
        var better = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01 1080p.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(better);
        _ranks[worse.FilePath] = 3;
        _ranks[better.FilePath] = 2;
        var subject = _subject;
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);
        var best = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01 2160p.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(best);
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
    }

    [Test]
    public async Task ApplyAsync_SkipsTheRow_WhenTheDryRunDidNotListTheSidecarRow()
    {
        // Arrange
        var keep = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/A.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(keep);
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-msvideo",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.avi",
        };
        _episode.ContentItem.MediaFiles.Add(worse);
        var subtitle = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Subtitle",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.en.srt",
        };
        _episode.ContentItem.MediaFiles.Add(subtitle);
        _ranks[keep.FilePath] = 1;
        _ranks[worse.FilePath] = 3;
        var subject = _subject;
        var approved = (await subject.PlanAsync(null).ConfigureAwait(false)).Unlink
            .Single(row => row.MediaFileId == subtitle.Id);
        var arriving = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01.mkv",
        };
        _season.ContentItem.MediaFiles.Add(arriving);
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
        var video = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01.mkv",
        };
        _season.ContentItem.MediaFiles.Add(video);
        _bindings[video.FilePath] = _episode.Id;
        var subject = _subject;
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
    public async Task ApplyAsync_SkipsTheRow_WhenTheRowIsAReviewRow()
    {
        // Arrange
        var first = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(first);
        var second = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/mp4",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01.mp4",
        };
        _episode.ContentItem.MediaFiles.Add(second);
        _ranks[first.FilePath] = 1;
        _ranks[second.FilePath] = 1;
        var subject = _subject;
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
    }

    [Test]
    public async Task ApplyAsync_UnlinksAnExtraAndItsSidecar_WhenTheRowsAreApproved()
    {
        // Arrange
        var film = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Movie (2024).mkv",
        };
        _movie.ContentItem.MediaFiles.Add(film);
        film.MediaInfo = new() { DurationSeconds = 5663 };
        var extra = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Deleted Scenes.mkv",
        };
        _movie.ContentItem.MediaFiles.Add(extra);
        extra.MediaInfo = new() { DurationSeconds = 865 };
        var subtitle = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Subtitle",
            FilePath = $"/tv/{_movie.Id:N}/Deleted Scenes.en.srt",
        };
        _movie.ContentItem.MediaFiles.Add(subtitle);
        var subject = _subject;
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);

        // Act
        var results = await subject.ApplyAsync(new() { Rows = plan.Unlink }, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(result => result.Applied), Is.EqualTo([true, true]));
            _contentService.Verify(
                service => service.UnlinkFileAsync(_movie.Id, extra.Id, null, It.IsAny<CancellationToken>()),
                Times.Once);
            _contentService.Verify(
                service => service.UnlinkFileAsync(_movie.Id, subtitle.Id, null, It.IsAny<CancellationToken>()),
                Times.Once);
            _contentService.Verify(
                service => service.UnlinkFileAsync(
                    It.IsAny<Guid>(),
                    film.Id,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
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
    public async Task ApplyAsync_RefusesADelete_WhenTheRowIsAnExtraOrItsSidecar()
    {
        // Arrange
        var film = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Movie (2024) 1080p.mkv",
        };
        _movie.ContentItem.MediaFiles.Add(film);
        film.MediaInfo = new() { DurationSeconds = 5663 };
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Movie (2024) 480p.mkv",
        };
        _movie.ContentItem.MediaFiles.Add(worse);
        worse.MediaInfo = new() { DurationSeconds = 5663 };
        var extra = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_movie.Id:N}/Deleted Scenes.mkv",
        };
        _movie.ContentItem.MediaFiles.Add(extra);
        extra.MediaInfo = new() { DurationSeconds = 865 };
        var subtitle = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _movie.ContentItem!.Id,
            FileKind = "Subtitle",
            FilePath = $"/tv/{_movie.Id:N}/Deleted Scenes.en.srt",
        };
        _movie.ContentItem.MediaFiles.Add(subtitle);
        _ranks[film.FilePath] = 1;
        _ranks[worse.FilePath] = 3;
        var subject = _subject;
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);

        // Act
        var results = await subject
            .ApplyAsync(
                new()
                {
                    Rows =
                    [
                        plan.Unlink.Single(row => row.MediaFileId == extra.Id) with
                        {
                            Action = MediaFileCleanupAction.Delete
                        },
                        plan.Unlink.Single(row => row.MediaFileId == subtitle.Id) with
                        {
                            Action = MediaFileCleanupAction.Delete
                        },
                    ]
                },
                null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(result => result.Applied), Is.EqualTo([false, false]));
            Assert.That(
                results.Select(result => result.Message),
                Has.All.EqualTo("No copy stays in place of this file, so it is not deleted."));
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
    }

    [Test]
    public async Task ApplyAsync_ReassignsAndCoversThroughTheFileOperations_WhenAContainerFileCoversSeveralItems()
    {
        // Arrange
        _provider = _router.Object;
        var video = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01-E02.mkv",
        };
        _season.ContentItem.MediaFiles.Add(video);
        var subtitle = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Subtitle",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01-E02.en.srt",
        };
        _season.ContentItem.MediaFiles.Add(subtitle);
        _signalValues[video.FilePath] = ["1", "2"];
        _signalValues[subtitle.FilePath] = ["1", "2"];
        _routes[video.FilePath] = [_episode.Id, _secondEpisode.Id];
        _routes[subtitle.FilePath] = [_episode.Id, _secondEpisode.Id];
        var subject = _subject;
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);

        // Act
        var results = await subject
            .ApplyAsync(new() { Rows = [.. plan.Reassign.OrderByDescending(row => row.Primary)] }, null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(result => result.Applied), Is.EqualTo([true, true]));
            _contentService.Verify(
                service => service.ReassignFileAsync(
                    _season.Id,
                    video.Id,
                    It.Is<ReassignContentFileRequest>(reassign => reassign.TargetContentHierarchyItemId == _episode.Id),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentService.Verify(
                service => service.AttachFileAsync(
                    _secondEpisode.Id,
                    video.FilePath,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentService.Verify(
                service => service.AttachFileAsync(
                    _secondEpisode.Id,
                    subtitle.FilePath,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
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
    }

    [Test]
    public async Task ApplyAsync_CoversTheOtherItemsThroughTheAttach_WhenACoverRowIsApproved()
    {
        // Arrange
        _provider = _router.Object;
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01-E03.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(file);
        _signalValues[file.FilePath] = ["1", "2", "3"];
        _routes[file.FilePath] = [_episode.Id, _secondEpisode.Id, _thirdEpisode.Id];
        var subject = _subject;
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);

        // Act
        var results = await subject.ApplyAsync(new() { Rows = [plan.Cover.Single()] }, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Single().Applied, Is.True);
            Assert.That(results.Single().Action, Is.EqualTo(MediaFileCleanupAction.Cover));
            _contentService.Verify(
                service => service.AttachFileAsync(
                    _secondEpisode.Id,
                    file.FilePath,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentService.Verify(
                service => service.AttachFileAsync(
                    _thirdEpisode.Id,
                    file.FilePath,
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentService.Verify(
                service => service.ReassignFileAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<ReassignContentFileRequest>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
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
    }

    [Test]
    public async Task ApplyAsync_SkipsACoverRow_WhenItsCoveredItemsNoLongerMatchThePlan()
    {
        // Arrange
        _provider = _router.Object;
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01-E03.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(file);
        _signalValues[file.FilePath] = ["1", "2", "3"];
        _routes[file.FilePath] = [_episode.Id, _secondEpisode.Id, _thirdEpisode.Id];
        var subject = _subject;
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);

        // Act
        var results = await subject
            .ApplyAsync(new() { Rows = [plan.Cover.Single() with { CoveredItemIds = [_thirdEpisode.Id] }] }, null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Single().Applied, Is.False);
            Assert.That(results.Single().Message, Is.EqualTo("The row no longer matches the current plan."));
            _contentService.Verify(
                service => service.AttachFileAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ApplyAsync_AppliesARow_WhenTheApiBindsItsCoveredItemIdsFromNull()
    {
        // Arrange
        var video = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01.mkv",
        };
        _season.ContentItem.MediaFiles.Add(video);
        _bindings[video.FilePath] = _episode.Id;
        var options = new JsonSerializerOptions(JsonSerializerOptions.Web);
        MagpieHostBuilderExtensions.ConfigureApiJsonOptions(options);
        var request = JsonSerializer.Deserialize<MediaFileCleanupApplyRequest>(
            $$"""
            {
              "rows": [
                {
                  "mediaFileId": "{{video.Id}}",
                  "filePath": "{{video.FilePath}}",
                  "primary": true,
                  "currentItemId": "{{_season.Id}}",
                  "currentItem": "Show / Season 1",
                  "targetItemId": "{{_episode.Id}}",
                  "coveredItemIds": null,
                  "action": "Reassign",
                  "reason": "The binding matched episode 'Pilot'."
                }
              ]
            }
            """,
            options)!;

        // Act
        var results = await _subject.ApplyAsync(request, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Rows.Single().CoveredItemIds, Is.Empty);
            Assert.That(results.Single().Applied, Is.True);
        }
    }

    [Test]
    public async Task ApplyAsync_RunsEachRowInItsOwnTransaction_WhenSeveralRowsAreApplied()
    {
        // Arrange
        _provider = _router.Object;
        var fourthEpisode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "fourth",
            Name = "Fourth",
            EffectiveRootFolderId = Guid.NewGuid(),
        };
        fourthEpisode.ContentItem = new ContentItem { Id = Guid.NewGuid(), ContentHierarchyItemId = fourthEpisode.Id };
        _items.Add(fourthEpisode);
        var container = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01-E02.mkv",
        };
        _season.ContentItem.MediaFiles.Add(container);
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _thirdEpisode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_thirdEpisode.Id:N}/Show - S01E03-E04.mkv",
        };
        _thirdEpisode.ContentItem.MediaFiles.Add(file);
        _signalValues[container.FilePath] = ["1", "2"];
        _signalValues[file.FilePath] = ["3", "4"];
        _routes[container.FilePath] = [_episode.Id, _secondEpisode.Id];
        _routes[file.FilePath] = [_thirdEpisode.Id, fourthEpisode.Id];
        var subject = _subject;
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);

        // Act
        var results = await subject
            .ApplyAsync(new() { Rows = [plan.Reassign.Single(), plan.Cover.Single()] }, null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(result => result.Applied), Is.EqualTo([true, true]));
            Assert.That(
                _operations,
                Is.EqualTo(
                [
                    ("reassign", _episode.Id, (int?)1),
                    ("attach", _secondEpisode.Id, (int?)1),
                    ("attach", fourthEpisode.Id, (int?)2)
                ]));
            Assert.That(_rolledBack, Is.Empty);
        }
    }

    [Test]
    public async Task ApplyAsync_RollsBackTheWholeRow_WhenACoveredItemAlreadyHasAPrimaryFile()
    {
        // Arrange
        _provider = _router.Object;
        var fourthEpisode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = _season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "fourth",
            Name = "Fourth",
            EffectiveRootFolderId = Guid.NewGuid(),
        };
        fourthEpisode.ContentItem = new ContentItem { Id = Guid.NewGuid(), ContentHierarchyItemId = fourthEpisode.Id };
        _items.Add(fourthEpisode);
        var container = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E03-E04.mkv",
        };
        _season.ContentItem.MediaFiles.Add(container);
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01-E04.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(file);
        _signalValues[container.FilePath] = ["3", "4"];
        _signalValues[file.FilePath] = ["1", "2", "3", "4"];
        _routes[container.FilePath] = [_thirdEpisode.Id, fourthEpisode.Id];
        _routes[file.FilePath] = [_episode.Id, _secondEpisode.Id, _thirdEpisode.Id, fourthEpisode.Id];
        var subject = _subject;
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);

        // Act
        var results = await subject
            .ApplyAsync(new() { Rows = [plan.Reassign.Single(), plan.Cover.Single()] }, null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(result => result.Applied), Is.EqualTo([true, false]));
            Assert.That(results[1].Message, Is.EqualTo("The episode 'Third' already has a primary file."));
            Assert.That(
                _operations,
                Is.EqualTo(
                [
                    ("reassign", _thirdEpisode.Id, (int?)1),
                    ("attach", fourthEpisode.Id, (int?)1),
                    ("attach", _secondEpisode.Id, (int?)2)
                ]));
            Assert.That(_rolledBack, Is.EqualTo([2]));
        }
    }

    [TestCase(UpgradeFileHandling.Replace)]
    [TestCase(UpgradeFileHandling.KeepBoth)]
    public async Task ApplyAsync_CoversWithTheBetterCopyOnly_WhenAWorseCopyOnTheContainerNamesTheSameItems(
        UpgradeFileHandling handling)
    {
        // Arrange
        _provider = _router.Object;
        _upgradeFileHandling = handling;
        var worse = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _season.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_season.Id:N}/Show - S01E01-E02 720p.mkv",
        };
        _season.ContentItem.MediaFiles.Add(worse);
        var better = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01-E02 1080p.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(better);
        _signalValues[worse.FilePath] = ["1", "2"];
        _signalValues[better.FilePath] = ["1", "2"];
        _routes[worse.FilePath] = [_episode.Id, _secondEpisode.Id];
        _routes[better.FilePath] = [_episode.Id, _secondEpisode.Id];
        _ranks[worse.FilePath] = 3;
        _ranks[better.FilePath] = 1;
        var subject = _subject;
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);

        // Act
        var results = await subject
            .ApplyAsync(new() { Rows = [.. plan.Reassign, .. plan.Cover, .. plan.Unlink] }, null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Reassign.Single().MediaFileId, Is.EqualTo(worse.Id));
            Assert.That(plan.Reassign.Single().CoveredItemIds, Is.Empty);
            Assert.That(plan.Reassign.Single().CoveredItems, Is.Empty);
            Assert.That(plan.Reassign.Single().Reason, Is.EqualTo("The binding matched episode 'Pilot'."));
            Assert.That(plan.Unlink.Concat(plan.Review).Single().MediaFileId, Is.EqualTo(worse.Id));
            Assert.That(plan.Cover.Single().MediaFileId, Is.EqualTo(better.Id));
            Assert.That(results.Select(result => result.Applied), Has.All.True);
            Assert.That(_secondEpisode.ContentItem!.CoveringMediaFiles, Is.EqualTo([better]));
        }
    }

    [Test]
    public async Task PlanAsync_ListsNoCoverRow_WhenACoverRowWasApplied()
    {
        // Arrange
        _provider = _router.Object;
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            ContentItemId = _episode.ContentItem!.Id,
            FileKind = "Video",
            MimeType = "video/x-matroska",
            FilePath = $"/tv/{_episode.Id:N}/Show - S01E01-E03.mkv",
        };
        _episode.ContentItem.MediaFiles.Add(file);
        _signalValues[file.FilePath] = ["1", "2", "3"];
        _routes[file.FilePath] = [_episode.Id, _secondEpisode.Id, _thirdEpisode.Id];
        var subject = _subject;
        var plan = await subject.PlanAsync(null).ConfigureAwait(false);
        await subject.ApplyAsync(new() { Rows = [plan.Cover.Single()] }, null).ConfigureAwait(false);

        // Act
        var after = await subject.PlanAsync(null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(after.Cover, Is.Empty);
            Assert.That(after.Review, Is.Empty);
        }
    }
}
