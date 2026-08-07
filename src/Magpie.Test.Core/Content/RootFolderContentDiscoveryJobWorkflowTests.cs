using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.MetadataSearch;
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
    private Guid _userId;
    private Guid _rootFolderId;
    private RootFolder _rootFolder = null!;
    private List<string> _files = null!;
    private List<LibraryImportCandidate> _createdCandidates = null!;
    private Func<string, string, IReadOnlyList<MetadataSearchCandidate>> _searchResponse = null!;
    private Func<string, IReadOnlyDictionary<string, IReadOnlyList<string>>?> _parse = null!;
    private string? _levelSlug;
    private ContentTypeDefinition _definition = null!;
    private Mock<IFileSystem> _fileSystem = null!;
    private Mock<IMetadataSearchService> _metadataService = null!;
    private Mock<IRootFolderRepository> _rootFolders = null!;
    private Mock<ILibraryImportCandidateRepository> _candidates = null!;
    private Mock<IMediaFileRepository> _mediaFiles = null!;
    private Mock<IProgressReporter> _progress = null!;
    private IJob _scanJob = null!;

    [SetUp]
    public void SetUp()
    {
        _userId = Guid.NewGuid();

        _files = [];
        _fileSystem = new Mock<IFileSystem>();
        _fileSystem.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);
        _fileSystem.Setup(f => f.EnumerateFiles(It.IsAny<string>())).Returns(() => _files);
        _fileSystem.Setup(f => f.PathsEqual(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string left, string right) => Path.GetRelativePath(left, right) == ".");

        _searchResponse = (_, _) => [];
        _metadataService = new Mock<IMetadataSearchService>();
        _metadataService
            .Setup(s => s.SearchAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string query, string contentType, int _, CancellationToken cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(_searchResponse(query, contentType));
            });

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

        _rootFolderId = Guid.NewGuid();
        _rootFolder = new RootFolder
        {
            Id = _rootFolderId,
            Name = "Movies",
            Path = "/media/movies",
            ContentType = "type-a",
            CreatedAt = DateTime.UtcNow
        };
        _rootFolders = new Mock<IRootFolderRepository>();
        _rootFolders
            .Setup(r => r.GetByIdAsync(_rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _rootFolder);

        _createdCandidates = [];
        _candidates = new Mock<ILibraryImportCandidateRepository>();
        _candidates
            .Setup(r => r.CreateAsync(
                It.IsAny<LibraryImportCandidate>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((LibraryImportCandidate model, Guid? _, CancellationToken _) => _createdCandidates.Add(model))
            .ReturnsAsync((LibraryImportCandidate model, Guid? _, CancellationToken _) => model);
        _candidates
            .Setup(r => r.DeleteByRootFolderAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _candidates
            .Setup(r => r.QueryAsync(
                It.IsAny<LibraryImportCandidateQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<LibraryImportCandidate> { Items = [] });

        _mediaFiles = new Mock<IMediaFileRepository>();
        _mediaFiles
            .Setup(r => r.QueryAsync(
                It.IsAny<MediaFileQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile>());

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

        _progress = new Mock<IProgressReporter>();
        _scanJob = new RootFolderContentDiscoveryJob(
            _rootFolders.Object,
            _candidates.Object,
            _mediaFiles.Object,
            Mock.Of<IAuditRecordRepository>(),
            [definitionProvider.Object],
            _metadataService.Object,
            _fileSystem.Object,
            eventBus.Object,
            _progress.Object,
            TimeProvider.System,
            NullLogger<RootFolderContentDiscoveryJob>.Instance);
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
        Assert.That(ex.Message, Does.Contain("Root folder"));
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
        Assert.That(_createdCandidates, Is.Empty);
        _candidates.Verify(
            r => r.DeleteByRootFolderAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _progress.Verify(
            value => value.Report(1, "The scan stopped because the content type has no definition provider."),
            Times.Once);
    }

    [Test]
    public async Task ScanAsync_ProducesPendingRows_ForEachFile()
    {
        // Arrange
        _searchResponse = (query, _) =>
        [
            new MetadataSearchCandidate
            {
                ProviderType = "provider-a",
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "100",
                ContentType = "type-a",
                Title = "Title for " + query
            }
        ];
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
                Has.All.Property(nameof(LibraryImportCandidate.Status)).EqualTo(LibraryImportCandidateStatus.Pending));
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

    [TestCase(
        "/media/movies/Inception.2010.mkv",
        null,
        null,
        "Inception|27205|",
        "27205",
        TestName = "ScanAsync_SingleResult_WritesMatchedRow_WithOneCandidate")]
    [TestCase(
        "/media/movies/Matrix.1999.mkv",
        "Matrix",
        1999,
        "The Matrix|603|1999",
        "603",
        TestName = "ScanAsync_ProviderTitleWithArticle_MatchesTitleSignalWithoutArticle")]
    [TestCase(
        "/media/movies/spiderman.2002.mkv",
        "spiderman",
        2002,
        "Spider-Man|557|2002",
        "557",
        TestName = "ScanAsync_PunctuationVariant_MatchesCompactTitle")]
    [TestCase(
        "/media/movies/The.Matrix.2003.mkv",
        "The Matrix",
        2003,
        "The Matrix|603|1999",
        "603",
        TestName = "ScanAsync_YearMismatch_StillMatchesStrongTitle")]
    [TestCase(
        "/media/movies/The.Matrix.1999.mkv",
        "The Matrix",
        1999,
        "The Matrix|wrong-year|2003;The Matrix|603|1999",
        "603",
        TestName = "ScanAsync_YearMatch_WinsOverSameTitleYearMismatch")]
    [TestCase(
        "/media/movies/a.mkv",
        null,
        null,
        "",
        null,
        TestName = "ScanAsync_ZeroResults_QuarantinesRow_WithReason")]
    [TestCase(
        "/media/movies/Inception.2010.mkv",
        null,
        null,
        "A Totally Different Film|999|",
        null,
        TestName = "ScanAsync_LowConfidenceResult_QuarantinesRow_WithReason")]
    [TestCase(
        "/media/movies/Solaris.1972.mkv",
        null,
        null,
        "Solaris|1018|;Solaris|8413|",
        null,
        TestName = "ScanAsync_AmbiguousResults_QuarantinesRow_WithReason")]
    public async Task ScanAsync_SingleFile_ResolvesOrQuarantines(
        string fileName,
        string? parseTitle,
        int? parseYear,
        string resultsSpec,
        string? expectedExternalId)
    {
        // Arrange
        var results = resultsSpec.Length == 0
            ? []
            : resultsSpec
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(item =>
                {
                    var parts = item.Split('|');
                    return (
                        Title: parts[0],
                        ExternalId: parts[1],
                        Year: parts[2].Length == 0
                            ? (int?)null
                            : int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
                })
                .ToArray();

        if (parseTitle is { } title)
        {
            _parse = _ => new SignalBagBuilder()
                    .AddValue(ParsedFileSignalKeys.Title, title)
                    .AddValue(ParsedFileSignalKeys.Year, parseYear)
                    .AddValue(ParsedFileSignalKeys.TitleToken, title)
                    .Build();
        }

        if (results.Length > 0)
        {
            _searchResponse = (_, _) => [.. results
                .Select(r => new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = r.ExternalId,
                    ContentType = "type-a",
                    Title = r.Title,
                    ReleaseDate = r.Year is { } year
                        ? new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                        : null
                })];
        }

        _files = [fileName];
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
            if (expectedExternalId is { } expected)
            {
                Assert.That(row.Matches, Has.Count.EqualTo(1));
                Assert.That(row.Matches[0].ExternalId, Is.EqualTo(expected));
            }
            else
            {
                Assert.That(row.Matches, Is.Empty);
                Assert.That(row.Notes, Is.Not.Null.And.Not.Empty);
            }
        }
    }

    [Test]
    public async Task ScanAsync_CreatesCandidateWithoutMediaFile_WhenMatchIsConfident()
    {
        // Arrange
        _searchResponse = (_, _) =>
        [
            new MetadataSearchCandidate
            {
                ProviderType = "provider-a",
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "27205",
                ContentType = "type-a",
                Title = "Inception"
            }
        ];
        _files = ["/media/movies/Inception.2010.mkv"];
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
        _mediaFiles.Verify(
            r => r.CreateAsync(
                It.IsAny<MediaFile>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ScanAsync_WipesAndRebuilds_TheRootFolderPendingSet()
    {
        // Arrange
        _searchResponse = (_, _) =>
        [
            new MetadataSearchCandidate
            {
                ProviderType = "provider-a",
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "100",
                ContentType = "type-a",
                Title = "A"
            }
        ];
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
        _searchResponse = (_, _) => [];

        // Act
        await _scanJob.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        _candidates.Verify(
            r => r.DeleteByRootFolderAsync(_rootFolderId, _userId, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_createdCandidates, Has.Count.EqualTo(2));
            Assert.That(_createdCandidates[0].Matches, Has.Count.EqualTo(1));
            Assert.That(_createdCandidates[1].Matches, Is.Empty);
        }
    }

    [Test]
    public async Task ScanAsync_SkipsFile_WhenAlreadyRepresentedInMediaFile()
    {
        // Arrange
        _mediaFiles
            .Setup(r => r.QueryAsync(
                It.Is<MediaFileQuery>(query => query.FilePaths!.Contains("/media/movies/a.mkv")),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MediaFile> { TotalItems = 1 });
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
    public async Task ScanAsync_SkipsAtAcceptanceGate_ForNonAcceptedExtensions()
    {
        // Arrange
        _fileSystem
            .Setup(service => service.MatchesAnyFileTypeAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _searchResponse = (_, _) =>
            throw new InvalidOperationException("Metadata search must not be called for non-accepted files.");
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
        _metadataService.Verify(
            s => s.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ScanAsync_QuarantinesFile_WhenParseReturnsNull()
    {
        // Arrange
        _parse = _ => null;
        _searchResponse = (_, _) =>
            throw new InvalidOperationException("Metadata search must not be called when Parse returns null.");
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
        _metadataService.Verify(
            s => s.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Status, Is.EqualTo(LibraryImportCandidateStatus.Pending));
            Assert.That(row.Matches, Is.Empty);
            Assert.That(row.ParsedSignals, Is.Empty);
            Assert.That(row.Notes, Is.Not.Null.And.Not.Empty);
        }
    }

    [Test]
    public async Task ScanAsync_PassesParsedSignals_WhenSearchingMetadata()
    {
        // Arrange
        _parse = _ => new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, "Fight Club")
                .AddValue(ParsedFileSignalKeys.Year, 1999)
                .AddValue(ParsedFileSignalKeys.TitleToken, "Fight Club 1999")
                .Build();
        _searchResponse = (_, _) =>
        [
            new MetadataSearchCandidate
            {
                ProviderType = "provider-a",
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "550",
                ContentType = "type-a",
                Title = "Fight Club"
            }
        ];
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
        _metadataService.Verify(
            s => s.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _metadataService.Verify(
            s => s.SearchAsync("Fight Club 1999", "type-a", It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ScanAsync_PersistsParsedSignals_FromProviderBag()
    {
        // Arrange
        _parse = _ => new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, "The Show")
                .AddValue(ParsedFileSignalKeys.Year, 2020)
                .AddValue("season", 2)
                .AddValue("episode", 5)
                .AddValue(ParsedFileSignalKeys.TitleToken, "The Show 2020")
                .Build();
        _searchResponse = (_, _) =>
        [
            new MetadataSearchCandidate
            {
                ProviderType = "provider-a",
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "2000",
                ContentType = "type-a",
                Title = "The Show"
            }
        ];
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
            Assert.That(row.ParsedSignals[ParsedFileSignalKeys.Title].Single(), Is.EqualTo("The Show"));
            Assert.That(row.ParsedSignals[ParsedFileSignalKeys.Year].Single(), Is.EqualTo("2020"));
            Assert.That(row.ParsedSignals["season"].Single(), Is.EqualTo("2"));
            Assert.That(row.ParsedSignals["episode"].Single(), Is.EqualTo("5"));
            Assert.That(row.ParsedSignals[ParsedFileSignalKeys.TitleToken].Single(), Is.EqualTo("The Show 2020"));
        }
    }

    [Test]
    public async Task ScanAsync_RetriesWithoutYear_WhenTitleWithYearFindsNothing()
    {
        // Arrange
        _parse = _ => new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, "The Matrix")
                .AddValue(ParsedFileSignalKeys.Year, 1999)
                .AddValue(ParsedFileSignalKeys.TitleToken, "The Matrix 1999")
                .Build();
        _searchResponse = (query, _) => query == "The Matrix"
            ?
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "603",
                    ContentType = "type-a",
                    Title = "The Matrix",
                    ReleaseDate = new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            ]
            : [];
        _files = ["/media/movies/The.Matrix.1999.mkv"];
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
        _metadataService.Verify(
            s => s.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _metadataService.Verify(
            s => s.SearchAsync("The Matrix", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Matches, Has.Count.EqualTo(1));
            Assert.That(row.Matches[0].ExternalId, Is.EqualTo("603"));
        }
    }

    [Test]
    public async Task ScanAsync_QuarantinesWithOriginalTitle_WhenRetryAlsoFindsNothing()
    {
        // Arrange
        _parse = _ => new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, "Nonexistent")
                .AddValue(ParsedFileSignalKeys.Year, 2099)
                .AddValue(ParsedFileSignalKeys.TitleToken, "Nonexistent 2099")
                .Build();
        _files = ["/media/movies/Nonexistent.2099.mkv"];
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
        _metadataService.Verify(
            s => s.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Matches, Is.Empty);
            Assert.That(row.Notes, Does.Contain("Nonexistent 2099"));
        }
    }

    [Test]
    public async Task ScanAsync_PreservesInFlightCandidate_AndSkipsRecreatingItsFile()
    {
        // Arrange
        _candidates
            .Setup(r => r.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(q =>
                    q.Statuses != null &&
                    q.Statuses.Contains(LibraryImportCandidateStatus.Queued) &&
                    q.Statuses.Contains(LibraryImportCandidateStatus.Importing)),
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
}
