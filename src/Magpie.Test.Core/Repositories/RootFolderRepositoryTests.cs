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
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class RootFolderRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IRootFolderRepository _subject = null!;
    private Mock<IContentHierarchyItemRepository> _contentHierarchyItems = null!;
    private List<MagpieEvent> _publishedEvents = null!;
    private bool _publishedInsideTransaction;
    private Guid _userId;
    private string _contentType = null!;

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

        var user = new User
        {
            Username = "TestUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.SaveChanges();

        _userId = user.Id;
        _contentType = "type-a";

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _publishedEvents = [];
        _publishedInsideTransaction = false;
        var events = new Mock<IEventBus>(MockBehavior.Strict);
        events
            .Setup(x => x.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) =>
            {
                _publishedEvents.Add(magpieEvent);
                _publishedInsideTransaction |= _context.Database.CurrentTransaction is not null;
            })
            .Returns(Task.CompletedTask);

        _contentHierarchyItems = new Mock<IContentHierarchyItemRepository>();

        _subject = new RootFolderRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            _contentHierarchyItems.Object,
            new LibraryImportCandidateRepository(
                _context,
                auditRecordRepository,
                transactionManager,
                events.Object,
                NullLogger<LibraryImportCandidateRepository>.Instance,
                new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))),
            events.Object,
            NullLogger<RootFolderRepository>.Instance,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsRootFolder_WhenRootFolderExists()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetByIdAsync(rootFolder.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("Movies"));
            Assert.That(result, Has.Property(nameof(result.Path)).EqualTo("/media/movies"));
            Assert.That(result, Has.Property(nameof(result.ContentType)).EqualTo(_contentType));
        }
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenRootFolderDoesNotExist()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid()).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GetContentSizesAsync_KeepsSharedFilesInTheirOriginalFolder_WhenOwnershipChanges(bool multiRoot)
    {
        // Arrange
        var firstRoot = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "First",
            Path = "/media/first",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var secondRoot = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "Second",
            Path = "/media/second",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var emptyRoot = new RootFolder
        {
            Name = "Empty",
            Path = "/media/empty",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var coveredItemId = Guid.NewGuid();
        var firstItem = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ContentType = _contentType,
            Level = "movie",
            Slug = "first-film",
            Name = "First film",
            EffectiveRootFolderId = firstRoot.Id,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new()
            {
                CreatedAt = DateTime.UtcNow,
                MediaFiles =
                {
                    new()
                    {
                        FileKind = "Video",
                        FilePath = "/media/first/film.mkv",
                        PathKey = "/MEDIA/FIRST/FILM.MKV",
                        FileSize = 100,
                        CreatedAt = DateTime.UtcNow,
                        Coverage = {
                            new()
                            {
                                ContentItemId = coveredItemId
                            }
                        }
                    },
                    new()
                    {
                        FileKind = "Subtitle",
                        FilePath = "/media/first/film.srt",
                        PathKey = "/MEDIA/FIRST/FILM.SRT",
                        FileSize = 50,
                        CreatedAt = DateTime.UtcNow,
                        Coverage =
                        {
                            new()
                            {
                                ContentItemId = coveredItemId
                            }
                        }
                    }
                }
            }
        };
        var coveredItem = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ContentType = _contentType,
            Level = "movie",
            Slug = "covered-film",
            Name = "Covered film",
            EffectiveRootFolderId = multiRoot ? secondRoot.Id : firstRoot.Id,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new() { Id = coveredItemId, CreatedAt = DateTime.UtcNow }
        };
        var secondItem = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ContentType = _contentType,
            Level = "movie",
            Slug = "second-film",
            Name = "Second film",
            EffectiveRootFolderId = secondRoot.Id,
            CreatedAt = DateTime.UtcNow,
            ContentItem = new()
            {
                CreatedAt = DateTime.UtcNow,
                MediaFiles =
                {
                    new()
                    {
                        FileKind = "Video",
                        FilePath = "/media/second/film.mkv",
                        PathKey = "/MEDIA/SECOND/FILM.MKV",
                        FileSize = 7,
                        CreatedAt = DateTime.UtcNow
                    }
                }
            }
        };
        _context.RootFolders.AddRange(firstRoot, secondRoot, emptyRoot);
        _context.ContentHierarchyItems.AddRange(firstItem, coveredItem, secondItem);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var before = await _subject.GetContentSizesAsync().ConfigureAwait(false);
        await (
            from coverage in _context.MediaFileCoverages
            where coverage.ContentItemId == coveredItemId
            select coverage)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
        await (
            from file in _context.MediaFiles
            where file.ContentItemId == firstItem.ContentItem.Id
            select file).ExecuteUpdateAsync(
                setters => setters.SetProperty(file => file.ContentItemId, coveredItemId)).ConfigureAwait(false);
        var result = await _subject.GetContentSizesAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before[firstRoot.Id], Is.EqualTo(150));
            Assert.That(before[secondRoot.Id], Is.EqualTo(7));
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[firstRoot.Id], Is.EqualTo(150));
            Assert.That(result[secondRoot.Id], Is.EqualTo(7));
            Assert.That(result.Values.Sum(), Is.EqualTo(157));
            Assert.That(result.ContainsKey(emptyRoot.Id), Is.False);
        }
    }

    [TestCase("/media", "/media/movies", "/media/movies/film.mkv", 0, 100)]
    [TestCase("/media/", "/media/movies/", "/media/movies/film.mkv", 0, 100)]
    [TestCase("/media/movies", "/media/movies-old", "/media/movies-old/film.mkv", 0, 100)]
    [TestCase("/media/movies", "/media/shows", "/media/movies-old/film.mkv", 0, 0)]
    [TestCase("/media", "/media/movies", "/media/movies/../shows/film.mkv", 100, 0)]
    [TestCase("/", "/media", "/other/film.mkv", 100, 0)]
    [TestCase("/media/movies_%", "/media/shows", "/media/movies_ab/film.mkv", 0, 0)]
    [TestCase("/media/movies", "/media/shows", "/media/MOVIES/film.mkv", 100, 0, IncludePlatform = "Win")]
    [TestCase("/media/movies", "/media/shows", "/media/MOVIES/film.mkv", 0, 0, ExcludePlatform = "Win")]
    [TestCase(@"C:\media", @"C:\media\movies\", "C:/media/movies/film.mkv", 0, 100, IncludePlatform = "Win")]
    [TestCase(@"\\host\media", @"\\host\media\tv", @"\\HOST\MEDIA\tv\file.mkv", 0, 100, IncludePlatform = "Win")]
    public async Task GetContentSizesAsync_CountsFilesInTheClosestContainingRoot_WhenPathsDiffer(
        string firstRootPath,
        string secondRootPath,
        string filePath,
        long firstRootBytes,
        long secondRootBytes)
    {
        // Arrange
        var firstRoot = new RootFolder
        {
            Name = "First",
            Path = firstRootPath,
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow
        };
        var secondRoot = new RootFolder
        {
            Name = "Second",
            Path = secondRootPath,
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow
        };
        var item = new ContentHierarchyItem
        {
            MetadataContainer = new() { CreatedAt = DateTime.UtcNow },
            ContentType = _contentType,
            Level = "movie",
            Slug = "film",
            Name = "Film",
            CreatedAt = DateTime.UtcNow,
            ContentItem = new()
            {
                CreatedAt = DateTime.UtcNow,
                MediaFiles =
                {
                    new()
                    {
                        FileKind = "Video",
                        FilePath = filePath,
                        PathKey = filePath,
                        FileSize = 100,
                        CreatedAt = DateTime.UtcNow
                    }
                }
            }
        };
        _context.RootFolders.AddRange(firstRoot, secondRoot);
        _context.ContentHierarchyItems.Add(item);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetContentSizesAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetValueOrDefault(firstRoot.Id), Is.EqualTo(firstRootBytes));
            Assert.That(result.GetValueOrDefault(secondRoot.Id), Is.EqualTo(secondRootBytes));
        }
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllRootFolders_WhenFoldersExist()
    {
        // Arrange
        var rootFolder1 = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var rootFolder2 = new RootFolder
        {
            Name = "TV Shows",
            Path = "/media/tv",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.AddRange(rootFolder1, rootFolder2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoRootFolders()
    {
        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_CreatesRootFolder_WhenRequestIsValid()
    {
        // Arrange
        var model = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Name == "Movies")
            .SingleOrDefaultAsync().ConfigureAwait(false);
        var audit = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("Movies"));
            Assert.That(result, Has.Property(nameof(result.Path)).EqualTo("/media/movies"));
            Assert.That(result, Has.Property(nameof(result.ContentType)).EqualTo(_contentType));
            Assert.That(entity, Has.Property(nameof(entity.Name)).EqualTo("Movies"));
            Assert.That(entity, Has.Property(nameof(entity.Path)).EqualTo("/media/movies"));
            Assert.That(entity, Has.Property(nameof(entity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(audit, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesRootFolder_WhenFolderExists()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        const string newContentType = "type-c";
        var model = new RootFolder
        {
            Id = rootFolder.Id,
            Name = "Updated Movies",
            Path = "/media/movies-updated",
            ContentType = newContentType,
            CreatedAt = rootFolder.CreatedAt,
            CreatedByUserId = rootFolder.CreatedByUserId
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Id == rootFolder.Id)
            .SingleOrDefaultAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entity, Has.Property(nameof(entity.Name)).EqualTo("Updated Movies"));
            Assert.That(entity, Has.Property(nameof(entity.Path)).EqualTo("/media/movies-updated"));
            Assert.That(entity, Has.Property(nameof(entity.ContentType)).EqualTo(newContentType));
            Assert.That(entity, Has.Property(nameof(entity.ModifiedAt)).Not.Null);
            Assert.That(entity, Has.Property(nameof(entity.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_PersistsRenameAndImportFields_WhenValuesAreProvided()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType,
            ImportMode = ImportMode.Move,
            DefaultMonitoring = MonitoringMode.Monitored,
            RenameTemplate = null,
            DefaultImportCollisionMode = ImportCollisionMode.Fail,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var model = new RootFolder
        {
            Id = rootFolder.Id,
            Name = rootFolder.Name,
            Path = rootFolder.Path,
            ContentType = rootFolder.ContentType,
            ImportMode = ImportMode.Copy,
            DefaultMonitoring = null,
            RenameTemplate = "{Title}.{Ext}",
            DefaultImportCollisionMode = ImportCollisionMode.Replace,
            CreatedAt = rootFolder.CreatedAt,
            CreatedByUserId = rootFolder.CreatedByUserId
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Id == rootFolder.Id)
            .SingleOrDefaultAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entity, Has.Property(nameof(entity.RenameTemplate)).EqualTo("{Title}.{Ext}"));
            Assert.That(entity, Has.Property(nameof(entity.ImportMode)).EqualTo(ImportMode.Copy));
            Assert.That(entity, Has.Property(nameof(entity.DefaultMonitoring)).EqualTo(null));
            Assert.That(
                entity,
                Has.Property(nameof(entity.DefaultImportCollisionMode)).EqualTo(ImportCollisionMode.Replace));
        }
    }

    [Test]
    public async Task CreateAsync_DefaultsUpgradeFileHandlingToReplace_WhenValueIsNotProvided()
    {
        // Arrange
        var model = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType
        };

        // Act
        await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Name == "Movies")
            .SingleOrDefaultAsync().ConfigureAwait(false);

        Assert.That(entity, Has.Property(nameof(entity.UpgradeFileHandling)).EqualTo(UpgradeFileHandling.Replace));
    }

    [Test]
    public async Task CreateAndUpdateAsync_RoundTripsUpgradeFileHandling_WhenValueChanges()
    {
        // Arrange
        var model = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType,
            UpgradeFileHandling = UpgradeFileHandling.KeepBoth
        };

        var created = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Act
        var afterCreate = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Id == created.Id)
            .SingleOrDefaultAsync().ConfigureAwait(false);

        // Assert
        Assert.That(
            afterCreate,
            Has.Property(nameof(afterCreate.UpgradeFileHandling)).EqualTo(UpgradeFileHandling.KeepBoth));

        var update = created with { UpgradeFileHandling = UpgradeFileHandling.Replace };
        var result = await _subject.UpdateAsync(update, _userId).ConfigureAwait(false);
        var afterUpdate = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Id == created.Id)
            .SingleOrDefaultAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(
                afterUpdate,
                Has.Property(nameof(afterUpdate.UpgradeFileHandling)).EqualTo(UpgradeFileHandling.Replace));
        }
    }

    [Test]
    public async Task UpdateAsync_ClearsRenameTemplate_WhenSetToNull()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType,
            RenameTemplate = "{Title}.{Ext}",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var model = new RootFolder
        {
            Id = rootFolder.Id,
            Name = rootFolder.Name,
            Path = rootFolder.Path,
            ContentType = rootFolder.ContentType,
            RenameTemplate = null,
            CreatedAt = rootFolder.CreatedAt,
            CreatedByUserId = rootFolder.CreatedByUserId
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Id == rootFolder.Id)
            .SingleOrDefaultAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entity, Has.Property(nameof(entity.RenameTemplate)).Null);
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenRootFolderDoesNotExist()
    {
        // Arrange
        var model = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "NonExistent",
            Path = "/media/nonexistent",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var result = await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteAsync_DeletesRootFolder_WhenFolderExists()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "ToDelete",
            Path = "/media/to-delete",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(rootFolder.Id, _userId).ConfigureAwait(false);

        // Assert
        var entityExists = await _context.RootFolders.AnyAsync(rf => rf.Id == rootFolder.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entityExists, Is.False);
        }
    }

    [Test]
    public async Task DeleteAsync_RaisesAnUpdateForEachImportCandidateAfterCommit_WhenTheFolderHasCandidates()
    {
        // Arrange
        var rootFolder = new RootFolder
        {
            Name = "ToDelete",
            Path = "/media/to-delete",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var candidates = new[] { LibraryImportCandidateStatus.Pending, LibraryImportCandidateStatus.Queued }
            .Select(status => new LibraryImportCandidate
            {
                RootFolderId = rootFolder.Id,
                ScanRunId = Guid.NewGuid(),
                FilePath = $"/media/to-delete/{status}.mkv",
                ContentType = _contentType,
                Status = status,
                CreatedAt = DateTime.UtcNow
            })
            .ToList();
        _context.LibraryImportCandidates.AddRange(candidates);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.DeleteAsync(rootFolder.Id, _userId).ConfigureAwait(false);

        // Assert
        var remaining = await _context.LibraryImportCandidates.CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(remaining, Is.Zero);
            Assert.That(_publishedInsideTransaction, Is.False);
            Assert.That(
                _publishedEvents
                    .Where(magpieEvent => magpieEvent.Name == "library.importCandidate.updated")
                    .Select(magpieEvent => magpieEvent.Context["library.importCandidateId"]),
                Is.EquivalentTo(candidates.Select(candidate => candidate.Id.ToString("D"))));
            Assert.That(
                _publishedEvents,
                Has.One.With.Property(nameof(MagpieEvent.Name)).EqualTo("library.rootFolder.deleted"));
        }
    }

    [Test]
    public async Task DeleteAsync_PromotesTheOldestRemainingFolder_WhenTheDeletedFolderWasTheDefault()
    {
        // Arrange
        var first = new RootFolder
        {
            Name = "First",
            Path = "/media/first",
            ContentType = _contentType,
            IsDefault = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = _userId
        };
        var second = new RootFolder
        {
            Name = "Second",
            Path = "/media/second",
            ContentType = _contentType,
            IsDefault = false,
            CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = _userId
        };
        var third = new RootFolder
        {
            Name = "Third",
            Path = "/media/third",
            ContentType = _contentType,
            IsDefault = false,
            CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = _userId
        };
        _context.RootFolders.AddRange(first, second, third);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(first.Id, _userId).ConfigureAwait(false);

        // Assert
        var defaults = await _context.RootFolders
            .AsNoTracking()
            .Where(folder => folder.ContentType == _contentType && folder.IsDefault)
            .Select(folder => folder.Id)
            .ToListAsync()
            .ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(defaults, Is.EqualTo([second.Id]));
        }
    }

    [Test]
    public async Task UpdateAsync_PromotesTheOldestRemainingFolder_WhenTheDefaultLeavesItsContentType()
    {
        // Arrange
        var moved = new RootFolder
        {
            Name = "Moved",
            Path = "/media/moved",
            ContentType = _contentType,
            IsDefault = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = _userId
        };
        var stayed = new RootFolder
        {
            Name = "Stayed",
            Path = "/media/stayed",
            ContentType = _contentType,
            IsDefault = false,
            CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = _userId
        };
        _context.RootFolders.AddRange(moved, stayed);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject.UpdateAsync(
            new RootFolder
            {
                Id = moved.Id,
                Name = "Moved",
                Path = "/media/moved",
                ContentType = "type-b",
                IsDefault = true,
                CreatedAt = moved.CreatedAt
            },
            _userId).ConfigureAwait(false);

        // Assert
        var defaults = await _context.RootFolders
            .AsNoTracking()
            .Where(folder => folder.ContentType == _contentType && folder.IsDefault)
            .Select(folder => folder.Id)
            .ToListAsync()
            .ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(defaults, Is.EqualTo([stayed.Id]));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenRootFolderDoesNotExist()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CreateAsync_CreatesRootFolder_WhenNullUserIdIsProvided()
    {
        // Arrange
        var model = new RootFolder
        {
            Name = "System Folder",
            Path = "/media/system",
            ContentType = _contentType
        };

        // Act
        var result = await _subject.CreateAsync(model, null).ConfigureAwait(false);

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Name == "System Folder")
            .SingleOrDefaultAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Name)).EqualTo("System Folder"));
            Assert.That(entity, Has.Property(nameof(entity.CreatedByUserId)).Null);
        }
    }

    [Test]
    public async Task UpdateAsync_DoesNotCreateAuditRecord_WhenNoRowsUpdated()
    {
        // Arrange
        var model = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "NonExistent",
            Path = "/media/nonexistent",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await _subject.UpdateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        Assert.That(auditRecords, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_DoesNotCreateAuditRecord_WhenRootFolderDoesNotExist()
    {
        // Act
        await _subject.DeleteAsync(Guid.NewGuid(), _userId).ConfigureAwait(false);

        // Assert
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);
        Assert.That(auditRecords, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_PublishesRootFolderCreatedEvent_WhenCreateSucceeds()
    {
        // Arrange
        var model = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_publishedEvents, Has.Count.EqualTo(1));
            Assert.That(_publishedEvents[0].Name, Is.EqualTo("library.rootFolder.created"));
            Assert.That(
                _publishedEvents[0].Context["rootFolder.id"],
                Is.EqualTo(result.Id.ToString("D")));
            Assert.That(_publishedEvents[0].Context["rootFolder.path"], Is.EqualTo("/media/movies"));
            Assert.That(_publishedEvents[0].Context["rootFolder.contentType"], Is.EqualTo(_contentType));
        }
    }

    [Test]
    public async Task CreateAsync_ThrowsAfterCommittedWrite_WhenEventPublishFails()
    {
        // Arrange
        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var failing = new Mock<IEventBus>();
        failing
            .Setup(x => x.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("publish failed"));
        var subject = new RootFolderRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<ILibraryImportCandidateRepository>(),
            failing.Object,
            NullLogger<RootFolderRepository>.Instance,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var model = new RootFolder
        {
            Name = "Movies",
            Path = "/media/movies",
            ContentType = _contentType
        };

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await subject.CreateAsync(model, _userId, default).ConfigureAwait(false));

        // Assert
        var entity = await _context.RootFolders
            .AsNoTracking()
            .Where(rf => rf.Path == model.Path)
            .SingleOrDefaultAsync().ConfigureAwait(false);
        failing.Verify(x => x.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception, Has.Message.EqualTo("publish failed"));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity, Has.Property(nameof(entity.Name)).EqualTo("Movies"));
        }
    }

    [Test]
    public async Task UpdateAsync_InvalidatesImportCandidatesLeftOutsideAllRoots_WhenRootRepointed()
    {
        // Arrange
        var movies = new RootFolder
        {
            Name = "Movies",
            Path = @"\\mediahost\movies",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var shows = new RootFolder
        {
            Name = "Shows",
            Path = @"D:\shows",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.AddRange(movies, shows);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var orphaned = new LibraryImportCandidate
        {
            RootFolderId = movies.Id,
            ScanRunId = Guid.NewGuid(),
            FilePath = @"\\mediahost\movies\Heat (1995)\heat.mkv",
            ContentType = _contentType,
            Status = LibraryImportCandidateStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var stillValid = new LibraryImportCandidate
        {
            RootFolderId = shows.Id,
            ScanRunId = Guid.NewGuid(),
            FilePath = @"D:\shows\The Wire\s01e01.mkv",
            ContentType = _contentType,
            Status = LibraryImportCandidateStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var alreadyResolved = new LibraryImportCandidate
        {
            RootFolderId = movies.Id,
            ScanRunId = Guid.NewGuid(),
            FilePath = @"\\mediahost\movies\Old (1999)\old.mkv",
            ContentType = _contentType,
            Status = LibraryImportCandidateStatus.Queued,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var orphanedMatchingPending = new LibraryImportCandidate
        {
            RootFolderId = movies.Id,
            ScanRunId = Guid.NewGuid(),
            FilePath = @"\\mediahost\movies\Speed (1994)\speed.mkv",
            ContentType = _contentType,
            Status = LibraryImportCandidateStatus.MatchingPending,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.LibraryImportCandidates.AddRange(orphaned, stillValid, alreadyResolved, orphanedMatchingPending);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var repointed = movies with { Path = @"D:\movies" };
        var updated = await _subject.UpdateAsync(repointed, _userId, default).ConfigureAwait(false);

        // Assert
        var orphanedAfter = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(m => m.Id == orphaned.Id).ConfigureAwait(false);
        var stillValidAfter = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(m => m.Id == stillValid.Id).ConfigureAwait(false);
        var resolvedAfter = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(m => m.Id == alreadyResolved.Id).ConfigureAwait(false);
        var orphanedMatchingPendingAfter = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(m => m.Id == orphanedMatchingPending.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.True);
            Assert.That(orphanedAfter.Status, Is.EqualTo(LibraryImportCandidateStatus.Invalidated));
            Assert.That(orphanedAfter.ModifiedByUserId, Is.EqualTo(_userId));
            Assert.That(stillValidAfter.Status, Is.EqualTo(LibraryImportCandidateStatus.Pending));
            Assert.That(resolvedAfter.Status, Is.EqualTo(LibraryImportCandidateStatus.Queued));
            Assert.That(orphanedMatchingPendingAfter.Status, Is.EqualTo(LibraryImportCandidateStatus.Invalidated));
            Assert.That(
                _publishedEvents
                    .Where(magpieEvent => magpieEvent.Name == "library.importCandidate.updated")
                    .Select(magpieEvent => magpieEvent.Context["library.importCandidateId"]),
                Is.EquivalentTo([orphaned.Id.ToString("D"), orphanedMatchingPending.Id.ToString("D")]));
        }
    }

    [Test]
    public async Task UpdateAsync_LeavesImportCandidatesAlone_WhenPathStillCoversThem()
    {
        // Arrange
        var movies = new RootFolder
        {
            Name = "Movies",
            Path = @"D:\movies",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.Add(movies);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var match = new LibraryImportCandidate
        {
            RootFolderId = movies.Id,
            ScanRunId = Guid.NewGuid(),
            FilePath = @"D:\movies\Heat (1995)\heat.mkv",
            ContentType = _contentType,
            Status = LibraryImportCandidateStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.LibraryImportCandidates.Add(match);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var renamed = movies with { Name = "Films" };
        await _subject.UpdateAsync(renamed, _userId, default).ConfigureAwait(false);

        // Assert
        var after = await _context.LibraryImportCandidates.AsNoTracking().SingleAsync(m => m.Id == match.Id)
            .ConfigureAwait(false);
        Assert.That(after.Status, Is.EqualTo(LibraryImportCandidateStatus.Pending));
    }

    [Test]
    public async Task UpdateAsync_ReplacesLevelDefaultAndRecomputesEffectiveValues_WhenMappingChanges()
    {
        // Arrange
        var firstTierList = new QualityTierList
        {
            Name = "First",
            CreatedAt = DateTime.UtcNow,
        };
        var secondTierList = new QualityTierList
        {
            Name = "Second",
            CreatedAt = DateTime.UtcNow,
        };
        var rootFolder = new RootFolder
        {
            Name = "Books",
            Path = "/books",
            ContentType = _contentType,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
        };
        _context.QualityTierLists.AddRange(firstTierList, secondTierList);
        rootFolder.Defaults = new()
        {
            ["book"] = new RootFolderDefault
            {
                QualityTierListId = firstTierList.Id,
            },
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var updated = rootFolder with
        {
            Defaults = new()
            {
                ["book"] = new RootFolderDefault
                {
                    QualityTierListId = secondTierList.Id,
                },
            },
        };

        // Act
        var result = await _subject.UpdateAsync(updated, _userId).ConfigureAwait(false);

        // Assert
        var stored = (await _context.RootFolders.AsNoTracking().SingleAsync().ConfigureAwait(false))
            .Defaults["book"];
        var published = _publishedEvents.Single(magpieEvent =>
            magpieEvent.Name == "library.rootFolder.updated");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(stored.QualityTierListId, Is.EqualTo(secondTierList.Id));
            Assert.That(
                published.Context["rootFolder.changedFields"],
                Does.Contain("Defaults"));
        }

        _contentHierarchyItems.Verify(
            items => items.RecomputeAllEffectiveValuesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task CreateAsync_ReplacesTheDefaultAndRecomputes_WhenANewDefaultIsCreated()
    {
        // Arrange
        var first = await _subject.CreateAsync(
            new RootFolder
            {
                Name = "First",
                Path = "/library/first",
                ContentType = _contentType
            },
            _userId).ConfigureAwait(false);

        // Act
        var second = await _subject.CreateAsync(
            new RootFolder
            {
                Name = "Second",
                Path = "/library/second",
                ContentType = _contentType,
                IsDefault = true
            },
            _userId).ConfigureAwait(false);

        // Assert
        var roots = await _context.RootFolders.AsNoTracking()
            .Where(folder => folder.ContentType == _contentType)
            .ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(roots.Single(folder => folder.Id == first.Id).IsDefault, Is.False);
            Assert.That(roots.Single(folder => folder.Id == second.Id).IsDefault, Is.True);
        }

        _contentHierarchyItems.Verify(
            items => items.RecomputeAllEffectiveValuesAsync(It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Test]
    public async Task PatchAsync_LeavesUnnamedColumnsUnchanged_WhenOnlyTheNameChanges()
    {
        // Arrange
        var tierList = new QualityTierList
        {
            Name = "Tiers",
            CreatedAt = DateTime.UtcNow,
        };
        var rootFolder = new RootFolder
        {
            Name = "Old name",
            Path = "/library/books",
            ContentType = _contentType,
            IsDefault = true,
            DefaultMonitoring = MonitoringMode.Monitored,
            RenameTemplate = "{Title}.{Ext}",
            UpgradeFileHandling = UpgradeFileHandling.KeepBoth,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = _userId
        };
        _context.QualityTierLists.Add(tierList);
        rootFolder.Defaults = new()
        {
            ["book"] = new RootFolderDefault
            {
                QualityTierListId = tierList.Id,
            },
        };
        _context.RootFolders.Add(rootFolder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject.PatchAsync(
            new RootFolderPatch
            {
                Id = rootFolder.Id,
                Name = new() { Value = "New name" }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var stored = await _context.RootFolders.AsNoTracking().SingleAsync().ConfigureAwait(false);
        var published = _publishedEvents.Single(magpieEvent =>
            magpieEvent.Name == "library.rootFolder.updated");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(stored.Name, Is.EqualTo("New name"));
            Assert.That(stored.Path, Is.EqualTo("/library/books"));
            Assert.That(stored.ContentType, Is.EqualTo(_contentType));
            Assert.That(stored.IsDefault, Is.True);
            Assert.That(stored.DefaultMonitoring, Is.EqualTo(MonitoringMode.Monitored));
            Assert.That(stored.RenameTemplate, Is.EqualTo("{Title}.{Ext}"));
            Assert.That(stored.UpgradeFileHandling, Is.EqualTo(UpgradeFileHandling.KeepBoth));
            Assert.That(stored.Defaults["book"].QualityTierListId, Is.EqualTo(tierList.Id));
            Assert.That(
                published.Context["rootFolder.changedFields"],
                Is.EqualTo("Name"));
        }
    }

    [Test]
    public async Task PatchAsync_PromotesTheOldestRemainingFolder_WhenTheDefaultLeavesItsContentType()
    {
        // Arrange
        var moved = new RootFolder
        {
            Name = "Moved",
            Path = "/media/moved",
            ContentType = _contentType,
            IsDefault = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = _userId
        };
        var stayed = new RootFolder
        {
            Name = "Stayed",
            Path = "/media/stayed",
            ContentType = _contentType,
            IsDefault = false,
            CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = _userId
        };
        _context.RootFolders.AddRange(moved, stayed);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject.PatchAsync(
            new RootFolderPatch
            {
                Id = moved.Id,
                ContentType = new() { Value = "type-b" }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var defaults = await _context.RootFolders
            .AsNoTracking()
            .Where(folder => folder.ContentType == _contentType && folder.IsDefault)
            .Select(folder => folder.Id)
            .ToListAsync()
            .ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(defaults, Is.EqualTo([stayed.Id]));
        }

        _contentHierarchyItems.Verify(
            items => items.RecomputeAllEffectiveValuesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task PatchAsync_UnmarksTheOtherDefault_WhenTheFolderBecomesTheDefault()
    {
        // Arrange
        var incumbent = new RootFolder
        {
            Name = "Incumbent",
            Path = "/media/incumbent",
            ContentType = _contentType,
            IsDefault = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = _userId
        };
        var challenger = new RootFolder
        {
            Name = "Challenger",
            Path = "/media/challenger",
            ContentType = _contentType,
            IsDefault = false,
            CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = _userId
        };
        _context.RootFolders.AddRange(incumbent, challenger);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject.PatchAsync(
            new RootFolderPatch
            {
                Id = challenger.Id,
                IsDefault = new() { Value = true }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var defaults = await _context.RootFolders
            .AsNoTracking()
            .Where(folder => folder.ContentType == _contentType && folder.IsDefault)
            .Select(folder => folder.Id)
            .ToListAsync()
            .ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(defaults, Is.EqualTo([challenger.Id]));
        }
    }

    [Test]
    public async Task PatchAsync_InvalidatesImportCandidatesLeftOutsideAllRoots_WhenTheRootIsRepointed()
    {
        // Arrange
        var movies = new RootFolder
        {
            Name = "Movies",
            Path = @"\\mediahost\movies",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var shows = new RootFolder
        {
            Name = "Shows",
            Path = @"D:\shows",
            ContentType = _contentType,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.RootFolders.AddRange(movies, shows);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var orphaned = new LibraryImportCandidate
        {
            RootFolderId = movies.Id,
            ScanRunId = Guid.NewGuid(),
            FilePath = @"\\mediahost\movies\Heat (1995)\heat.mkv",
            ContentType = _contentType,
            Status = LibraryImportCandidateStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        var stillValid = new LibraryImportCandidate
        {
            RootFolderId = shows.Id,
            ScanRunId = Guid.NewGuid(),
            FilePath = @"D:\shows\The Wire\s01e01.mkv",
            ContentType = _contentType,
            Status = LibraryImportCandidateStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = _userId
        };
        _context.LibraryImportCandidates.AddRange(orphaned, stillValid);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject.PatchAsync(
            new RootFolderPatch
            {
                Id = movies.Id,
                Path = new() { Value = @"D:\movies" }
            },
            _userId).ConfigureAwait(false);

        // Assert
        var orphanedAfter = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == orphaned.Id).ConfigureAwait(false);
        var stillValidAfter = await _context.LibraryImportCandidates.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == stillValid.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(orphanedAfter.Status, Is.EqualTo(LibraryImportCandidateStatus.Invalidated));
            Assert.That(orphanedAfter.ModifiedByUserId, Is.EqualTo(_userId));
            Assert.That(stillValidAfter.Status, Is.EqualTo(LibraryImportCandidateStatus.Pending));
            Assert.That(
                _publishedEvents.Count(magpieEvent =>
                    magpieEvent.Name == "library.importCandidate.updated"),
                Is.EqualTo(1));
        }
    }

    [Test]
    public async Task PatchAsync_StoresAutoImportAndReportsTheField_WhenAutoImportIsTurnedOn()
    {
        // Arrange
        var created = await _subject.CreateAsync(
            new RootFolder { Name = "Movies", Path = "/media/movies", ContentType = _contentType },
            _userId).ConfigureAwait(false);
        var afterCreate = await _context.RootFolders.AsNoTracking().SingleAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.PatchAsync(
            new RootFolderPatch { Id = created.Id, AutoImport = new() { Value = true } },
            _userId).ConfigureAwait(false);

        // Assert
        var stored = await _context.RootFolders.AsNoTracking().SingleAsync().ConfigureAwait(false);
        var published = _publishedEvents.Last(magpieEvent => magpieEvent.Name == "library.rootFolder.updated");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterCreate.AutoImport, Is.False);
            Assert.That(result, Is.True);
            Assert.That(stored.AutoImport, Is.True);
            Assert.That(published.Context["rootFolder.changedFields"], Is.EqualTo("AutoImport"));
        }
    }
}
