using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Content;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Models.Signals;
using Magpie.Sdk.Content;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class RootFolderContentDiscoveryJobWorkflowTests
{
    private Mock<ITransactionManager> _transactions = null!;
    private Mock<IAuditRecordRepository> _auditRecords = null!;

    private Guid _userId;
    private Guid _rootFolderId;
    private RootFolder _rootFolder = null!;
    private List<string> _files = null!;
    private List<LibraryImportCandidate> _createdCandidates = null!;
    private Func<string, IReadOnlyDictionary<string, IReadOnlyList<string>>?> _parse = null!;
    private string? _levelSlug;
    private Mock<IPathResolutionContentTypeDefinitionProvider> _groupingProvider = null!;
    private ContentTypeDefinition _definition = null!;
    private Mock<IFileSystem> _fileSystem = null!;
    private Mock<IContentTypeRegistry> _contentTypeRegistry = null!;
    private Mock<IRootFolderRepository> _rootFolders = null!;
    private Mock<ILibraryImportCandidateRepository> _candidates = null!;
    private Mock<IMediaFileRepository> _mediaFiles = null!;
    private Mock<IContentHierarchyItemRepository> _hierarchyItems = null!;
    private Mock<IContentFileService> _contentFiles = null!;
    private Mock<IJobManager> _jobManager = null!;
    private Mock<ILibraryImportService> _libraryImport = null!;
    private Mock<IProgressReporter> _progress = null!;
    private IJob _scanJob = null!;

    [SetUp]
    public void SetUp()
    {
        _auditRecords = new Mock<IAuditRecordRepository> { CallBase = true };
        _transactions = new Mock<ITransactionManager>();
        _transactions.Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken token) => operation(token));

        _userId = Guid.NewGuid();

        _files = [];
        _fileSystem = new Mock<IFileSystem>();
        _fileSystem.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);
        _fileSystem.Setup(f => f.EnumerateFiles(It.IsAny<string>())).Returns(() => _files);
        _fileSystem.Setup(f => f.PathsEqual(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string left, string right) => Path.GetRelativePath(left, right) == ".");
        _fileSystem.Setup(f => f.GetFullPath(It.IsAny<string>()))
            .Returns((string path) => Path.GetFullPath(path));

        _levelSlug = null;
        _parse = path => new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, Path.GetFileNameWithoutExtension(path))
                .AddValue(ParsedFileSignalKeys.TitleToken, Path.GetFileNameWithoutExtension(path))
                .Build();

        _definition = new ContentTypeDefinition
        {
            Slug = "type-a",
            DisplayName = "type-a",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-a",
                    ParentSlug = null,
                    DisplayName = "type-a",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = [".mkv", ".mp4"]
                }
            ]
        };
        var definitionProvider = new Mock<IContentTypeDefinitionProvider>();
        definitionProvider.SetupGet(p => p.Definition).Returns(() => _definition);
        definitionProvider
            .Setup(p => p.Parse(It.IsAny<string>(), out It.Ref<string?>.IsAny))
            .Returns((string path, out string? levelSlug) =>
            {
                levelSlug = _levelSlug;
                return _parse(path);
            });

        _groupingProvider = new Mock<IPathResolutionContentTypeDefinitionProvider> { CallBase = true };
        _groupingProvider.SetupGet(provider => provider.Definition).Returns(() => _definition);
        _groupingProvider.Setup(provider => provider.Parse(It.IsAny<string>(), out It.Ref<string?>.IsAny))
            .Returns((string path, out string? levelSlug) =>
            {
                levelSlug = _levelSlug;
                return _parse(path);
            });

        _rootFolderId = Guid.NewGuid();
        _rootFolder = new RootFolder
        {
            Id = _rootFolderId,
            Name = "Movies",
            Path = "/media/movies",
            ContentType = "type-a",
            CreatedAt = DateTime.UtcNow
        };
        _rootFolders = new Mock<IRootFolderRepository> { CallBase = true };
        _rootFolders
            .Setup(r => r.GetByIdAsync(_rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _rootFolder);

        _createdCandidates = [];
        _candidates = new Mock<ILibraryImportCandidateRepository> { CallBase = true };
        _candidates
            .Setup(r => r.CreateAsync(
                It.IsAny<LibraryImportCandidate>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((LibraryImportCandidate model, Guid? _, CancellationToken _) => _createdCandidates.Add(model))
            .ReturnsAsync((LibraryImportCandidate model, Guid? _, CancellationToken _) => model);
        _candidates
            .Setup(r => r.DeleteByRootFolderAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _candidates
            .Setup(r => r.QueryAsync(
                It.IsAny<LibraryImportCandidateQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate> { Items = [] });

        _mediaFiles = new Mock<IMediaFileRepository> { CallBase = true };
        _mediaFiles
            .Setup(r => r.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>());

        _jobManager = new Mock<IJobManager>();
        _jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(b => b.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _fileSystem
            .Setup(service => service.MatchesAnyFileTypeAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _contentTypeRegistry = new Mock<IContentTypeRegistry>();
        _contentTypeRegistry
            .Setup(r => r.GetChildLevels(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string _, string levelSlug) =>
                [.. _definition.LevelDefinitions.Where(level => level.ParentSlug == levelSlug)]);

        _contentTypeRegistry.Setup(registry => registry.FindProviderFor("type-a"))
            .Returns(definitionProvider.Object);

        _fileSystem.Setup(f => f.IsPathWithin(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string root, string path) =>
                !Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path))
                    .StartsWith("..", StringComparison.Ordinal));

        _progress = new Mock<IProgressReporter>();
        _libraryImport = new Mock<ILibraryImportService>();
        _libraryImport
            .Setup(s => s.RefreshLibraryTargetsAsync(
                It.IsAny<IReadOnlyCollection<LibraryImportCandidate>>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        _contentFiles = new Mock<IContentFileService>();
        _scanJob = new RootFolderContentDiscoveryJob(
            _rootFolders.Object,
            _candidates.Object,
            _mediaFiles.Object,
            _hierarchyItems.Object,
            _contentFiles.Object,
            _auditRecords.Object,
            _transactions.Object,
            _contentTypeRegistry.Object,
            _jobManager.Object,
            _libraryImport.Object,
            _fileSystem.Object,
            eventBus.Object,
            _progress.Object,
            TimeProvider.System,
            NullLogger<RootFolderContentDiscoveryJob>.Instance);
    }

    [TestCase(null, 60, "direct")]
    [TestCase("15", 15, "direct")]
    [TestCase(null, 60, "schedule")]
    [TestCase("180", 180, "schedule")]
    [TestCase(null, 60, "event")]
    [TestCase("15", 15, "event")]
    public async Task ExecuteAsync_PassesConfiguredCutoff_WhenRecoveryRuns(
        string? configured, int minutes, string trigger)
    {
        // Arrange
        var candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/a.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Importing,
            CreatedAt = DateTime.MinValue
        };
        _candidates.Setup(repository => repository.QueryAsync(It.Is<LibraryImportCandidateQuery>(query =>
                    query.Statuses != null), It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate> { Items = [candidate] });
        var parameters = new Dictionary<string, string> { ["rootFolderId"] = _rootFolderId.ToString("D") };
        if (configured is not null)
        {
            parameters["recoveryInactivityMinutes"] = configured;
        }

        MagpieEvent? importEvent = null;
        if (trigger == "event")
        {
            parameters.Remove("rootFolderId");
            importEvent = new MagpieEvent(Guid.NewGuid(), "library.scan.requested",
                new Dictionary<string, string> { ["rootFolder.id"] = _rootFolderId.ToString("D") },
                DateTime.UtcNow, DateTime.UtcNow, null);
        }

        var context = new JobContext(Guid.NewGuid(), trigger == "schedule" ? Guid.NewGuid() : null,
            trigger == "event" ? Guid.NewGuid() : null, _userId, JobExecutionMode.Queue, parameters, importEvent);
        DateTime cutoff = default;
        _candidates.Setup(repository => repository.TryReclaimInactiveImportAsync(candidate.Id,
                It.IsAny<DateTime>(), _userId, It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime, Guid?, CancellationToken>((_, value, _, _) => cutoff = value).ReturnsAsync(false);

        // Act
        var before = DateTime.UtcNow;
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);
        var after = DateTime.UtcNow;

        // Assert
        Assert.That(cutoff, Is.InRange(before.AddMinutes(-minutes), after.AddMinutes(-minutes)));
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("1.5")]
    [TestCase("bad")]
    public void ExecuteAsync_RejectsInvalidRecoveryThreshold_WhenConfigured(string value)
    {
        // Arrange
        var context = new JobContext(Guid.NewGuid(), null, null, _userId, JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString("D"),
                ["recoveryInactivityMinutes"] = value
            }, null);

        // Act
        var error = Assert.ThrowsAsync<ArgumentException>(() => _scanJob.ExecuteAsync(context));

        // Assert
        Assert.That(error!.Message, Is.EqualTo("Recovery inactivity minutes must be a positive integer."));
    }

    [Test]
    public void ScanAsync_Throws_WhenRootFolderNotFound()
    {
        // Arrange
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = Guid.NewGuid().ToString()
            },
            Event: null);

        // Act
        void Act() => _scanJob.ExecuteAsync(context).GetAwaiter().GetResult();

        // Assert
        var ex = Assert.Throws<InvalidOperationException>(Act);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Message, Does.Contain("Root folder"));
        }
    }

    [Test]
    public async Task ScanAsync_ReportsStopWithoutPersisting_WhenProviderIsMissing()
    {
        // Arrange
        _files = ["/media/movies/a.mkv"];
        _rootFolder = _rootFolder with { ContentType = "type-c" };
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_createdCandidates, Is.Empty);
            _candidates.Verify(
                r => r.DeleteByRootFolderAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<IReadOnlyCollection<Guid>?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _progress.Verify(
                value => value.Report(1, "The scan stopped because the content type has no definition provider."),
                Times.Once);
        }
    }

    [Test]
    public async Task ScanAsync_ProducesMatchingPendingRows_WhenTheFolderContainsFiles()
    {
        // Arrange
        _files = ["/media/movies/a.mkv", "/media/movies/b.mkv"];
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_createdCandidates, Has.Count.EqualTo(2));
            Assert.That(
                _createdCandidates,
                Has.All.Property(nameof(LibraryImportCandidate.Status))
                    .EqualTo(LibraryImportCandidateStatus.MatchingPending));
            Assert.That(_createdCandidates, Has.All.Property(nameof(LibraryImportCandidate.Matches)).Empty);
            Assert.That(_createdCandidates, Has.All.Property(nameof(LibraryImportCandidate.Notes)).Null);
            Assert.That(
                _createdCandidates.Select(r => r.FilePath),
                Is.EquivalentTo(
                [
                    "/media/movies/a.mkv",
                    "/media/movies/b.mkv"
                ]));
            Assert.That(_createdCandidates.Select(r => r.ScanRunId).Distinct().Count(), Is.EqualTo(1));
            _progress.Verify(value => value.Report(0, "The scan will load the root folder."), Times.Once);
            _progress.Verify(value => value.Report(null, "The scan will list the files."), Times.Once);
            _progress.Verify(value => value.Report(0, "The scan will examine a.mkv."), Times.Once);
            _progress.Verify(value => value.Report(0.5, "The scan will examine b.mkv."), Times.Once);
            _progress.Verify(value => value.Report(1, "The root-folder scan completed."), Times.Once);
        }
    }

    [Test]
    public async Task ScanAsync_CreatesOnlyIdentifiedContent_WhenReleaseContainsExtras()
    {
        // Arrange
        _files =
        [
            "/media/movies/The Movie/The.Movie.2025.mkv",
            "/media/movies/The Movie/gag-reel.mkv",
            "/media/movies/The Movie/Featurettes/featurette.mkv",
            "/media/movies/The Movie/Sample/sample.mkv"
        ];
        _parse = path => new SignalBagBuilder()
            .AddValue(
                ParsedFileSignalKeys.Title,
                Path.GetFileName(path) switch
                {
                    "The Movie" or "The.Movie.2025.mkv" => "The Movie",
                    "gag-reel.mkv" => "gag reel",
                    "featurette.mkv" => "featurette",
                    _ => null
                })
            .Build();
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        Assert.That(_createdCandidates.Select(candidate => candidate.FilePath), Is.EqualTo(
        [
            "/media/movies/The Movie/The.Movie.2025.mkv"
        ]));
    }

    [Test]
    public async Task ScanAsync_CreatesEveryIdentifiedContentFile_WhenReleaseContainsManyFiles()
    {
        // Arrange
        _files = Enumerable.Range(1, 20)
            .Select(episode => $"/media/tv/The Show/Season 01/The.Show.S01E{episode:D2}.mkv")
            .ToList();
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        Assert.That(_createdCandidates, Has.Count.EqualTo(20));
    }

    [Test]
    public async Task ScanAsync_KeepsUnidentifiedContent_WhenReleaseHasNoIdentifiedFile()
    {
        // Arrange
        _parse = _ => null;
        _files = ["/media/movies/Unknown Release/bad-name.mkv"];
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        Assert.That(_createdCandidates.Single().FilePath, Is.EqualTo(
            "/media/movies/Unknown Release/bad-name.mkv"));
    }

    [Test]
    public async Task ScanAsync_DoesNotCreateStandaloneSidecarCandidate_WhenPrimaryIsPresent()
    {
        // Arrange
        _definition = _definition with
        {
            LevelDefinitions =
            [
                _definition.LevelDefinitions[0] with { AcceptedSidecarTypes = [".srt"] }
            ]
        };
        _fileSystem
            .Setup(service => service.MatchesAnyFileTypeAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, IReadOnlyList<string>? accepted, CancellationToken _) =>
                accepted?.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) == true);
        _files =
        [
            "/media/movies/The Movie/The.Movie.2025.mkv",
            "/media/movies/The Movie/The.Movie.2025.en.srt"
        ];
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        Assert.That(_createdCandidates.Select(candidate => candidate.FilePath), Is.EqualTo(
        [
            "/media/movies/The Movie/The.Movie.2025.mkv"
        ]));
    }

    [Test]
    public async Task ScanAsync_QuarantinesRowImmediately_WhenTheFilenameHasNoTitle()
    {
        // Arrange
        _parse = _ => null;
        _files = ["/media/movies/movie.sample.mkv"];
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        var row = _createdCandidates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Status, Is.EqualTo(LibraryImportCandidateStatus.Pending));
            Assert.That(row.Matches, Is.Empty);
            Assert.That(row.ParsedSignals, Is.Empty);
            Assert.That(row.Notes, Is.Not.Null.And.Not.Empty);
            _jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ScanAsync_PersistsParsedSignals_WhenProviderBagIsProvided()
    {
        // Arrange
        _parse = _ => new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, "The Show")
                .AddValue(ParsedFileSignalKeys.Year, 2020)
                .AddValue("season", 2)
                .AddValue("episode", 5)
                .AddValue(ParsedFileSignalKeys.TitleToken, "The Show 2020")
                .Build();
        _files = ["/media/tv/The.Show.S02E05.mkv"];
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        var row = _createdCandidates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Status, Is.EqualTo(LibraryImportCandidateStatus.MatchingPending));
            Assert.That(row.ParsedSignals[ParsedFileSignalKeys.Title].Single(), Is.EqualTo("The Show"));
            Assert.That(row.ParsedSignals[ParsedFileSignalKeys.Year].Single(), Is.EqualTo("2020"));
            Assert.That(row.ParsedSignals["season"].Single(), Is.EqualTo("2"));
            Assert.That(row.ParsedSignals["episode"].Single(), Is.EqualTo("5"));
            Assert.That(row.ParsedSignals[ParsedFileSignalKeys.TitleToken].Single(), Is.EqualTo("The Show 2020"));
        }
    }

    [Test]
    public async Task ScanAsync_EnqueuesImmediateMatchResolution_WhenItWroteAnUnresolvedCandidate()
    {
        // Arrange
        _files = ["/media/movies/a.mkv"];
        _candidates
            .Setup(r => r.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(q => q.Status == LibraryImportCandidateStatus.MatchingPending),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate> { Items = [], TotalItems = 1 });
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "LibraryImportMatchResolution" &&
                    request.NotBeforeAt == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ScanAsync_EnqueuesMatchResolution_WhenUnresolvedCandidatesExistButThisScanWroteNone()
    {
        // Arrange
        _files = [];
        _candidates
            .Setup(r => r.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(q => q.Status == LibraryImportCandidateStatus.MatchingPending),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate> { Items = [], TotalItems = 1 });
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_createdCandidates, Is.Empty);
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(request =>
                        request.JobType == "LibraryImportMatchResolution"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ScanAsync_DoesNotEnqueueMatchResolution_WhenNoUnresolvedCandidatesExistAnywhere()
    {
        // Arrange
        _files = [];
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_createdCandidates, Is.Empty);
            _jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ScanAsync_RebuildsTheRootFolderPendingSet_WhenTheFolderIsScanned()
    {
        // Arrange
        _files = ["/media/movies/a.mkv"];
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _candidates.Verify(
            r => r.DeleteByRootFolderAsync(
                _rootFolderId,
                It.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Count == 0),
                _userId,
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_createdCandidates, Has.Count.EqualTo(2));
            Assert.That(_createdCandidates.Select(r => r.ScanRunId).Distinct().Count(), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task ScanAsync_SkipsFile_WhenAlreadyRepresentedInMediaFile()
    {
        // Arrange
        _mediaFiles
            .Setup(r => r.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>
            {
                Items = [new MediaFile { FileKind = "video", FilePath = "/media/movies/a.mkv" }],
                TotalItems = 1
            });
        _files = ["/media/movies/a.mkv", "/media/movies/b.mkv"];
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_createdCandidates, Has.Count.EqualTo(1));
            Assert.That(_createdCandidates[0].FilePath, Is.EqualTo("/media/movies/b.mkv"));
        }
    }

    [Test]
    public async Task ScanAsync_SkipsFileAndBoundsTheLinkedPathRead_WhenTheLinkedPathNeedsNormalization()
    {
        // Arrange
        var queries = new List<MediaFileQuery>();
        _mediaFiles
            .Setup(r => r.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((MediaFileQuery query, PagingParameter _, SortParameter? _, CancellationToken _) =>
                queries.Add(query))
            .ReturnsAsync(new PagedList<MediaFile>
            {
                Items = [new MediaFile { FileKind = "video", FilePath = "/media/movies/./a.mkv" }],
                TotalItems = 1
            });
        _files = ["/media/movies/a.mkv", "/media/movies/b.mkv"];
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_createdCandidates, Has.Count.EqualTo(1));
            Assert.That(_createdCandidates[0].FilePath, Is.EqualTo("/media/movies/b.mkv"));
            Assert.That(queries, Has.Count.EqualTo(1));
            Assert.That(
                queries[0].FilePathPrefix,
                Is.EqualTo(Path.GetFullPath("/media/movies") + Path.DirectorySeparatorChar));
        }
    }

    [Test]
    public async Task ScanAsync_LoadsLinkedPathsOnce_WhenMultipleFilesAreScanned()
    {
        // Arrange
        _files = ["/media/movies/a.mkv", "/media/movies/b.mkv", "/media/movies/c.mkv"];
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _mediaFiles.Verify(
            r => r.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ScanAsync_SkipsAtAcceptanceGate_WhenTheExtensionIsNotAccepted()
    {
        // Arrange
        _fileSystem
            .Setup(service => service.MatchesAnyFileTypeAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _files = ["/media/movies/sample.nfo", "/media/movies/poster.jpg", "/media/movies/feature.en.srt"];
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        Assert.That(_createdCandidates, Is.Empty);
    }

    [Test]
    public async Task ScanAsync_PreservesCandidateAndSkipsItsFile_WhenTheCandidateIsInFlight()
    {
        // Arrange
        _candidates
            .Setup(r => r.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(q =>
                    q.RootFolderId == _rootFolderId && q.Status == null && q.Statuses == null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate>
            {
                Items =
                [
                    new LibraryImportCandidate
                    {
                        RootFolderId = _rootFolderId,
                        ScanRunId = Guid.NewGuid(),
                        FilePath = "/media/movies/a.mkv",
                        ContentType = "type-a",
                        Status = LibraryImportCandidateStatus.Queued
                    }
                ]
            });
        _files = ["/media/movies/a.mkv", "/media/movies/b.mkv"];
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_createdCandidates, Has.Count.EqualTo(1));
            Assert.That(_createdCandidates[0].FilePath, Is.EqualTo("/media/movies/b.mkv"));
        }
    }

    [Test]
    public async Task ScanAsync_DedupsFile_WhenTwoOverlappingPlansEnumerateTheSamePath()
    {
        // Arrange
        _definition = _definition with
        {
            LevelDefinitions =
            [
                .. _definition.LevelDefinitions,
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-a-alt",
                    ParentSlug = null,
                    DisplayName = "type-a-alt",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = [".mkv"]
                }
            ]
        };
        _files = ["/media/movies/a.mkv"];
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        Assert.That(_createdCandidates, Has.Count.EqualTo(1));
    }

    [Test]
    public void ExecuteAsync_StopsListing_WhenCancelledMidEnumeration()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        IEnumerable<string> EnumerateAndCancelBeforeSecondFile()
        {
            yield return "/media/movies/a.mkv";
            cts.Cancel();
            yield return "/media/movies/b.mkv";
        }

        _fileSystem.Setup(f => f.EnumerateFiles(It.IsAny<string>())).Returns(EnumerateAndCancelBeforeSecondFile());
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        void Act() => _scanJob.ExecuteAsync(context, cts.Token).GetAwaiter().GetResult();

        // Assert
        Assert.Throws<OperationCanceledException>(Act);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_createdCandidates, Is.Empty);
        }
    }

    [Test]
    public async Task ScanAsync_GroupsCandidatesByFirstDirectory_WhenDepthToleranceIsProvided()
    {
        // Arrange
        _definition = _definition with
        {
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "show",
                    ParentSlug = null,
                    DisplayName = "Show",
                    CustomMetadataFields = [],
                    Subfolder = string.Empty
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "episode",
                    ParentSlug = "show",
                    DisplayName = "Episode",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = [".mkv"]
                }
            ]
        };
        _rootFolder = _rootFolder with { Path = "/media/tv" };
        _files =
        [
            "/media/tv/Show A/S01E01.mkv",
            "/media/tv/Show A/S01E02.mkv",
            "/media/tv/Show B/Season 02/S02E01.mkv",
            "/media/tv/direct.mkv"
        ];
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(b => b.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _contentTypeRegistry.Setup(registry => registry.FindProviderFor("type-a"))
            .Returns(_groupingProvider.Object);
        var scanJob = new RootFolderContentDiscoveryJob(
            _rootFolders.Object,
            _candidates.Object,
            _mediaFiles.Object,
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IContentFileService>(),
            _auditRecords.Object,
            _transactions.Object,
            _contentTypeRegistry.Object,
            _jobManager.Object,
            _libraryImport.Object,
            _fileSystem.Object,
            eventBus.Object,
            _progress.Object,
            TimeProvider.System,
            NullLogger<RootFolderContentDiscoveryJob>.Instance);
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_createdCandidates, Has.Count.EqualTo(4));

            var showAKeys = _createdCandidates
                .Where(candidate => candidate.FilePath.Contains("Show A", StringComparison.Ordinal))
                .Select(candidate => candidate.LayoutKey)
                .Distinct()
                .ToList();
            Assert.That(showAKeys, Has.Count.EqualTo(1));
            Assert.That(showAKeys[0], Does.EndWith("Show A"));

            var showBCandidate = _createdCandidates
                .Single(candidate => candidate.FilePath.Contains("Show B", StringComparison.Ordinal));
            Assert.That(showBCandidate.LayoutKey, Does.EndWith("Show B"));
            Assert.That(showBCandidate.LayoutKey, Is.Not.EqualTo(showAKeys[0]));

            var directCandidate = _createdCandidates
                .Single(candidate => candidate.FilePath.Contains("direct.mkv", StringComparison.Ordinal));
            Assert.That(directCandidate.LayoutKey, Is.Null);
        }
    }

    [Test]
    public async Task ScanAsync_MarksGroupedFileMatchingPending_WhenItsFilenameHasNoTitle()
    {
        // Arrange
        _definition = _definition with
        {
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "show",
                    ParentSlug = null,
                    DisplayName = "Show",
                    CustomMetadataFields = [],
                    Subfolder = string.Empty
                },
                new ContentHierarchyLevelDefinition
                {
                    Slug = "episode",
                    ParentSlug = "show",
                    DisplayName = "Episode",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = [".mkv"]
                }
            ]
        };
        _rootFolder = _rootFolder with { Path = "/media/tv" };
        _parse = _ => null;
        _files = ["/media/tv/Show A/01.mkv"];
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(b => b.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _contentTypeRegistry.Setup(registry => registry.FindProviderFor("type-a"))
            .Returns(_groupingProvider.Object);
        var scanJob = new RootFolderContentDiscoveryJob(
            _rootFolders.Object,
            _candidates.Object,
            _mediaFiles.Object,
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IContentFileService>(),
            _auditRecords.Object,
            _transactions.Object,
            _contentTypeRegistry.Object,
            _jobManager.Object,
            _libraryImport.Object,
            _fileSystem.Object,
            eventBus.Object,
            _progress.Object,
            TimeProvider.System,
            NullLogger<RootFolderContentDiscoveryJob>.Instance);
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString()
            },
            Event: null);

        // Act
        await scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        var row = _createdCandidates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.LayoutKey, Does.EndWith("Show A"));
            Assert.That(row.Status, Is.EqualTo(LibraryImportCandidateStatus.MatchingPending));
            Assert.That(row.Notes, Is.Null);
        }
    }

    [Test]
    public async Task ExecuteAsync_WritesAFileErroredAudit_WhenTheAuditWriteFails()
    {
        // Arrange
        _files = ["/media/movies/item.mkv"];
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The audit write failed."));
        var context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["rootFolderId"] = _rootFolderId.ToString() },
            Event: null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == "FileScanned"),
                It.IsAny<CancellationToken>()), Times.Once);
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == "FileErrored"),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_QueuesAScanOfEachRootFolder_WhenNoRootFolderIsGiven()
    {
        // Arrange
        var otherRootFolderId = Guid.NewGuid();
        _rootFolders.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                _rootFolder,
                new RootFolder { Id = otherRootFolderId, Name = "Shows", Path = "/media/shows", ContentType = "type-a" }
            ]);
        var requests = new List<JobEnqueueRequest>();
        _jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback((JobEnqueueRequest request, CancellationToken _) => requests.Add(request))
            .ReturnsAsync(Guid.NewGuid());
        var context = new JobContext(
            Guid.NewGuid(), Guid.NewGuid(), null, null, JobExecutionMode.Queue, new Dictionary<string, string>(), null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                requests.Select(request => request.Parameters!["rootFolderId"]),
                Is.EqualTo([_rootFolderId.ToString("D"), otherRootFolderId.ToString("D")]));
            Assert.That(
                requests.Select(request => (request.JobType, request.Mode)),
                Is.All.EqualTo(("RootFolderContentDiscovery", JobExecutionMode.Queue)));
            _fileSystem.Verify(f => f.EnumerateFiles(It.IsAny<string>()), Times.Never);
            _candidates.Verify(
                r => r.DeleteByRootFolderAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<IReadOnlyCollection<Guid>?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_ScansAndReplacesOnlyTheTopLevelItemFolder_WhenAnItemIsGiven()
    {
        // Arrange
        var showId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        _hierarchyItems.Setup(r => r.GetLineageAsync(episodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ContentHierarchyItem
                {
                    Id = episodeId,
                    MetadataContainerId = Guid.NewGuid(),
                    ContentType = "type-a",
                    Level = "type-a",
                    Name = "Episode",
                    Slug = "episode",
                    EffectiveRootFolderId = _rootFolderId
                },
                new ContentHierarchyItem
                {
                    Id = showId,
                    MetadataContainerId = Guid.NewGuid(),
                    ContentType = "type-a",
                    Level = "type-a",
                    Name = "Show",
                    Slug = "show",
                    EffectiveRootFolderId = _rootFolderId
                }
            ]);
        _contentFiles.Setup(s => s.GetEffectivePathAsync(showId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("/media/movies/Show");
        var inside = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/Show/inside.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending
        };
        var outside = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/Other/outside.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending
        };
        _candidates
            .Setup(r => r.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(query =>
                    query.RootFolderId == _rootFolderId && query.Status == null && query.Statuses == null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate> { Items = [inside, outside], TotalItems = 2 });
        var context = new JobContext(
            Guid.NewGuid(), null, null, _userId, JobExecutionMode.Queue,
            new Dictionary<string, string>
            {
                ["rootFolderId"] = _rootFolderId.ToString("D"),
                ["contentHierarchyItemId"] = episodeId.ToString("D")
            },
            null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _fileSystem.Verify(f => f.EnumerateFiles(Path.GetFullPath("/media/movies/Show")), Times.Once);
            _fileSystem.Verify(f => f.EnumerateFiles("/media/movies"), Times.Never);
            _candidates.Verify(
                r => r.DeleteByRootFolderAsync(
                    _rootFolderId,
                    It.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.SequenceEqual(new[] { inside.Id })),
                    _userId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsARejectionAndDropsAVanishedOne_WhenTheFolderIsRescanned()
    {
        // Arrange
        var rejected = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/rejected.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Rejected
        };
        var vanished = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/vanished.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Rejected
        };
        _candidates
            .Setup(r => r.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(query =>
                    query.RootFolderId == _rootFolderId && query.Status == null && query.Statuses == null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate> { Items = [rejected, vanished], TotalItems = 2 });
        _files = ["/media/movies/rejected.mkv", "/media/movies/new.mkv"];
        var context = new JobContext(
            Guid.NewGuid(), null, null, _userId, JobExecutionMode.Queue,
            new Dictionary<string, string> { ["rootFolderId"] = _rootFolderId.ToString("D") }, null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _createdCandidates.Select(candidate => candidate.FilePath),
                Is.EqualTo(["/media/movies/new.mkv"]));
            _candidates.Verify(r => r.DeleteAsync(vanished.Id, _userId, It.IsAny<CancellationToken>()), Times.Once);
            _candidates.Verify(r => r.DeleteAsync(rejected.Id, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public void DefaultSchedule_QueuesADailyScanOfEveryRootFolder_WhenSeeded()
    {
        // Act
        var schedule = DefaultJobTriggerCatalog.ScheduleTriggers.Single(trigger =>
            trigger.JobType == MagpieJobs.Types.RootFolderContentDiscovery);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(schedule.Schedule, Is.EqualTo("30 4 * * *"));
            Assert.That(schedule.Enabled, Is.True);
            Assert.That(schedule.ExecutionMode, Is.EqualTo(JobExecutionMode.Queue));
            Assert.That(schedule.Scope, Is.EqualTo(JobScope.Content));
            Assert.That(schedule.Parameters, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsAnUnattachedMatchAndReplacesTheRest_WhenTheFullRootFolderIsRescanned()
    {
        // Arrange
        var matched = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = _rootFolderId,
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/matched.mkv",
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.Pending,
            Matches =
            [
                new LibraryImportMatch
                {
                    Confidence = 1,
                    ProviderType = "provider-a",
                    ExternalIdKey = "provider-a",
                    ExternalId = "27205",
                    Title = "Inception"
                }
            ]
        };
        var unmatched = matched with { Id = Guid.NewGuid(), FilePath = "/media/movies/unmatched.mkv", Matches = [] };
        var removed = matched with { Id = Guid.NewGuid(), FilePath = "/media/movies/removed.mkv" };
        var attached = matched with { Id = Guid.NewGuid(), FilePath = "/media/movies/attached.mkv" };
        _mediaFiles
            .Setup(r => r.QueryAsync(
                It.IsAny<MediaFileQuery>(), It.IsAny<PagingParameter>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>
            {
                Items =
                [
                    new MediaFile
                    {
                        Id = Guid.NewGuid(),
                        ContentItemId = Guid.NewGuid(),
                        FileKind = "video",
                        FilePath = "/media/movies/attached.mkv"
                    }
                ],
                TotalItems = 1
            });
        _candidates
            .Setup(r => r.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(query =>
                    query.RootFolderId == _rootFolderId && query.Status == null && query.Statuses == null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate>
            {
                Items = [matched, unmatched, removed, attached],
                TotalItems = 4
            });
        _fileSystem.Setup(f => f.FileExists("/media/movies/matched.mkv")).Returns(true);
        _fileSystem.Setup(f => f.FileExists("/media/movies/unmatched.mkv")).Returns(true);
        _fileSystem.Setup(f => f.FileExists("/media/movies/attached.mkv")).Returns(true);
        _files = ["/media/movies/matched.mkv", "/media/movies/unmatched.mkv"];
        var context = new JobContext(
            Guid.NewGuid(), null, null, _userId, JobExecutionMode.Queue,
            new Dictionary<string, string> { ["rootFolderId"] = _rootFolderId.ToString("D") }, null);

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _createdCandidates.Select(candidate => candidate.FilePath),
                Is.EqualTo(["/media/movies/unmatched.mkv"]));
            _candidates.Verify(
                r => r.DeleteByRootFolderAsync(
                    _rootFolderId,
                    It.Is<IReadOnlyCollection<Guid>?>(ids =>
                        ids != null && ids.Count == 3 && ids.Contains(unmatched.Id) && ids.Contains(removed.Id) &&
                        ids.Contains(attached.Id)),
                    _userId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
