using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class LibraryImportCandidateRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private ILibraryImportCandidateRepository _subject = null!;
    private List<MagpieEvent> _publishedEvents = null!;
    private Guid _userId;
    private Guid _rootFolderId;

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
            .ConfigureWarnings(warnings => warnings.Throw(CoreEventId.RowLimitingOperationWithoutOrderByWarning))
            .Options);

        var user = new User
        {
            Username = "TestUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(user);

        var rootFolder = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = "type-a",
            CreatedAt = DateTime.UtcNow
        };
        _context.RootFolders.Add(rootFolder);
        _context.SaveChanges();

        _userId = user.Id;
        _rootFolderId = rootFolder.Id;

        var loggerFactory = NullLoggerFactory.Instance;
        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _publishedEvents = [];
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) => _publishedEvents.Add(magpieEvent))
            .Returns(Task.CompletedTask);

        _subject = new LibraryImportCandidateRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            eventBus.Object,
            loggerFactory.CreateLogger<LibraryImportCandidateRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [TestCase(JobExecutionStatus.Queued, false)]
    [TestCase(JobExecutionStatus.Running, false)]
    [TestCase(JobExecutionStatus.CancellationRequested, false)]
    [TestCase(JobExecutionStatus.Completed, true)]
    [TestCase(JobExecutionStatus.Failed, true)]
    [TestCase(JobExecutionStatus.Cancelled, true)]
    public async Task TryReclaimInactiveImportAsync_ChecksExecutionOwnership_WhenCandidateIsOld(
        JobExecutionStatus executionStatus, bool expected)
    {
        // Arrange
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/recover.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Importing,
            CreatedAt = new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc)
        };
        _context.LibraryImportCandidates.Add(candidate);
        _context.JobExecutions.Add(new JobExecution
        {
            JobType = "RootFolderContentImportItem",
            Status = executionStatus,
            Parameters = new() { ["candidateId"] = candidate.Id.ToString("B").ToUpperInvariant() },
            NotBeforeAt = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc),
            LastHeartbeat = new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var first = await _subject.TryReclaimInactiveImportAsync(candidate.Id,
            new DateTime(2026, 9, 8, 11, 0, 0, DateTimeKind.Utc), _userId, CancellationToken.None)
            .ConfigureAwait(false);
        var second = await _subject.TryReclaimInactiveImportAsync(candidate.Id,
            new DateTime(2026, 9, 8, 11, 0, 0, DateTimeKind.Utc), _userId, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(expected));
            Assert.That(second, Is.False);
            Assert.That(await _context.LibraryImportCandidates.AsNoTracking().Select(row => row.Status)
                .SingleAsync().ConfigureAwait(false),
                Is.EqualTo(expected ? LibraryImportCandidateStatus.Failed : LibraryImportCandidateStatus.Importing));
            Assert.That(await _context.MediaFiles.CountAsync().ConfigureAwait(false), Is.Zero);
        }
    }

    [TestCase(LibraryImportCandidateStatus.Queued, 120, true)]
    [TestCase(LibraryImportCandidateStatus.Importing, 60, true)]
    [TestCase(LibraryImportCandidateStatus.Importing, 59, false)]
    [TestCase(LibraryImportCandidateStatus.Pending, 120, false)]
    public async Task TryReclaimInactiveImportAsync_RechecksActivityAndState_WhenNoExecutionExists(
        LibraryImportCandidateStatus status, int ageMinutes, bool expected)
    {
        // Arrange
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/recover.mkv",
            ContentType = "type-a",
            Status = status,
            CreatedAt = new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc),
            ModifiedAt = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-ageMinutes)
        };
        _context.LibraryImportCandidates.Add(candidate);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var reclaimed = await _subject.TryReclaimInactiveImportAsync(candidate.Id,
            new DateTime(2026, 9, 8, 11, 0, 0, DateTimeKind.Utc), _userId, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.That(reclaimed, Is.EqualTo(expected));
    }

    [Test]
    public async Task CreateAsync_PersistsRowAndAudit_WhenTheCandidateIsValid()
    {
        // Arrange
        var model = new LibraryImportCandidate
        {
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/sample.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending
        };
        model.Matches =
        [
            new()
            {
                Confidence = 0.9,
                ProviderType = "provider-a",
                ExternalIdKey = "provider-a.type-a",
                ExternalId = "550",
                Title = "Fight Club"
            }
        ];

        // Act
        var result = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.LibraryImportCandidates.AsNoTracking().FirstAsync().ConfigureAwait(false);
        var audit = await _context.AuditRecords.AsNoTracking().FirstAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(entity.FilePath, Is.EqualTo("/media/movies/sample.mkv"));
            Assert.That(entity.Status, Is.EqualTo(LibraryImportCandidateStatus.Pending));
            Assert.That(entity.Matches, Has.Count.EqualTo(1));
            Assert.That(entity.Matches[0].Title, Is.EqualTo("Fight Club"));
            Assert.That(entity.CreatedByUserId, Is.EqualTo(_userId));
            Assert.That(audit.Category, Is.EqualTo(AuditCategories.Content));
            Assert.That(audit.Operation, Is.EqualTo("Create"));
            Assert.That(audit.EntityType, Is.EqualTo(nameof(LibraryImportCandidate)));
            Assert.That(audit.EntityId, Is.EqualTo(entity.Id.ToString()));
        }
    }

    [Test]
    public async Task CreateAsync_StampsMatchCount_WhenMatchesAreProvided()
    {
        // Arrange
        var model = new LibraryImportCandidate
        {
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/sample.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending
        };
        model.Matches =
        [
            new()
            {
                Confidence = 0.9,
                ProviderType = "provider-a",
                ExternalIdKey = "provider-a.type-a",
                ExternalId = "550",
                Title = "Fight Club"
            },
            new()
            {
                Confidence = 0.5,
                ProviderType = "provider-a",
                ExternalIdKey = "provider-a.type-a",
                ExternalId = "551",
                Title = "Other"
            }
        ];

        // Act
        await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.LibraryImportCandidates.AsNoTracking().FirstAsync(m => m.Id == model.Id)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity.MatchCount, Is.EqualTo(2));
            Assert.That(entity.MatchCount, Is.EqualTo(entity.Matches.Count));
        }
    }

    [Test]
    public async Task UpdateAsync_KeepsMatchCount_WhenMatchesChange()
    {
        // Arrange
        var model = new LibraryImportCandidate
        {
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/x.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending
        };
        model.Matches =
        [
            new()
            {
                Confidence = 0.9,
                ProviderType = "provider-a",
                ExternalIdKey = "provider-a.type-a",
                ExternalId = "550",
                Title = "Fight Club"
            },
            new()
            {
                Confidence = 0.5,
                ProviderType = "provider-a",
                ExternalIdKey = "provider-a.type-a",
                ExternalId = "551",
                Title = "Other"
            }
        ];
        await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        model.Matches = [model.Matches[0]];

        // Act
        await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        var entity = await _context.LibraryImportCandidates.AsNoTracking().FirstAsync(m => m.Id == model.Id)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity.MatchCount, Is.EqualTo(1));
            Assert.That(entity.MatchCount, Is.EqualTo(entity.Matches.Count));
        }

        model.Matches = [];
        await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);
        var cleared = await _context.LibraryImportCandidates.AsNoTracking().FirstAsync(m => m.Id == model.Id)
            .ConfigureAwait(false);
        Assert.That(cleared.MatchCount, Is.Zero);
    }

    [Test]
    public async Task QueryGroupsAsync_ReturnsOnePageOfGroups_WhenMoreGroupsExistThanThePageSize()
    {
        // Arrange
        foreach (var show in new[] { "Show A", "Show B", "Show C" })
        {
            await _subject.CreateAsync(
                new LibraryImportCandidate
                {
                    RootFolderId = _rootFolderId,
                    ScanRunId = Guid.NewGuid(),
                    FilePath = $"/media/tv/{show}/S01E01.mkv",
                    ContentType = "type-a",
                    Status = LibraryImportCandidateStatus.Pending,
                    LayoutKey = $"/media/tv/{show}"
                },
                _userId).ConfigureAwait(false);
            await _subject.CreateAsync(
                new LibraryImportCandidate
                {
                    RootFolderId = _rootFolderId,
                    ScanRunId = Guid.NewGuid(),
                    FilePath = $"/media/tv/{show}/S01E02.mkv",
                    ContentType = "type-a",
                    Status = LibraryImportCandidateStatus.Failed,
                    LayoutKey = $"/media/tv/{show}"
                },
                _userId).ConfigureAwait(false);
        }

        // Act
        var page = await _subject.QueryGroupsAsync(
            new LibraryImportCandidateQuery(),
            new PagingParameter { PageNumber = 1, PageSize = 2 },
            CancellationToken.None).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.TotalItems, Is.EqualTo(3));
            Assert.That(page.Items.Select(item => item.GroupKey), Is.EqualTo(
                ["/media/tv/Show A", "/media/tv/Show B"]));
            Assert.That(page.Items.First().StatusCounts[LibraryImportCandidateStatus.Pending], Is.EqualTo(1));
            Assert.That(page.Items.First().StatusCounts[LibraryImportCandidateStatus.Failed], Is.EqualTo(1));
            Assert.That(page.Items.First().Representative.LayoutKey, Is.EqualTo("/media/tv/Show A"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyTheGroupMembers_WhenTheQueryNamesAGroupKey()
    {
        // Arrange
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Show A/S01E01.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending,
                LayoutKey = "/media/tv/Show A"
            },
            _userId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Show B/S01E01.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending,
                LayoutKey = "/media/tv/Show B"
            },
            _userId).ConfigureAwait(false);

        // Act
        var members = await _subject.QueryAsync(
            new LibraryImportCandidateQuery { GroupKey = "/media/tv/Show A" },
            paging: new() { PageNumber = 1, PageSize = 100 },
            sort: null).ConfigureAwait(false);
        var remainder = await _subject.QueryAsync(
            new LibraryImportCandidateQuery { ExcludeGroupKeys = ["/media/tv/Show A"] },
            paging: new() { PageNumber = 1, PageSize = 100 },
            sort: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(members.Items.Select(item => item.FilePath), Is.EqualTo(
                ["/media/tv/Show A/S01E01.mkv"]));
            Assert.That(remainder.Items.Select(item => item.FilePath), Is.EqualTo(
                ["/media/tv/Show B/S01E01.mkv"]));
        }
    }

    [TestCase(null, "/media/tv/Show", "/media/tv/Show")]
    [TestCase("550", null, "match:type-a:provider-a:provider-a.type-a:550")]
    [TestCase(null, null, "query:type-a:s01e01")]
    public async Task CreateAsync_DerivesTheGroupKey_WhenGroupIdentityFieldsVary(
        string? externalId,
        string? layoutKey,
        string expectedGroupKey)
    {
        // Arrange
        var model = new LibraryImportCandidate
        {
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Show/S01E01.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending,
            LayoutKey = layoutKey,
            Matches = externalId is null
                ?
                []
                :
                [
                    new()
                    {
                        Confidence = 0.9,
                        ProviderType = "provider-a",
                        ExternalIdKey = "provider-a.type-a",
                        ExternalId = externalId,
                        Title = "Show"
                    }
                ]
        };

        // Act
        await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.LibraryImportCandidates.AsNoTracking().FirstAsync().ConfigureAwait(false);
        Assert.That(entity.GroupKey, Is.EqualTo(expectedGroupKey));
    }

    [Test]
    public async Task UpdateAsync_RederivesTheGroupKey_WhenTheCandidateGainsAMatch()
    {
        // Arrange
        var created = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/Show/S01E01.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);
        created.Matches =
        [
            new()
            {
                Confidence = 1,
                ProviderType = "provider-a",
                ExternalIdKey = "provider-a.type-a",
                ExternalId = "550",
                Title = "Fight Club"
            }
        ];

        // Act
        await _subject.UpdateAsync(created, _userId, CancellationToken.None).ConfigureAwait(false);

        // Assert
        var entity = await _context.LibraryImportCandidates.AsNoTracking().FirstAsync().ConfigureAwait(false);
        Assert.That(entity.GroupKey, Is.EqualTo("match:type-a:provider-a:provider-a.type-a:550"));
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingCandidates_WhenHasMatchesFilterIsSet()
    {
        // Arrange
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/m/with.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending,
                Matches =
                [
                    new()
                    {
                        Confidence = 0.9,
                        ProviderType = "provider-a",
                        ExternalIdKey = "provider-a.type-a",
                        ExternalId = "550",
                        Title = "Fight Club"
                    }
                ]
            },
            _userId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/m/without.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending,
                Matches = []
            },
            _userId).ConfigureAwait(false);

        // Act
        var withCandidates = await _subject.QueryAsync(
            new LibraryImportCandidateQuery { HasMatches = true },
                paging: new() { PageNumber = 1, PageSize = 100 },
                sort: null).ConfigureAwait(false);
        var withoutCandidates = await _subject.QueryAsync(
            new LibraryImportCandidateQuery { HasMatches = false },
                paging: new() { PageNumber = 1, PageSize = 100 },
                sort: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(withCandidates.Items.Single().FilePath, Is.EqualTo("/m/with.mkv"));
            Assert.That(withoutCandidates.Items.Single().FilePath, Is.EqualTo("/m/without.mkv"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingCandidates_WhenCombinedFiltersAreSet()
    {
        // Arrange
        var otherFolder = new RootFolder
        {
            Name = "TV",
            Path = "/media/tv",
            ContentType = "type-a",
            CreatedAt = DateTime.UtcNow
        };
        _context.RootFolders.Add(otherFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var excluded = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/m/matched-a.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending,
                Matches =
                [
                    new()
                    {
                        Confidence = 0.9,
                        ProviderType = "provider-a",
                        ExternalIdKey = "provider-a.type-a",
                        ExternalId = "550",
                        Title = "Fight Club"
                    }
                ]
            },
            _userId).ConfigureAwait(false);
        var included = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/m/matched-b.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending,
                Matches =
                [
                    new()
                    {
                        Confidence = 0.9,
                        ProviderType = "provider-a",
                        ExternalIdKey = "provider-a.type-a",
                        ExternalId = "550",
                        Title = "Fight Club"
                    }
                ]
            },
            _userId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/m/unmatched.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending,
                Matches = []
            },
            _userId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/m/queued.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Queued,
                Matches =
                [
                    new()
                    {
                        Confidence = 0.9,
                        ProviderType = "provider-a",
                        ExternalIdKey = "provider-a.type-a",
                        ExternalId = "550",
                        Title = "Fight Club"
                    }
                ]
            },
            _userId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = otherFolder.Id,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/tv/other.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending,
                Matches =
                [
                    new()
                    {
                        Confidence = 0.9,
                        ProviderType = "provider-a",
                        ExternalIdKey = "provider-a.type-a",
                        ExternalId = "550",
                        Title = "Fight Club"
                    }
                ]
            },
            _userId).ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new()
            {
                RootFolderId = _rootFolderId,
                Status = LibraryImportCandidateStatus.Pending,
                HasMatches = true,
                ExcludeIds = [excluded.Id]
            },
            new() { PageNumber = 1, PageSize = 100 },
            new() { PropertyName = nameof(LibraryImportCandidate.Id), Direction = SortDirection.Ascending })
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items.Select(candidate => candidate.Id), Is.EqualTo([included.Id]));
            Assert.That(result.TotalItems, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsPageAndTotal_WhenPagingIsSet()
    {
        // Arrange
        foreach (var id in new[]
        {
            Guid.Parse("62e41376-1d58-4058-a915-e6aa280e44f8"),
            Guid.Parse("6308fc94-9c53-40fa-a8fa-8520d851ca15"),
            Guid.Parse("786caae1-03a8-4d3c-9ba2-047d42b26764"),
            Guid.Parse("db98f071-8b6d-4c16-adbd-79b73b622334"),
            Guid.Parse("fe719867-e291-49ff-bd78-3936f61f9a96")
        })
        {
            await _subject.CreateAsync(
                new LibraryImportCandidate
                {
                    Id = id,
                    RootFolderId = _rootFolderId,
                    ScanRunId = Guid.NewGuid(),
                    FilePath = "/m/" + id.ToString("D") + ".mkv",
                    ContentType = "type-a",
                    Status = LibraryImportCandidateStatus.Pending,
                    Matches = []
                },
                _userId).ConfigureAwait(false);
        }

        // Act
        var page = await _subject.QueryAsync(
            new LibraryImportCandidateQuery(),
            new PagingParameter { PageNumber = 2, PageSize = 2 },
            sort: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.TotalItems, Is.EqualTo(5));
            Assert.That(page.PageNumber, Is.EqualTo(2));
            Assert.That(page.PageSize, Is.EqualTo(2));
            Assert.That(page.Items.Select(candidate => candidate.Id), Is.EqualTo(
            [
                Guid.Parse("786caae1-03a8-4d3c-9ba2-047d42b26764"),
                Guid.Parse("db98f071-8b6d-4c16-adbd-79b73b622334")
            ]));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsCandidate_WhenCandidateExists()
    {
        // Arrange
        var model = new LibraryImportCandidate
        {
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/x.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending
        };
        await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(model.Id).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.FilePath, Is.EqualTo("/media/movies/x.mkv"));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task UpdateAsync_PersistsValuesAndAudit_WhenTheCandidateExists()
    {
        // Arrange
        var model = new LibraryImportCandidate
        {
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/x.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending
        };
        await _subject.CreateAsync(model, _userId).ConfigureAwait(false);
        model.Notes = "operator note";

        // Act
        var updated = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.LibraryImportCandidates.AsNoTracking().FirstAsync(m => m.Id == model.Id)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.True);
            Assert.That(entity.Notes, Is.EqualTo("operator note"));
            Assert.That(entity.ModifiedAt, Is.Not.Null);
            Assert.That(entity.ModifiedByUserId, Is.EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalseWithoutAudit_WhenCandidateIsMissing()
    {
        // Arrange
        var ghost = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/missing.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending
        };

        // Act
        var updated = await _subject.UpdateAsync(ghost, _userId).ConfigureAwait(false);

        // Assert
        var audits = await _context.AuditRecords.AsNoTracking().CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.False);
            Assert.That(audits, Is.Zero);
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesRowAndAudits_WhenTheCandidateExists()
    {
        // Arrange
        var model = new LibraryImportCandidate
        {
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/x.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending
        };
        await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteAsync(model.Id, _userId).ConfigureAwait(false);

        // Assert
        var exists = await _context.LibraryImportCandidates.AnyAsync(m => m.Id == model.Id).ConfigureAwait(false);
        var deleteAudit = await _context.AuditRecords
            .AsNoTracking()
            .Where(a => a.Operation == "Delete")
            .ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            Assert.That(exists, Is.False);
            Assert.That(deleteAudit, Has.Count.EqualTo(1));
            Assert.That(deleteAudit[0].EntityType, Is.EqualTo(nameof(LibraryImportCandidate)));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenMissing()
    {
        // Act
        var deleted = await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        Assert.That(deleted, Is.False);
    }

    [Test]
    public async Task UpdateDeleteAsync_PublishesUpdatedEvent_WhenCreateStaysSilent()
    {
        // Arrange
        var model = new LibraryImportCandidate
        {
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/lifecycle.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending
        };
        await _subject.CreateAsync(model, _userId).ConfigureAwait(false);
        model.Notes = "matched";

        // Act
        await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);
        await _subject.DeleteAsync(model.Id, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_publishedEvents, Has.Count.EqualTo(2));
            Assert.That(
                _publishedEvents,
                Has.All.Property(nameof(MagpieEvent.Name)).EqualTo("library.importCandidate.updated"));
            Assert.That(
                _publishedEvents,
                Has.All.Matches<MagpieEvent>(magpieEvent =>
                    magpieEvent.Context.Values.Contains(model.Id.ToString("D"), StringComparer.Ordinal)));
        }
    }

    [Test]
    public async Task DeleteByRootFolderAsync_DeletesAndAuditsMatchingRows_WhenFoldersDiffer()
    {
        // Arrange
        var otherRootFolder = new RootFolder
        {
            Name = "TV",
            Path = "/media/tv",
            ContentType = "type-c",
            CreatedAt = DateTime.UtcNow
        };
        _context.RootFolders.Add(otherRootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/a.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending,
                Matches =
                [
                    new()
                    {
                        Confidence = 0.9,
                        ProviderType = "provider-a",
                        ExternalIdKey = "provider-a.type-a",
                        ExternalId = "550",
                        Title = "Fight Club"
                    }
                ]
            },
            _userId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/b.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending,
                Matches = []
            },
            _userId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = otherRootFolder.Id,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/tv/x.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending,
                Matches = []
            },
            _userId).ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteByRootFolderAsync(_rootFolderId, null, _userId, default)
            .ConfigureAwait(false);

        // Assert
        var remaining = await _context.LibraryImportCandidates.AsNoTracking().ToListAsync().ConfigureAwait(false);
        var wipeAudit = await _context.AuditRecords
            .AsNoTracking()
            .Where(a => a.Operation == "Delete" && a.EntityId == _rootFolderId.ToString())
            .ToListAsync()
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.EqualTo(2));
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(remaining[0].FilePath, Is.EqualTo("/media/tv/x.mkv"));
            Assert.That(wipeAudit, Has.Count.EqualTo(1));
            Assert.That(wipeAudit[0].EntityType, Is.EqualTo(nameof(LibraryImportCandidate)));
            Assert.That(_publishedEvents, Is.Empty);
        }
    }

    [Test]
    public async Task DeleteByRootFolderAsync_ReturnsZeroWithoutAudit_WhenNoRowsMatch()
    {
        // Act
        var deleted = await _subject.DeleteByRootFolderAsync(_rootFolderId, null, _userId, default)
            .ConfigureAwait(false);

        // Assert
        var audits = await _context.AuditRecords.AsNoTracking().CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.Zero);
            Assert.That(audits, Is.Zero);
            Assert.That(_publishedEvents, Is.Empty);
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingCandidates_WhenRootFolderFilterIsSet()
    {
        // Arrange
        var other = new RootFolder
        {
            Name = "TV",
            Path = "/media/tv",
            ContentType = "type-a",
            CreatedAt = DateTime.UtcNow
        };
        _context.RootFolders.Add(other);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/a.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);
        var sibling = new LibraryImportCandidate
        {
            RootFolderId = other.Id,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/b.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending
        };
        await _subject.CreateAsync(sibling, _userId).ConfigureAwait(false);

        // Act
        var page = await _subject.QueryAsync(
            new LibraryImportCandidateQuery { RootFolderId = _rootFolderId },
            paging: new() { PageNumber = 1, PageSize = 100 },
            sort: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.TotalItems, Is.EqualTo(1));
            Assert.That(
                page.Items,
                Has.Exactly(1).Property(nameof(LibraryImportCandidate.RootFolderId)).EqualTo(_rootFolderId));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByScanRunIdAndStatus_WhenBothFiltersAreSet()
    {
        // Arrange
        var scanA = Guid.NewGuid();
        var scanB = Guid.NewGuid();
        var first = new LibraryImportCandidate
        {
            RootFolderId = _rootFolderId,
            ScanRunId = scanA,
            FilePath = "/media/movies/a.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending
        };
        await _subject.CreateAsync(first, _userId).ConfigureAwait(false);
        var second = new LibraryImportCandidate
        {
            RootFolderId = _rootFolderId,
            ScanRunId = scanA,
            FilePath = "/media/movies/b.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Failed
        };
        await _subject.CreateAsync(second, _userId).ConfigureAwait(false);
        var third = new LibraryImportCandidate
        {
            RootFolderId = _rootFolderId,
            ScanRunId = scanB,
            FilePath = "/media/movies/c.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending
        };
        await _subject.CreateAsync(third, _userId).ConfigureAwait(false);

        // Act
        var pageByScan = await _subject.QueryAsync(
            new LibraryImportCandidateQuery { ScanRunId = scanA },
            paging: new() { PageNumber = 1, PageSize = 100 },
            sort: null).ConfigureAwait(false);
        var pageByStatus = await _subject.QueryAsync(
            new LibraryImportCandidateQuery { Status = LibraryImportCandidateStatus.Failed },
            paging: new() { PageNumber = 1, PageSize = 100 },
            sort: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pageByScan.TotalItems, Is.EqualTo(2));
            Assert.That(pageByStatus.TotalItems, Is.EqualTo(1));
            Assert.That(pageByStatus.Items.Single().FilePath, Is.EqualTo("/media/movies/b.mkv"));
        }
    }

    [Test]
    public async Task TryClaimForImportAsync_ReportsOneWinner_WhenTheCandidateIsPending()
    {
        // Arrange
        var candidate = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/a.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);

        // Act
        var claimed = await _subject.TryClaimForImportAsync(candidate.Id, _userId, default).ConfigureAwait(false);

        // Assert
        var stored = await _context.LibraryImportCandidates.AsNoTracking()
            .FirstAsync(m => m.Id == candidate.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claimed, Is.True);
            Assert.That(stored.Status, Is.EqualTo(LibraryImportCandidateStatus.Queued));
        }
    }

    [Test]
    public async Task TryClaimForImportAsync_ClearsNoteAndQueues_WhenTheCandidateFailed()
    {
        // Arrange
        var candidate = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/failed.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Failed,
                Notes = "provider offline"
            },
            _userId).ConfigureAwait(false);

        // Act
        var claimed = await _subject.TryClaimForImportAsync(candidate.Id, _userId, default).ConfigureAwait(false);

        // Assert
        var stored = await _context.LibraryImportCandidates.AsNoTracking()
            .FirstAsync(m => m.Id == candidate.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claimed, Is.True);
            Assert.That(stored.Status, Is.EqualTo(LibraryImportCandidateStatus.Queued));
            Assert.That(stored.Notes, Is.Null);
        }
    }

    [TestCase(LibraryImportCandidateStatus.Queued)]
    [TestCase(LibraryImportCandidateStatus.Importing)]
    public async Task TryClaimForImportAsync_ReturnsFalse_WhenCandidateIsInFlight(
        LibraryImportCandidateStatus status)
    {
        // Arrange
        var candidate = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/in-flight.mkv",
                ContentType = "type-a",
                Status = status
            },
            _userId).ConfigureAwait(false);

        // Act
        var claimed = await _subject.TryClaimForImportAsync(candidate.Id, _userId, default).ConfigureAwait(false);

        // Assert
        Assert.That(claimed, Is.False);
    }

    [Test]
    public async Task TryClaimForImportAsync_HasExactlyOneWinner_WhenTwoClaimsRace()
    {
        // Arrange
        var candidate = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/a.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);

        // Act
        var first = await _subject.TryClaimForImportAsync(candidate.Id, _userId, default).ConfigureAwait(false);
        var second = await _subject.TryClaimForImportAsync(candidate.Id, _userId, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.False);
        }
    }

    [Test]
    public async Task TryBeginImportAsync_ImportsOnlyQueuedCandidates_WhenStatusVaries()
    {
        // Arrange
        var candidate = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/a.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);

        // Act
        var beforeClaim = await _subject.TryBeginImportAsync(candidate.Id, _userId, default).ConfigureAwait(false);
        await _subject.TryClaimForImportAsync(candidate.Id, _userId, default).ConfigureAwait(false);
        var afterClaim = await _subject.TryBeginImportAsync(candidate.Id, _userId, default).ConfigureAwait(false);

        // Assert
        var stored = await _context.LibraryImportCandidates.AsNoTracking()
            .FirstAsync(m => m.Id == candidate.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(beforeClaim, Is.False);
            Assert.That(afterClaim, Is.True);
            Assert.That(stored.Status, Is.EqualTo(LibraryImportCandidateStatus.Importing));
        }
    }

    [Test]
    public async Task MarkImportFailedAsync_TransitionsImportingToFailed_WhenReasonIsProvided()
    {
        // Arrange
        var candidate = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/a.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);
        await _subject.TryClaimForImportAsync(candidate.Id, _userId, default).ConfigureAwait(false);
        await _subject.TryBeginImportAsync(candidate.Id, _userId, default).ConfigureAwait(false);

        // Act
        var failed = await _subject
            .MarkImportFailedAsync(candidate.Id, "provider returned not found", _userId, default)
            .ConfigureAwait(false);

        // Assert
        var stored = await _context.LibraryImportCandidates.AsNoTracking()
            .FirstAsync(m => m.Id == candidate.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed, Is.True);
            Assert.That(stored.Status, Is.EqualTo(LibraryImportCandidateStatus.Failed));
            Assert.That(stored.Notes, Is.EqualTo("provider returned not found"));
        }
    }

    [Test]
    public async Task DeleteByRootFolderAsync_PreservesActiveCandidates_WhenCandidatesAreQueuedOrImporting()
    {
        // Arrange
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/pending.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);
        var queued = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/queued.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);
        var importing = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/importing.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);
        await _subject.TryClaimForImportAsync(queued.Id, _userId, default).ConfigureAwait(false);
        await _subject.TryClaimForImportAsync(importing.Id, _userId, default).ConfigureAwait(false);
        await _subject.TryBeginImportAsync(importing.Id, _userId, default).ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteByRootFolderAsync(_rootFolderId, null, _userId, default)
            .ConfigureAwait(false);

        // Assert
        var remaining = await _context.LibraryImportCandidates.AsNoTracking()
            .ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(
                remaining.Select(m => m.Status),
                Is.EquivalentTo(
                [
                    LibraryImportCandidateStatus.Queued,
                    LibraryImportCandidateStatus.Importing
                ]));
        }
    }

    [Test]
    public async Task BulkPatchAsync_MovesEveryMemberAndWritesOneAuditRecord_WhenAGroupIsMatched()
    {
        // Arrange
        var scanRunId = Guid.NewGuid();
        var first = await _subject.CreateAsync(
            new()
            {
                RootFolderId = _rootFolderId,
                ScanRunId = scanRunId,
                FilePath = "/media/movies/Bluey S01E01.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);
        var second = await _subject.CreateAsync(
            new()
            {
                RootFolderId = _rootFolderId,
                ScanRunId = scanRunId,
                FilePath = "/media/movies/Bluey S01E02.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);

        // Act
        var result = await _subject.BulkPatchAsync(
            new()
            {
                Ids = [first.Id, second.Id],
                Patch = new()
                {
                    Matches = new()
                    {
                        Value =
                        [
                            new LibraryImportMatch
                            {
                                Confidence = 1.0,
                                ProviderType = "tvmaze",
                                ExternalIdKey = "tvmaze",
                                ExternalId = "44796",
                                Title = "Bluey"
                            }
                        ]
                    }
                }
            },
            _userId,
            default).ConfigureAwait(false);

        // Assert
        var stored = await _context.LibraryImportCandidates.AsNoTracking()
            .OrderBy(candidate => candidate.FilePath)
            .ToListAsync().ConfigureAwait(false);
        var updateAudits = await _context.AuditRecords.AsNoTracking()
            .Where(record => record.EntityType == "LibraryImportCandidate" && record.Operation == "Update")
            .CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(
                stored.Select(candidate => candidate.GroupKey),
                Is.All.EqualTo("match:type-a:tvmaze:tvmaze:44796"));
            Assert.That(stored.Select(candidate => candidate.MatchCount), Is.All.EqualTo(1));
            Assert.That(stored[0].Matches[0].ExternalId, Is.EqualTo("44796"));
            Assert.That(updateAudits, Is.EqualTo(1));
        }
    }

    [TestCase(LibraryImportCandidateStatus.Pending)]
    [TestCase(LibraryImportCandidateStatus.Failed)]
    [TestCase(LibraryImportCandidateStatus.MatchingPending)]
    public async Task TryRejectAsync_RemovesMatchesAndTarget_WhenNoImportOwnsTheCandidate(
        LibraryImportCandidateStatus status)
    {
        // Arrange
        var candidate = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/Movie.2020.mkv",
                ContentType = "type-a",
                Status = status,
                ContentHierarchyItemId = Guid.NewGuid(),
                Matches =
                [
                    new LibraryImportMatch
                    {
                        Confidence = 1,
                        ProviderType = "provider-a",
                        ExternalIdKey = "provider-a",
                        ExternalId = "44796",
                        Title = "Movie"
                    }
                ]
            },
            _userId).ConfigureAwait(false);

        // Act
        var rejected = await _subject.TryRejectAsync(candidate.Id, _userId, default).ConfigureAwait(false);

        // Assert
        var stored = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(m => m.Id == candidate.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rejected, Is.True);
            Assert.That(stored.Status, Is.EqualTo(LibraryImportCandidateStatus.Rejected));
            Assert.That(stored.Matches, Is.Empty);
            Assert.That(stored.MatchCount, Is.Zero);
            Assert.That(stored.ContentHierarchyItemId, Is.Null);
            Assert.That(stored.GroupKey, Is.EqualTo("query:type-a:movie.2020"));
        }
    }

    [Test]
    public async Task TryRejectAsync_LeavesTheImport_WhenAnImportOwnsTheCandidate()
    {
        // Arrange
        var queued = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/queued.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);
        var importing = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/importing.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);
        await _subject.TryClaimForImportAsync(queued.Id, _userId, default).ConfigureAwait(false);
        await _subject.TryClaimForImportAsync(importing.Id, _userId, default).ConfigureAwait(false);
        await _subject.TryBeginImportAsync(importing.Id, _userId, default).ConfigureAwait(false);

        // Act
        var queuedRejected = await _subject.TryRejectAsync(queued.Id, _userId, default).ConfigureAwait(false);
        var importingRejected = await _subject.TryRejectAsync(importing.Id, _userId, default)
            .ConfigureAwait(false);

        // Assert
        var statuses = await _context.LibraryImportCandidates.AsNoTracking()
            .OrderBy(m => m.FilePath).Select(m => m.Status).ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(queuedRejected, Is.False);
            Assert.That(importingRejected, Is.False);
            Assert.That(
                statuses,
                Is.EqualTo([LibraryImportCandidateStatus.Importing, LibraryImportCandidateStatus.Queued]));
        }
    }

    [Test]
    public async Task DeleteByRootFolderAsync_KeepsRejectedCandidates_WhenTheScanReplacesCandidates()
    {
        // Arrange
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/pending.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);
        var rejected = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/rejected.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);
        await _subject.TryRejectAsync(rejected.Id, _userId, default).ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteByRootFolderAsync(_rootFolderId, null, _userId, default)
            .ConfigureAwait(false);

        // Assert
        var remaining = await _context.LibraryImportCandidates.AsNoTracking()
            .Select(m => m.FilePath).ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(remaining, Is.EqualTo(["/media/movies/rejected.mkv"]));
        }
    }

    [Test]
    public async Task DeleteByRootFolderAsync_DeletesOnlyTheNamedCandidates_WhenIdsAreGiven()
    {
        // Arrange
        var inside = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/Inside/inside.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/Outside/outside.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteByRootFolderAsync(_rootFolderId, [inside.Id], _userId, default)
            .ConfigureAwait(false);

        // Assert
        var remaining = await _context.LibraryImportCandidates.AsNoTracking()
            .Select(m => m.FilePath).ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(remaining, Is.EqualTo(["/media/movies/Outside/outside.mkv"]));
        }
    }

    [Test]
    public async Task BulkPatchAsync_KeepsARejectedCandidateUnmatched_WhenItsGroupIsMatched()
    {
        // Arrange
        var rejected = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/rejected.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);
        await _subject.TryRejectAsync(rejected.Id, _userId, default).ConfigureAwait(false);

        // Act
        var result = await _subject.BulkPatchAsync(
            new()
            {
                Ids = [rejected.Id],
                Patch = new()
                {
                    Matches = new()
                    {
                        Value =
                        [
                            new LibraryImportMatch
                            {
                                Confidence = 1,
                                ProviderType = "provider-a",
                                ExternalIdKey = "provider-a",
                                ExternalId = "44796",
                                Title = "Movie"
                            }
                        ]
                    }
                }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var stored = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(m => m.Id == rejected.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.Zero);
            Assert.That(stored.Status, Is.EqualTo(LibraryImportCandidateStatus.Rejected));
            Assert.That(stored.Matches, Is.Empty);
        }
    }

    [Test]
    public async Task BulkPatchAsync_MovesAMatchingPendingCandidateToPending_WhenTheUserMatchesIt()
    {
        // Arrange
        var candidate = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/Movie.2020.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.MatchingPending
            },
            _userId).ConfigureAwait(false);

        // Act
        var result = await _subject.BulkPatchAsync(
            new()
            {
                Ids = [candidate.Id],
                Patch = new()
                {
                    Matches = new()
                    {
                        Value =
                        [
                            new LibraryImportMatch
                            {
                                Confidence = 1,
                                ProviderType = "provider-a",
                                ExternalIdKey = "provider-a",
                                ExternalId = "12",
                                Title = "Movie"
                            }
                        ]
                    }
                }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var stored = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(m => m.Id == candidate.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(stored.Status, Is.EqualTo(LibraryImportCandidateStatus.Pending));
            Assert.That(stored.Matches.Single().ExternalId, Is.EqualTo("12"));
        }
    }

    [Test]
    public async Task BulkPatchAsync_ClearsTheLibraryTarget_WhenTheMatchChanges()
    {
        // Arrange
        var candidate = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/Movie.2020.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending,
                ContentHierarchyItemId = Guid.NewGuid()
            },
            _userId).ConfigureAwait(false);

        // Act
        await _subject.BulkPatchAsync(
            new()
            {
                Ids = [candidate.Id],
                Patch = new()
                {
                    Matches = new()
                    {
                        Value =
                        [
                            new LibraryImportMatch
                            {
                                Confidence = 1,
                                ProviderType = "provider-a",
                                ExternalIdKey = "provider-a",
                                ExternalId = "12",
                                Title = "Other"
                            }
                        ]
                    }
                }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var stored = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(m => m.Id == candidate.Id).ConfigureAwait(false);
        Assert.That(stored.ContentHierarchyItemId, Is.Null);
    }

    [Test]
    public async Task TryResolveMatchAsync_KeepsTheRejection_WhenTheUserRejectsDuringResolution()
    {
        // Arrange
        var candidate = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/Movie.2020.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.MatchingPending
            },
            _userId).ConfigureAwait(false);
        await _subject.TryRejectAsync(candidate.Id, _userId, default).ConfigureAwait(false);

        // Act
        var resolved = await _subject.TryResolveMatchAsync(
            candidate with
            {
                Status = LibraryImportCandidateStatus.Pending,
                ContentHierarchyItemId = Guid.NewGuid(),
                Matches =
                [
                    new LibraryImportMatch
                    {
                        Confidence = 1,
                        ProviderType = "provider-a",
                        ExternalIdKey = "provider-a",
                        ExternalId = "44796",
                        Title = "Movie"
                    }
                ]
            },
            null,
            default).ConfigureAwait(false);

        // Assert
        var stored = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(m => m.Id == candidate.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved, Is.False);
            Assert.That(stored.Status, Is.EqualTo(LibraryImportCandidateStatus.Rejected));
            Assert.That(stored.ContentHierarchyItemId, Is.Null);
        }
    }

    [TestCase(true, LibraryImportCandidateStatus.MatchingPending)]
    [TestCase(false, LibraryImportCandidateStatus.Pending)]
    public async Task TryRestoreAsync_ReturnsOnlyARejectedCandidateToMatching_WhenRestored(
        bool rejectFirst, LibraryImportCandidateStatus expected)
    {
        // Arrange
        var candidate = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/Movie.2020.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);
        if (rejectFirst)
        {
            await _subject.TryRejectAsync(candidate.Id, _userId, default).ConfigureAwait(false);
        }

        // Act
        var restored = await _subject.TryRestoreAsync(candidate.Id, _userId, default).ConfigureAwait(false);

        // Assert
        var stored = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(m => m.Id == candidate.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored, Is.EqualTo(rejectFirst));
            Assert.That(stored.Status, Is.EqualTo(expected));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyTheTargetedCandidates_WhenTargetIdsAreGiven()
    {
        // Arrange
        var targetId = Guid.NewGuid();
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/targeted.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending,
                ContentHierarchyItemId = targetId
            },
            _userId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/other-target.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending,
                ContentHierarchyItemId = Guid.NewGuid()
            },
            _userId).ConfigureAwait(false);
        await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/untargeted.mkv",
                ContentType = "type-a",
                Status = LibraryImportCandidateStatus.Pending
            },
            _userId).ConfigureAwait(false);

        // Act
        var page = await _subject.QueryAsync(
            new LibraryImportCandidateQuery { ContentHierarchyItemIds = [targetId] },
            new() { PageNumber = 1, PageSize = 10 },
            null,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Select(m => m.FilePath), Is.EqualTo(["/media/movies/targeted.mkv"]));
    }

    [TestCase(LibraryImportCandidateStatus.Pending, true)]
    [TestCase(LibraryImportCandidateStatus.Failed, true)]
    [TestCase(LibraryImportCandidateStatus.Queued, false)]
    [TestCase(LibraryImportCandidateStatus.Rejected, false)]
    public async Task TryUpdateTargetAsync_SavesTheItem_OnlyWhenNoImportOwnsTheMatchedCandidate(
        LibraryImportCandidateStatus status, bool expected)
    {
        // Arrange
        var targetId = Guid.NewGuid();
        var candidate = await _subject.CreateAsync(
            new LibraryImportCandidate
            {
                RootFolderId = _rootFolderId,
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/Movie.2020.mkv",
                ContentType = "type-a",
                Status = status,
                Matches =
                [
                    new LibraryImportMatch
                    {
                        Confidence = 1,
                        ProviderType = "provider-a",
                        ExternalIdKey = "provider-a",
                        ExternalId = "44796",
                        Title = "Movie"
                    }
                ]
            },
            _userId).ConfigureAwait(false);

        // Act
        var saved = await _subject.TryUpdateTargetAsync(candidate.Id, targetId, _userId, default)
            .ConfigureAwait(false);

        // Assert
        var stored = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(m => m.Id == candidate.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved, Is.EqualTo(expected));
            Assert.That(stored.ContentHierarchyItemId, Is.EqualTo(expected ? targetId : null));
        }
    }
}
