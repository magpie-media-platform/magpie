using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Releases;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Models.Patch;
using Magpie.Sdk.Content;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Releases;

[TestFixture]
public sealed class ReleaseUnmatchTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private ReleaseService _subject = null!;
    private Guid _releaseId;
    private Guid _seasonId;
    private Guid _episodeId;
    private Guid _otherSeasonId;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        using var context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(_template)
            .Options);
        context.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void DisposeTemplate()
    {
        _template.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options);

        var season = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainer = new MetadataContainer(),
            ContentType = "tv",
            Level = "season",
            Slug = "season-1",
            Name = "Season 1",
            CreatedAt = DateTime.UtcNow
        };
        var episode = new ContentHierarchyItem
        {
            MetadataContainer = new MetadataContainer(),
            ParentId = season.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-1",
            Name = "Episode 1",
            CreatedAt = DateTime.UtcNow
        };
        var otherSeason = new ContentHierarchyItem
        {
            MetadataContainer = new MetadataContainer(),
            ContentType = "tv",
            Level = "season",
            Slug = "season-2",
            Name = "Season 2",
            CreatedAt = DateTime.UtcNow
        };
        var release = new Release
        {
            Protocol = "torrent",
            ExternalId = "pack",
            Title = "Show.S01.1080p",
            PublishDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.AddRange(season, episode, otherSeason, release);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();
        _releaseId = release.Id;
        _seasonId = season.Id;
        _episodeId = episode.Id;
        _otherSeasonId = otherSeason.Id;

        var hierarchy = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        hierarchy
            .Setup(repository => repository.QueryAsync(
                It.Is<ContentHierarchyItemCriteria>(criteria => criteria.AncestorId == _seasonId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem> { Items = [episode] });
        ITransactionManager transactionManager = new TransactionManager(
            _context,
            NullLogger<TransactionManager>.Instance);
        var scopes = new ReleaseScopeRepository(
            _context,
            transactionManager,
            Mock.Of<IAuditRecordRepository>(),
            NullLogger<ReleaseScopeRepository>.Instance,
            TimeProvider.System);
        var downloads = Mock.Of<IDownloadRepository>();
        _subject = new ReleaseService(
            hierarchy.Object,
            Mock.Of<IIndexerRepository>(),
            Mock.Of<IReleaseRepository>(),
            scopes,
            Mock.Of<IDownloadScopeRepository>(),
            downloads,
            new ReleaseBlockList(downloads),
            Mock.Of<IDownloadClientRepository>(),
            [],
            Mock.Of<IDownloadService>(),
            Mock.Of<IRootFolderService>(),
            Mock.Of<IFileSystem>(),
            Mock.Of<IAuditRecordRepository>(),
            Mock.Of<IJobManager>(),
            Mock.Of<IJobExecutionRepository>(),
            transactionManager,
            Mock.Of<IEventBus>(),
            Mock.Of<IReleaseEvaluationService>(),
            [],
            TimeProvider.System,
            NullLogger<ReleaseService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [TestCase(ReleaseScopeStatus.Unmatched, ReleaseScopeStatus.Matched)]
    [TestCase(ReleaseScopeStatus.Matched, ReleaseScopeStatus.Unmatched)]
    public async Task PatchContentReleaseAsync_SetsTheSeasonAndEpisodeScopes_WhenTheStatusChangesOnTheSeason(
        ReleaseScopeStatus status,
        ReleaseScopeStatus priorStatus)
    {
        // Arrange
        _context.ReleaseScopes.AddRange(
            new ReleaseScope { ReleaseId = _releaseId, ContentHierarchyItemId = _seasonId, Status = priorStatus },
            new ReleaseScope { ReleaseId = _releaseId, ContentHierarchyItemId = _episodeId, Status = priorStatus },
            new ReleaseScope
            {
                ReleaseId = _releaseId,
                ContentHierarchyItemId = _otherSeasonId,
                Status = priorStatus
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject
            .PatchContentReleaseAsync(
                _seasonId,
                _releaseId,
                new ContentReleasePatch { Status = new ScalarPatch<ReleaseScopeStatus> { Value = status } },
                null)
            .ConfigureAwait(false);

        // Assert
        var stored = await _context.ReleaseScopes.AsNoTracking()
            .ToDictionaryAsync(scope => scope.ContentHierarchyItemId, scope => scope.Status)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(stored[_seasonId], Is.EqualTo(status));
            Assert.That(stored[_episodeId], Is.EqualTo(status));
            Assert.That(stored[_otherSeasonId], Is.EqualTo(priorStatus));
        }
    }

    [Test]
    public async Task PatchContentReleaseAsync_ReturnsFalse_WhenTheReleaseHasNoScopeInTheItem()
    {
        // Arrange
        _context.ReleaseScopes.Add(
            new ReleaseScope { ReleaseId = _releaseId, ContentHierarchyItemId = _otherSeasonId });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject
            .PatchContentReleaseAsync(
                _seasonId,
                _releaseId,
                new ContentReleasePatch
                {
                    Status = new ScalarPatch<ReleaseScopeStatus> { Value = ReleaseScopeStatus.Unmatched }
                },
                null)
            .ConfigureAwait(false);

        // Assert
        var stored = await _context.ReleaseScopes.AsNoTracking().SingleAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(stored.Status, Is.EqualTo(ReleaseScopeStatus.Matched));
        }
    }
}
