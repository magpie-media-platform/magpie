using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Import;
using Magpie.Models.Jobs;
using Magpie.Models.MetadataSearch;
using Magpie.Models.Paging;
using Magpie.Models.Signals;
using Magpie.Sdk.Content;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class LibraryImportMatchResolutionJobTests
{
    private Mock<ITransactionManager> _transactions = null!;
    private Mock<IAuditRecordRepository> _auditRecords = null!;

    private Guid _userId;
    private LibraryImportCandidate _candidate = null!;
    private JobContext _jobContext = null!;
    private List<LibraryImportCandidate> _pendingBatch = null!;
    private int _remainingAfterBatch;
    private List<LibraryImportCandidate> _updatedCandidates = null!;
    private List<Guid> _deletedCandidateIds = null!;
    private Func<string, string, IReadOnlyList<MetadataSearchCandidate>> _searchResponse = null!;
    private IReadOnlyList<string> _searchWarnings = null!;
    private Func<string, IReadOnlyDictionary<string, IReadOnlyList<string>>?> _parse = null!;
    private string? _levelSlug;
    private ContentTypeDefinition _definition = null!;
    private Mock<IMetadataSearchService> _metadataService = null!;
    private Mock<ILibraryImportCandidateRepository> _candidates = null!;
    private Mock<IJobManager> _jobManager = null!;
    private FakeTimeProvider _timeProvider = null!;
    private RootFolder _rootFolder = null!;
    private Guid? _libraryTargetId;
    private Mock<ILibraryImportService> _libraryImport = null!;
    private IJob _resolutionJob = null!;

    public static IEnumerable<TestCaseData> MatchCases()
    {
        yield return new TestCaseData(
            "/media/movies/Inception.2010.mkv",
            "Inception.2010",
            null,
            new MetadataSearchCandidate[]
            {
                new() {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "27205",
                    ContentType = "type-a",
                    Title = "Inception",
                    ReleaseDate = null
                }
            },
            new[] { "27205" },
            null);
        yield return new TestCaseData(
            "/media/movies/Matrix.1999.mkv",
            "Matrix",
            1999,
            new MetadataSearchCandidate[]
            {
                new() {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "603",
                    ContentType = "type-a",
                    Title = "The Matrix",
                    ReleaseDate = new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            },
            new[] { "603" },
            null);
        yield return new TestCaseData(
            "/media/movies/spiderman.2002.mkv",
            "spiderman",
            2002,
            new MetadataSearchCandidate[]
            {
                new() {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "557",
                    ContentType = "type-a",
                    Title = "Spider-Man",
                    ReleaseDate = new DateTime(2002, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            },
            new[] { "557" },
            null);
        yield return new TestCaseData(
            "/media/movies/The.Matrix.2003.mkv",
            "The Matrix",
            2003,
            new MetadataSearchCandidate[]
            {
                new() {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "603",
                    ContentType = "type-a",
                    Title = "The Matrix",
                    ReleaseDate = new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            },
            new[] { "603" },
            null);
        yield return new TestCaseData(
            "/media/movies/The.Matrix.1999.mkv",
            "The Matrix",
            1999,
            new MetadataSearchCandidate[]
            {
                new() {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "wrong-year",
                    ContentType = "type-a",
                    Title = "The Matrix",
                    ReleaseDate = new DateTime(2003, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new() {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "603",
                    ContentType = "type-a",
                    Title = "The Matrix",
                    ReleaseDate = new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            },
            new[] { "603" },
            null);
        yield return new TestCaseData(
            "/media/movies/a.mkv",
            "a",
            null,
            Array.Empty<MetadataSearchCandidate>(),
            Array.Empty<string>(),
            "No metadata result matched a.");
        yield return new TestCaseData(
            "/media/movies/Inception.2010.mkv",
            "Inception.2010",
            null,
            new MetadataSearchCandidate[]
            {
                new() {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "999",
                    ContentType = "type-a",
                    Title = "A Totally Different Film",
                    ReleaseDate = null
                }
            },
            Array.Empty<string>(),
            "The closest result, A Totally Different Film, scored below the match threshold.");
        yield return new TestCaseData(
            "/media/movies/Solaris.1972.mkv",
            "Solaris.1972",
            null,
            new MetadataSearchCandidate[]
            {
                new() {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "1018",
                    ContentType = "type-a",
                    Title = "Solaris",
                    ReleaseDate = new DateTime(1972, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new() {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "8413",
                    ContentType = "type-a",
                    Title = "Solaris",
                    ReleaseDate = new DateTime(2002, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            },
            Array.Empty<string>(),
            "2 results have similar scores. Select a result manually.");
        yield return new TestCaseData(
            "/media/movies/Star.Wars.mkv",
            "Star Wars",
            null,
            new MetadataSearchCandidate[]
            {
                new() {
                    ProviderType = "provider-a", ExternalIdSource = "provider-a.type-a", ExternalId = "11",
                    ContentType = "type-a", Title = "Star Wars IX"
                },
                new() {
                    ProviderType = "provider-a", ExternalIdSource = "provider-a.type-a", ExternalId = "12",
                    ContentType = "type-a", Title = "Star Wars IV"
                }
            },
            Array.Empty<string>(),
            "2 results have similar scores. Select a result manually.");
        yield return new TestCaseData(
            "/media/movies/Inception.2010.mkv",
            "Inception.2010",
            null,
            new MetadataSearchCandidate[]
            {
                new() {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "999",
                    ContentType = "type-a",
                    Title = "",
                    ReleaseDate = null
                }
            },
            Array.Empty<string>(),
            "The closest result, , scored below the match threshold.");
        yield return new TestCaseData(
            "/media/movies/Us.2019.mkv",
            "Us",
            2019,
            new MetadataSearchCandidate[]
            {
                new() {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "999",
                    ContentType = "type-a",
                    Title = "It",
                    ReleaseDate = null
                }
            },
            Array.Empty<string>(),
            "The closest result, It, scored below the match threshold.");
        yield return new TestCaseData(
            "/media/movies/Us.2019.mkv",
            "Us",
            2019,
            new MetadataSearchCandidate[]
            {
                new() {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "458723",
                    ContentType = "type-a",
                    Title = "Us (2019)",
                    ReleaseDate = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            },
            new[] { "458723" },
            null);
        yield return new TestCaseData(
            "/media/movies/Indiana.Jones.and.the.Temple.of.Doom.1984.mkv",
            "Indiana Jones and the Temple of Doom",
            1984,
            new MetadataSearchCandidate[]
            {
                new() {
                    ProviderType = "tmdb", ExternalIdSource = "tmdb.movie", ExternalId = "649923",
                    ContentType = "type-a", Title = "The Making of Indiana Jones and the Temple of Doom",
                    Year = 1984
                },
                new() {
                    ProviderType = "tmdb", ExternalIdSource = "tmdb.movie", ExternalId = "87",
                    ContentType = "type-a", Title = "Indiana Jones and the Temple of Doom", Year = 1984
                }
            },
            new[] { "87" },
            null);
        yield return new TestCaseData(
            "/media/movies/Indiana.Jones.and.the.Dial.of.Destiny.2023.mkv",
            "Indiana Jones and the Dial of Destiny",
            2023,
            new MetadataSearchCandidate[]
            {
                new() {
                    ProviderType = "tmdb", ExternalIdSource = "tmdb.movie", ExternalId = "1168918",
                    ContentType = "type-a", Title = "The Making of Indiana Jones and the Dial of Destiny",
                    Year = 2023
                },
                new() {
                    ProviderType = "tmdb", ExternalIdSource = "tmdb.movie", ExternalId = "335977",
                    ContentType = "type-a", Title = "Indiana Jones and the Dial of Destiny", Year = 2023
                }
            },
            new[] { "335977" },
            null);
        yield return new TestCaseData(
            "/media/movies/Indiana.Jones.and.the.Temple.of.Doom.mkv",
            "Indiana Jones and the Temple of Doom",
            null,
            new MetadataSearchCandidate[]
            {
                new() {
                    ProviderType = "tmdb", ExternalIdSource = "tmdb.movie", ExternalId = "649923",
                    ContentType = "type-a", Title = "The Making of Indiana Jones and the Temple of Doom"
                },
                new() {
                    ProviderType = "tmdb", ExternalIdSource = "tmdb.movie", ExternalId = "87",
                    ContentType = "type-a", Title = "Indiana Jones & the Temple of Doom"
                }
            },
            new[] { "87" },
            null);
    }

    [SetUp]
    public void SetUp()
    {
        _auditRecords = new Mock<IAuditRecordRepository>();
        _transactions = new Mock<ITransactionManager>();
        _transactions.Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken token) => operation(token));
        _transactions.Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken token) => operation(token));
        _transactions.Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> operation, CancellationToken token) => operation(token));

        _timeProvider = new FakeTimeProvider(
            new DateTimeOffset(new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc), TimeSpan.Zero));
        _userId = Guid.NewGuid();
        _candidate = new LibraryImportCandidate
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = string.Empty,
            ContentType = "type-a",
            Status = LibraryImportCandidateStatus.MatchingPending
        };
        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: _userId,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);
        _pendingBatch = [];
        _remainingAfterBatch = 0;
        _updatedCandidates = [];
        _deletedCandidateIds = [];

        _searchResponse = (_, _) => [];
        _searchWarnings = [];
        _metadataService = new Mock<IMetadataSearchService>();
        _metadataService
            .Setup(s => s.SearchAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((string query, string contentType, int _, CancellationToken cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new MetadataSearchPage
                {
                    Supported = true,
                    Matches = _searchResponse(query, contentType),
                    PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false },
                    Warnings = _searchWarnings
                });
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

        _candidates = new Mock<ILibraryImportCandidateRepository>();
        _candidates
            .Setup(r => r.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(q => q.Status == LibraryImportCandidateStatus.MatchingPending),
                It.Is<PagingParameter>(p => p.PageSize > 1),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new PagedList<LibraryImportCandidate>
            {
                Items = _pendingBatch,
                TotalItems = _pendingBatch.Count
            });
        _candidates
            .Setup(r => r.QueryAsync(
                It.Is<LibraryImportCandidateQuery>(q => q.Status == LibraryImportCandidateStatus.MatchingPending),
                It.Is<PagingParameter>(p => p.PageSize == 1),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new PagedList<LibraryImportCandidate> { TotalItems = _remainingAfterBatch });
        _candidates
            .Setup(r => r.TryResolveMatchAsync(
                It.IsAny<LibraryImportCandidate>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((LibraryImportCandidate model, Guid? _, CancellationToken _) => _updatedCandidates.Add(model))
            .ReturnsAsync(true);
        _candidates
            .Setup(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((Guid id, Guid? _, CancellationToken _) => _deletedCandidateIds.Add(id))
            .ReturnsAsync(true);

        _jobManager = new Mock<IJobManager>();
        _jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var contentTypes = new Mock<IContentTypeRegistry>();
        contentTypes.Setup(registry => registry.FindProviderFor("type-a")).Returns(definitionProvider.Object);

        _rootFolder = new RootFolder
        {
            Id = _candidate.RootFolderId,
            Name = "Movies",
            Path = "/media/movies",
            ContentType = "type-a"
        };
        var rootFolders = new Mock<IRootFolderRepository>();
        rootFolders.Setup(r => r.GetByIdAsync(_candidate.RootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _rootFolder);
        _libraryTargetId = null;
        _libraryImport = new Mock<ILibraryImportService>();
        _libraryImport
            .Setup(s => s.ResolveLibraryTargetAsync(It.IsAny<LibraryImportCandidate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _libraryTargetId);
        _libraryImport
            .Setup(s => s.AssociateMissingFilesAsync(
                It.IsAny<IReadOnlyCollection<LibraryImportCandidate>>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryImportSubmissionResult { Queued = [], Skipped = [] });

        _resolutionJob = new LibraryImportMatchResolutionJob(
            _candidates.Object,
            rootFolders.Object,
            _libraryImport.Object,
            _auditRecords.Object,
            _transactions.Object,
            contentTypes.Object,
            _metadataService.Object,
            _jobManager.Object,
            _timeProvider,
            NullLogger<LibraryImportMatchResolutionJob>.Instance,
            Mock.Of<IProgressReporter>());
    }

    [Test]
    public async Task ExecuteAsync_DoesNothing_WhenNoCandidateIsUnresolved()
    {
        // Arrange

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_updatedCandidates, Is.Empty);
            _jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_RequeuesItself_WhenTheBatchIsEmptyButCandidatesRemain()
    {
        // Arrange
        _remainingAfterBatch = 1;

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request => request.NotBeforeAt == null && request.IsSuccessor),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCaseSource(nameof(MatchCases))]
    public async Task ExecuteAsync_MatchesOrQuarantinesCandidate_WhenSearchReturnsCandidates(
        string filePath,
        string parsedTitle,
        int? parsedYear,
        MetadataSearchCandidate[] searchResults,
        string[] expectedExternalIds,
        string? expectedNotes)
    {
        // Arrange
        _parse = _ => new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, parsedTitle)
                .AddValue(ParsedFileSignalKeys.Year, parsedYear)
                .AddValue(ParsedFileSignalKeys.TitleToken, parsedTitle)
                .Build();
        _searchResponse = (_, _) => searchResults;
        _candidate.FilePath = filePath;
        _pendingBatch = [_candidate];

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var row = _updatedCandidates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Status, Is.EqualTo(LibraryImportCandidateStatus.Pending));
            Assert.That(row.Matches.Select(match => match.ExternalId), Is.EqualTo(expectedExternalIds));
            Assert.That(row.Notes, Is.EqualTo(expectedNotes));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotRequeueItself_WhenNoCandidateLeavesThePendingState()
    {
        // Arrange
        _parse = _ => throw new InvalidOperationException("The parse failed.");
        _candidates
            .Setup(r => r.MarkMatchingFailedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The failure status could not be saved."));
        _candidate.FilePath = "/media/movies/Inception.2010.mkv";
        _pendingBatch = [_candidate];
        _remainingAfterBatch = 1;

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_RetriesWithoutYear_WhenTitleWithYearFindsNothing()
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
        _candidate.FilePath = "/media/movies/The.Matrix.1999.mkv";
        _pendingBatch = [_candidate];

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var row = _updatedCandidates.Single();
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
    public async Task ExecuteAsync_QuarantinesWithOriginalTitle_WhenRetryAlsoFindsNothing()
    {
        // Arrange
        _parse = _ => new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, "Nonexistent")
                .AddValue(ParsedFileSignalKeys.Year, 2099)
                .AddValue(ParsedFileSignalKeys.TitleToken, "Nonexistent 2099")
                .Build();
        _candidate.FilePath = "/media/movies/Nonexistent.2099.mkv";
        _pendingBatch = [_candidate];

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var row = _updatedCandidates.Single();
        _metadataService.Verify(
            s => s.SearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Matches, Is.Empty);
            Assert.That(row.Notes, Does.Contain("Nonexistent 2099"));
        }
    }

    [TestCase(null)]
    [TestCase("/media/Example")]
    public async Task ExecuteAsync_RetriesPendingCandidates_WhenAProviderCouldNotAnswerAndNothingMatched(
        string? layoutKey)
    {
        // Arrange
        _searchWarnings = ["provider-a could not finish the search. Try again."];
        _candidate.FilePath = "/media/Example/Example.mkv";
        _candidate.LayoutKey = layoutKey;
        _pendingBatch = [_candidate];
        _remainingAfterBatch = 1;

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _candidates.Verify(
                r => r.MarkMatchingFailedAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.That(_candidate.Status, Is.EqualTo(LibraryImportCandidateStatus.MatchingPending));
            Assert.That(_updatedCandidates, Is.EqualTo([_candidate]));
            Assert.That(_candidate.Notes, Is.EqualTo(
                "Matching will retry because the metadata search did not complete: " +
                "provider-a could not finish the search. Try again."));
            _jobManager.Verify(manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request => request.IsSuccessor &&
                    request.NotBeforeAt == new DateTime(2026, 5, 20, 12, 1, 0, DateTimeKind.Utc)),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsTheNotes_WhenTheSameReasonRepeats()
    {
        // Arrange
        _searchWarnings = ["provider-a could not finish the search. Try again."];
        _candidate.FilePath = "/media/Example/Example.mkv";
        _candidate.Notes = "Matching will retry because the metadata search did not complete: " +
            "provider-a could not finish the search. Try again.";
        _pendingBatch = [_candidate];
        _remainingAfterBatch = 1;

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That(_updatedCandidates, Is.Empty);
    }

    [TestCase(null)]
    [TestCase("/media/Example")]
    public async Task ExecuteAsync_LeavesAManualMatchAlone_WhenTheUserMatchedTheFileDuringTheRetry(string? layoutKey)
    {
        // Arrange
        _candidate.FilePath = "/media/Example/Example.mkv";
        _candidate.LayoutKey = layoutKey;
        _pendingBatch = [_candidate];
        _remainingAfterBatch = 0;
        _searchResponse = (_, _) => [new MetadataSearchCandidate
        {
            ProviderType = "provider-a", ContentType = "type-a", Level = "type-a",
            ExternalIdSource = "provider-a", ExternalId = "example", Title = "Example"
        }];
        _candidates
            .Setup(r => r.TryResolveMatchAsync(
                It.IsAny<LibraryImportCandidate>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == "FileMatched"),
                It.IsAny<CancellationToken>()), Times.Never);
            _jobManager.Verify(manager => manager.ExecuteAsync(
                It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [TestCase(null)]
    [TestCase("/media/Example")]
    public async Task ExecuteAsync_FailsTheCandidate_WhenTheMetadataSearchItselfThrows(string? layoutKey)
    {
        // Arrange
        _searchResponse = (_, _) => throw new InvalidOperationException("The search service failed.");
        _candidates
            .Setup(r => r.MarkMatchingFailedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _candidate.FilePath = "/media/Example/Example.mkv";
        _candidate.LayoutKey = layoutKey;
        _pendingBatch = [_candidate];
        _remainingAfterBatch = 1;

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _candidates.Verify(
                r => r.MarkMatchingFailedAsync(
                    _candidate.Id,
                    "Match resolution failed: The search service failed.",
                    _userId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(_updatedCandidates, Is.Empty);
            _jobManager.Verify(manager => manager.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request => request.IsSuccessor && request.NotBeforeAt == null),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [TestCase(null)]
    [TestCase("/media/Example")]
    public async Task ExecuteAsync_ResolvesPendingCandidates_WhenTheProviderRecovers(string? layoutKey)
    {
        // Arrange
        _candidate.FilePath = "/media/Example/Example.mkv";
        _candidate.LayoutKey = layoutKey;
        _pendingBatch = [_candidate];
        _remainingAfterBatch = 1;
        _searchWarnings = ["The provider is unavailable."];
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);
        _searchWarnings = [];
        _searchResponse = (_, _) => [new MetadataSearchCandidate
        {
            ProviderType = "provider-a", ContentType = "type-a", Level = "type-a",
            ExternalIdSource = "provider-a", ExternalId = "example", Title = "Example"
        }];
        _remainingAfterBatch = 0;

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_updatedCandidates, Has.Count.EqualTo(2));
            Assert.That(_candidate.Status, Is.EqualTo(LibraryImportCandidateStatus.Pending));
            Assert.That(_candidate.Matches.Single().ExternalId, Is.EqualTo("example"));
            Assert.That(_candidate.Notes, Is.Null);
            _candidates.Verify(repository => repository.MarkMatchingFailedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_MemoizesDuplicateQueries_WhenQueriesRepeatInOneRun()
    {
        // Arrange
        _parse = _ => new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, "Fight Club")
                .AddValue(ParsedFileSignalKeys.TitleToken, "Fight Club")
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
        var secondCandidate = _candidate with
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/Fight.Club.2.mkv"
        };
        _candidate.FilePath = "/media/movies/Fight.Club.1.mkv";
        _pendingBatch = [_candidate, secondCandidate];

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_updatedCandidates, Has.Count.EqualTo(2));
            _metadataService.Verify(
                s => s.SearchAsync(
                    "Fight Club", "type-a", It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_SurvivesConfidentMatch_WhenMatchedLevelDeclaresNoFileTypes()
    {
        // Arrange
        _definition = _definition with
        {
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-a",
                    ParentSlug = null,
                    DisplayName = "type-a",
                    CustomMetadataFields = [],
                    AcceptedFileTypes = []
                }
            ]
        };
        _levelSlug = "type-a";
        _parse = _ => new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, "Show Name")
                .AddValue(ParsedFileSignalKeys.TitleToken, "Show Name")
                .Build();
        _searchResponse = (_, _) =>
        [
            new MetadataSearchCandidate
            {
                ProviderType = "provider-a",
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "603",
                ContentType = "type-a",
                Title = "Show Name",
                Level = "type-a"
            }
        ];
        _candidate.FilePath = "/media/tv/Show Name/Show.Name.S01E01.mkv";
        _pendingBatch = [_candidate];

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_deletedCandidateIds, Is.Empty);
            var row = _updatedCandidates.Single();
            Assert.That(row.Status, Is.EqualTo(LibraryImportCandidateStatus.Pending));
            Assert.That(row.Matches, Has.Count.EqualTo(1));
            Assert.That(row.Matches[0].ExternalId, Is.EqualTo("603"));
        }
    }

    [Test]
    public async Task ExecuteAsync_AppliesOneMatchToEveryMember_WhenCandidatesShareAGroup()
    {
        // Arrange
        _searchResponse = (_, _) =>
        [
            new MetadataSearchCandidate
            {
                ProviderType = "provider-a",
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "42",
                ContentType = "type-a",
                Title = "Grouped Show",
                Level = "type-a"
            }
        ];
        var candidateA = _candidate with
        {
            FilePath = "/media/tv/Grouped Show/S01E01.mkv",
            LayoutKey = "/media/tv/Grouped Show"
        };
        var candidateB = _candidate with
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/tv/Grouped Show/Season 02/S02E01.mkv",
            LayoutKey = "/media/tv/Grouped Show"
        };
        _pendingBatch = [candidateA, candidateB];

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_updatedCandidates, Has.Count.EqualTo(2));
            Assert.That(_updatedCandidates[0].Matches.Single().ExternalId, Is.EqualTo("42"));
            Assert.That(_updatedCandidates[1].Matches.Single().ExternalId, Is.EqualTo("42"));
            _metadataService.Verify(
                s => s.SearchAsync(
                    "Grouped Show", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_ResolvesGroup_WhenDirectoryHasParenthesizedYear()
    {
        // Arrange
        _searchResponse = (query, _) => query == "Grouped Show"
            ?
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "recap",
                    ContentType = "type-a",
                    Title = "Grouped Show Recap",
                    Year = 1999,
                    Level = "type-a"
                },
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "42",
                    ContentType = "type-a",
                    Title = "Grouped Show",
                    Year = 1999,
                    Level = "type-a"
                }
            ]
            : [];
        _candidate.FilePath = "/media/tv/Grouped Show (1999)/S01E01.mkv";
        _candidate.LayoutKey = "/media/tv/Grouped Show (1999)";
        _pendingBatch = [_candidate];

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var match = _updatedCandidates.Single().Matches.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(match.ExternalId, Is.EqualTo("42"));
            Assert.That(match.Confidence, Is.EqualTo(0.85d));
            _metadataService.Verify(
                service => service.SearchAsync(
                    "Grouped Show", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_PrefersExactShow_WhenRootTitleIncludesReleaseYear()
    {
        // Arrange
        _parse = _ => new SignalBagBuilder()
            .AddValue(ParsedFileSignalKeys.Title, "Pilot")
            .AddValue(ParsedFileSignalKeys.TitleToken, "Pilot")
            .AddValue(ParsedFileSignalKeys.RootTitle, "Fear the Walking Dead 2015")
            .AddValue(ParsedFileSignalKeys.Year, 2015)
            .Build();
        _searchResponse = (_, _) =>
        [
            new MetadataSearchCandidate
            {
                ProviderType = "provider-a", ExternalIdSource = "provider-a.type-a", ExternalId = "recap",
                ContentType = "type-a", Title = "Fear the Walking Dead Recap", Year = 2015
            },
            new MetadataSearchCandidate
            {
                ProviderType = "provider-a", ExternalIdSource = "provider-a.type-a", ExternalId = "exact",
                ContentType = "type-a", Title = "Fear the Walking Dead", Year = 2015
            }
        ];
        _candidate.FilePath = "/media/tv/Fear the Walking Dead 2015/S01E01.mkv";
        _pendingBatch = [_candidate];

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var row = _updatedCandidates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Matches.Select(match => match.ExternalId), Is.EqualTo(["exact"]));
            Assert.That(row.Notes, Is.Null);
        }
    }

    [Test]
    public async Task ExecuteAsync_PreservesNumericTitle_WhenReleaseYearDiffers()
    {
        // Arrange
        _parse = _ => new SignalBagBuilder()
            .AddValue(ParsedFileSignalKeys.Title, "Blade Runner 2049")
            .AddValue(ParsedFileSignalKeys.TitleToken, "Blade Runner 2049")
            .AddValue(ParsedFileSignalKeys.RootTitle, "Blade Runner 2049")
            .AddValue(ParsedFileSignalKeys.Year, 2017)
            .Build();
        _searchResponse = (_, _) =>
        [
            new MetadataSearchCandidate
            {
                ProviderType = "provider-a", ExternalIdSource = "provider-a.type-a", ExternalId = "original",
                ContentType = "type-a", Title = "Blade Runner", Year = 1982
            },
            new MetadataSearchCandidate
            {
                ProviderType = "provider-a", ExternalIdSource = "provider-a.type-a", ExternalId = "2049",
                ContentType = "type-a", Title = "Blade Runner 2049", Year = 2017
            }
        ];
        _candidate.FilePath = "/media/movies/Blade Runner 2049 (2017).mkv";
        _pendingBatch = [_candidate];

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var row = _updatedCandidates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Matches.Select(match => match.ExternalId), Is.EqualTo(["2049"]));
            Assert.That(row.Notes, Is.Null);
        }
    }

    [Test]
    public async Task ExecuteAsync_UsesRootTitleSignal_WhenResolvingMusicShapedCandidate()
    {
        // Arrange
        _parse = _ => new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, "Album Name")
                .AddValue(ParsedFileSignalKeys.TitleToken, "Album Name")
                .AddValue(ParsedFileSignalKeys.RootTitle, "Artist Name")
                .Build();
        _searchResponse = (query, _) => query == "Artist Name"
            ?
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "77",
                    ContentType = "type-a",
                    Title = "Artist Name"
                }
            ]
            : [];
        _candidate.FilePath = "/media/music/Artist Name/Album Name/01.mp3";
        _pendingBatch = [_candidate];

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var row = _updatedCandidates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Matches, Has.Count.EqualTo(1));
            Assert.That(row.Matches[0].ExternalId, Is.EqualTo("77"));
            _metadataService.Verify(
                s => s.SearchAsync(
                    "Artist Name", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _metadataService.Verify(
                s => s.SearchAsync(
                    "Album Name", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_MarksCandidateFailed_WhenResolutionThrows()
    {
        // Arrange
        _parse = _ => throw new InvalidOperationException("boom");
        _candidates
            .Setup(r => r.MarkMatchingFailedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _candidate.FilePath = "/media/movies/Broken.mkv";
        _pendingBatch = [_candidate];
        _remainingAfterBatch = 0;

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _candidates.Verify(
                r => r.MarkMatchingFailedAsync(
                    _candidate.Id,
                    It.Is<string>(reason => reason.Contains("boom", StringComparison.Ordinal)),
                    _userId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(_updatedCandidates, Is.Empty);
            _jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_RequeuesItself_WhenUnresolvedCandidatesRemain()
    {
        // Arrange
        _candidate.FilePath = "/media/movies/a.mkv";
        _pendingBatch = [_candidate];
        _remainingAfterBatch = 1;

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(request =>
                    request.JobType == "LibraryImportMatchResolution"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotRequeueItself_WhenNoUnresolvedCandidatesRemain()
    {
        // Arrange
        _candidate.FilePath = "/media/movies/a.mkv";
        _pendingBatch = [_candidate];
        _remainingAfterBatch = 0;

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_ProcessesBothCandidates_WhenFirstExceedsFormerJobLimit()
    {
        // Arrange
        _searchResponse = (query, _) =>
        {
            _timeProvider.Advance(TimeSpan.FromHours(5));
            return
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "603",
                    ContentType = "type-a",
                    Title = query
                }
            ];
        };
        _candidate.FilePath = "/media/movies/a.mkv";
        var second = _candidate with
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/b.mkv"
        };
        var first = _candidate;
        _pendingBatch = [first, second];
        _remainingAfterBatch = 0;

        // Act
        await _resolutionJob
            .ExecuteAsync(_jobContext)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_updatedCandidates, Has.Count.EqualTo(2));
            Assert.That(_updatedCandidates[0].FilePath, Is.EqualTo(first.FilePath));
            Assert.That(_updatedCandidates[1].FilePath, Is.EqualTo(second.FilePath));
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(request =>
                        request.JobType == "LibraryImportMatchResolution"),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_ProcessesWholeBatch_WhenCandidatesResolve()
    {
        // Arrange
        _searchResponse = (query, _) =>
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(15));
            return
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "603",
                    ContentType = "type-a",
                    Title = query
                }
            ];
        };
        _candidate.FilePath = "/media/movies/a.mkv";
        var second = _candidate with
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ScanRunId = Guid.NewGuid(),
            FilePath = "/media/movies/b.mkv"
        };
        var first = _candidate;
        _pendingBatch = [first, second];
        _remainingAfterBatch = 0;

        // Act
        await _resolutionJob
            .ExecuteAsync(_jobContext)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_updatedCandidates, Has.Count.EqualTo(2));
            Assert.That(_updatedCandidates[0].Matches, Has.Count.EqualTo(1));
            Assert.That(_updatedCandidates[1].Matches, Has.Count.EqualTo(1));
            _jobManager.Verify(
                m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotRequeue_WhenLongBatchHasNoUnresolvedCandidates()
    {
        // Arrange
        _searchResponse = (query, _) =>
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(15));
            return
            [
                new MetadataSearchCandidate
                {
                    ProviderType = "provider-a",
                    ExternalIdSource = "provider-a.type-a",
                    ExternalId = "603",
                    ContentType = "type-a",
                    Title = query
                }
            ];
        };
        _candidate.FilePath = "/media/movies/a.mkv";
        _pendingBatch =
        [
            _candidate,
            _candidate with
            {
                Id = Guid.NewGuid(),
                RootFolderId = Guid.NewGuid(),
                ScanRunId = Guid.NewGuid(),
                FilePath = "/media/movies/b.mkv"
            }
        ];
        _remainingAfterBatch = 0;

        // Act
        await _resolutionJob
            .ExecuteAsync(_jobContext)
            .ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_MarksMatchingFailedAndAudits_WhenTheAuditWriteFails()
    {
        // Arrange
        _candidate.FilePath = "/media/group/item.mkv";
        _pendingBatch = [_candidate];
        _auditRecords.Setup(repository => repository.CreateAsync(
                It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The audit write failed."));
        _candidates.Setup(repository => repository.MarkMatchingFailedAsync(
                _candidate.Id, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _candidates.Verify(repository => repository.MarkMatchingFailedAsync(
                _candidate.Id, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == "FileMatched"),
                It.IsAny<CancellationToken>()), Times.Once);
            _auditRecords.Verify(repository => repository.CreateAsync(
                It.Is<AuditRecord>(record => record.Operation == "FileErrored"),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [TestCase(true, true, 1)]
    [TestCase(false, true, 0)]
    [TestCase(true, false, 0)]
    public async Task ExecuteAsync_OffersAConfidentMatchForAutomaticAssociation_OnlyWhenAutoImportIsOnAndItHasATarget(
        bool autoImport, bool libraryHasItem, int expectedAssociations)
    {
        // Arrange
        var targetId = Guid.NewGuid();
        _rootFolder.AutoImport = autoImport;
        _libraryTargetId = libraryHasItem ? targetId : null;
        _parse = _ => new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, "Inception")
                .AddValue(ParsedFileSignalKeys.TitleToken, "Inception")
                .Build();
        _searchResponse = (_, _) =>
        [
            new()
            {
                ProviderType = "provider-a",
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "27205",
                ContentType = "type-a",
                Title = "Inception"
            }
        ];
        _candidate.FilePath = "/media/movies/Inception.mkv";
        _pendingBatch = [_candidate];

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var row = _updatedCandidates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Matches.Select(match => match.ExternalId), Is.EqualTo(["27205"]));
            Assert.That(row.ContentHierarchyItemId, Is.EqualTo(libraryHasItem ? targetId : null));
            _libraryImport.Verify(
                s => s.AssociateMissingFilesAsync(
                    It.Is<IReadOnlyCollection<LibraryImportCandidate>>(candidates =>
                        candidates.Select(candidate => candidate.Id).SequenceEqual(new[] { _candidate.Id })),
                    _userId,
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(expectedAssociations));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ExecuteAsync_KeepsAnAmbiguousMatchPending_WhenResultsTie(bool autoImport)
    {
        // Arrange
        _rootFolder.AutoImport = autoImport;
        _libraryTargetId = Guid.NewGuid();
        _parse = _ => new SignalBagBuilder()
                .AddValue(ParsedFileSignalKeys.Title, "Inception")
                .AddValue(ParsedFileSignalKeys.TitleToken, "Inception")
                .Build();
        _searchResponse = (_, _) =>
        [
            new()
            {
                ProviderType = "provider-a",
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "27205",
                ContentType = "type-a",
                Title = "Inception"
            },
            new()
            {
                ProviderType = "provider-a",
                ExternalIdSource = "provider-a.type-a",
                ExternalId = "99999",
                ContentType = "type-a",
                Title = "Inception"
            }
        ];
        _candidate.FilePath = "/media/movies/Inception.mkv";
        _pendingBatch = [_candidate];

        // Act
        await _resolutionJob.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        var row = _updatedCandidates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Status, Is.EqualTo(LibraryImportCandidateStatus.Pending));
            Assert.That(row.Matches, Is.Empty);
            Assert.That(row.ContentHierarchyItemId, Is.Null);
            _libraryImport.Verify(
                s => s.AssociateMissingFilesAsync(
                    It.IsAny<IReadOnlyCollection<LibraryImportCandidate>>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
