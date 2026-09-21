using System;
using System.Collections.Generic;
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
using Magpie.Models.Paging;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Diagnostics;

[TestFixture]
public sealed class DiagnosticCheckJobTests
{
    private ILoggerFactory _loggerFactory = null!;
    private List<DiagnosticFinding> _storedFindings = null!;
    private IDiagnosticFindingRepository _findingRepository = null!;
    private IMediaServerRepository _mediaServers = null!;
    private IRootFolderRepository _rootFolders = null!;
    private IDownloadClientRepository _downloadClients = null!;
    private IIndexerFeedRepository _indexerFeeds = null!;
    private ISettingsRepository _settingsRepository = null!;
    private ITransactionManager _transactionManager = null!;
    private IEventBus _eventBus = null!;
    private List<MagpieEvent> _publishedEvents = null!;
    private JobContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _loggerFactory = NullLoggerFactory.Instance;

        _storedFindings = [];
        var findingRepository = new Mock<IDiagnosticFindingRepository>(MockBehavior.Strict);
        findingRepository.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_storedFindings);
        findingRepository.Setup(repository => repository.QueryAllAsync(
            It.IsAny<DiagnosticFindingQuery>(),
            null,
            It.IsAny<CancellationToken>()))
            .Returns((DiagnosticFindingQuery query, SortParameter? _, CancellationToken _) =>
                Task.FromResult<IReadOnlyList<DiagnosticFinding>>(_storedFindings.Where(finding =>
                    finding.DiagnosticCheckId == query.DiagnosticCheckId).ToList()));
        findingRepository.Setup(repository => repository.DeleteAsync(
            It.IsAny<Guid>(),
            null,
            It.IsAny<CancellationToken>()))
            .Returns((Guid id, Guid? _, CancellationToken _) =>
                Task.FromResult(_storedFindings.RemoveAll(finding => finding.Id == id) > 0));
        findingRepository.Setup(repository => repository.CreateAsync(
            It.IsAny<DiagnosticFinding>(),
            null,
            It.IsAny<CancellationToken>()))
            .Returns((DiagnosticFinding finding, Guid? _, CancellationToken _) =>
            {
                _storedFindings.Add(finding);
                return Task.FromResult(finding);
            });
        _findingRepository = findingRepository.Object;

        _mediaServers = Mock.Of<IMediaServerRepository>(r =>
            r.ListAllAsync(It.IsAny<CancellationToken>()) ==
                Task.FromResult<IEnumerable<MediaServer>>(Array.Empty<MediaServer>()));
        _rootFolders = Mock.Of<IRootFolderRepository>(r =>
            r.ListAllAsync(It.IsAny<CancellationToken>()) ==
                Task.FromResult<IEnumerable<RootFolder>>(Array.Empty<RootFolder>()));
        _downloadClients = Mock.Of<IDownloadClientRepository>(r =>
            r.ListAllAsync(It.IsAny<CancellationToken>()) ==
                Task.FromResult<IEnumerable<DownloadClient>>(Array.Empty<DownloadClient>()));
        _indexerFeeds = Mock.Of<IIndexerFeedRepository>(r =>
            r.ListAllAsync(It.IsAny<CancellationToken>()) ==
                Task.FromResult<IEnumerable<IndexerFeed>>(Array.Empty<IndexerFeed>()));

        _settingsRepository = Mock.Of<ISettingsRepository>();
        var transactionManager = new Mock<ITransactionManager>();
        transactionManager.Setup(manager => manager.ExecuteWithTransactionAsync(
            It.IsAny<Func<CancellationToken, Task>>(),
            It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken token) => operation(token));
        _transactionManager = transactionManager.Object;
        _publishedEvents = [];
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) => _publishedEvents.Add(magpieEvent))
            .Returns(Task.CompletedTask);
        _eventBus = eventBus.Object;

        _context = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);
    }

    [Test]
    public async Task ExecuteAsync_PersistsFindings_WhenRegisteredDiagnosticsRun()
    {
        // Arrange
        var job = new DiagnosticCheckJob(
            [
                Mock.Of<IDiagnosticCheck>(c =>
                    c.DiagnosticCheckId == "test.a" &&
                    c.RunAsync(It.IsAny<DiagnosticContext>(), It.IsAny<CancellationToken>()) ==
                        Task.FromResult<IReadOnlyList<DiagnosticFinding>>(new[]
                        {
                            new DiagnosticFinding
                            {
                                DiagnosticCheckId = "unset",
                                Severity = DiagnosticFindingSeverity.Info,
                                Title = "alpha",
                                Detail = "detail",
                            },
                        })),
                Mock.Of<IDiagnosticCheck>(c =>
                    c.DiagnosticCheckId == "test.b" &&
                    c.RunAsync(It.IsAny<DiagnosticContext>(), It.IsAny<CancellationToken>()) ==
                        Task.FromResult<IReadOnlyList<DiagnosticFinding>>(new[]
                        {
                            new DiagnosticFinding
                            {
                                DiagnosticCheckId = "unset",
                                Severity = DiagnosticFindingSeverity.Info,
                                Title = "beta",
                                Detail = "detail",
                            },
                        })),
            ],
            _findingRepository,
            _mediaServers,
            _rootFolders,
            _downloadClients,
            _indexerFeeds,
            _settingsRepository,
            _transactionManager,
            _eventBus,
            TimeProvider.System,
            _loggerFactory.CreateLogger<DiagnosticCheckJob>());

        // Act
        await job.ExecuteAsync(_context).ConfigureAwait(false);

        // Assert
        var persisted = (await _findingRepository.ListAllAsync().ConfigureAwait(false)).ToList();
        var completed = _publishedEvents.Single(e => e.Name == "diagnostic.scan.completed");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted, Has.Count.EqualTo(2));
            Assert.That(persisted.Select(f => f.DiagnosticCheckId), Is.EquivalentTo(["test.a", "test.b"]));
            Assert.That(persisted.Select(f => f.Title), Is.EquivalentTo(["alpha", "beta"]));
            Assert.That(persisted.All(f => f.Id != Guid.Empty), Is.True);
            Assert.That(persisted.All(f => f.DetectedAt != default), Is.True);
            Assert.That(completed.Context["diagnostic.findingCount"], Is.EqualTo("2"));
            Assert.That(_publishedEvents[^1], Is.SameAs(completed));
        }
    }

    [Test]
    public async Task ExecuteAsync_PreservesPriorFindings_WhenOneDiagnosticThrows()
    {
        // Arrange
        _storedFindings.Add(new DiagnosticFinding
        {
            DiagnosticCheckId = "test.bad",
            Severity = DiagnosticFindingSeverity.Warning,
            Title = "Unresolved configuration",
            Detail = "The setting is missing.",
            DetectedAt = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc)
        });
        var throwing = new Mock<IDiagnosticCheck>();
        throwing.SetupGet(c => c.DiagnosticCheckId).Returns("test.bad");
        throwing
            .Setup(c => c.RunAsync(It.IsAny<DiagnosticContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("diagnostic blew up"));
        var job = new DiagnosticCheckJob(
            [
                throwing.Object,
                Mock.Of<IDiagnosticCheck>(c =>
                    c.DiagnosticCheckId == "test.good" &&
                    c.RunAsync(It.IsAny<DiagnosticContext>(), It.IsAny<CancellationToken>()) ==
                        Task.FromResult<IReadOnlyList<DiagnosticFinding>>(new[]
                        {
                            new DiagnosticFinding
                            {
                                DiagnosticCheckId = "unset",
                                Severity = DiagnosticFindingSeverity.Info,
                                Title = "survivor",
                                Detail = "detail",
                            },
                        })),
            ],
            _findingRepository,
            _mediaServers,
            _rootFolders,
            _downloadClients,
            _indexerFeeds,
            _settingsRepository,
            _transactionManager,
            _eventBus,
            TimeProvider.System,
            _loggerFactory.CreateLogger<DiagnosticCheckJob>());

        // Act
        await job.ExecuteAsync(_context).ConfigureAwait(false);

        // Assert
        var persisted = (await _findingRepository.ListAllAsync().ConfigureAwait(false)).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted, Has.Count.EqualTo(2));
            Assert.That(persisted[0].DiagnosticCheckId, Is.EqualTo("test.bad"));
            Assert.That(persisted[0].Title, Is.EqualTo("Unresolved configuration"));
            Assert.That(persisted[0].Detail, Is.EqualTo("The setting is missing."));
            Assert.That(persisted[0].DetectedAt, Is.EqualTo(new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc)));
            Assert.That(persisted[1].DiagnosticCheckId, Is.EqualTo("test.good"));
        }
    }

    [Test]
    public async Task ExecuteAsync_SupersedesPriorFindings_WhenMultipleDiagnosticsRun()
    {
        // Arrange
        var seed = new DiagnosticCheckJob(
            [
                Mock.Of<IDiagnosticCheck>(c =>
                    c.DiagnosticCheckId == "test.a" &&
                    c.RunAsync(It.IsAny<DiagnosticContext>(), It.IsAny<CancellationToken>()) ==
                        Task.FromResult<IReadOnlyList<DiagnosticFinding>>(new[]
                        {
                            new DiagnosticFinding
                            {
                                DiagnosticCheckId = "unset",
                                Severity = DiagnosticFindingSeverity.Info,
                                Title = "alpha",
                                Detail = "detail",
                            },
                        })),
                Mock.Of<IDiagnosticCheck>(c =>
                    c.DiagnosticCheckId == "test.b" &&
                    c.RunAsync(It.IsAny<DiagnosticContext>(), It.IsAny<CancellationToken>()) ==
                        Task.FromResult<IReadOnlyList<DiagnosticFinding>>(new[]
                        {
                            new DiagnosticFinding
                            {
                                DiagnosticCheckId = "unset",
                                Severity = DiagnosticFindingSeverity.Info,
                                Title = "beta",
                                Detail = "detail",
                            },
                        })),
            ],
            _findingRepository,
            _mediaServers,
            _rootFolders,
            _downloadClients,
            _indexerFeeds,
            _settingsRepository,
            _transactionManager,
            _eventBus,
            TimeProvider.System,
            _loggerFactory.CreateLogger<DiagnosticCheckJob>());
        await seed.ExecuteAsync(_context).ConfigureAwait(false);

        // Act
        var job = new DiagnosticCheckJob(
            [
                Mock.Of<IDiagnosticCheck>(c =>
                    c.DiagnosticCheckId == "test.a" &&
                    c.RunAsync(It.IsAny<DiagnosticContext>(), It.IsAny<CancellationToken>()) ==
                        Task.FromResult<IReadOnlyList<DiagnosticFinding>>(Array.Empty<DiagnosticFinding>())),
                Mock.Of<IDiagnosticCheck>(c =>
                    c.DiagnosticCheckId == "test.b" &&
                    c.RunAsync(It.IsAny<DiagnosticContext>(), It.IsAny<CancellationToken>()) ==
                        Task.FromResult<IReadOnlyList<DiagnosticFinding>>(new[]
                        {
                            new DiagnosticFinding
                            {
                                DiagnosticCheckId = "unset",
                                Severity = DiagnosticFindingSeverity.Info,
                                Title = "beta-2",
                                Detail = "detail",
                            },
                        })),
            ],
            _findingRepository,
            _mediaServers,
            _rootFolders,
            _downloadClients,
            _indexerFeeds,
            _settingsRepository,
            _transactionManager,
            _eventBus,
            TimeProvider.System,
            _loggerFactory.CreateLogger<DiagnosticCheckJob>());
        await job.ExecuteAsync(_context).ConfigureAwait(false);

        // Assert
        var persisted = (await _findingRepository.ListAllAsync().ConfigureAwait(false)).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted, Has.Count.EqualTo(1));
            Assert.That(persisted[0].DiagnosticCheckId, Is.EqualTo("test.b"));
            Assert.That(persisted[0].Title, Is.EqualTo("beta-2"));
        }
    }

    [Test]
    public async Task ExecuteAsync_PreservesOtherDiagnosticFindings_WhenOneDiagnosticIsAbsent()
    {
        // Arrange
        var seed = new DiagnosticCheckJob(
            [
                Mock.Of<IDiagnosticCheck>(c =>
                    c.DiagnosticCheckId == "test.a" &&
                    c.RunAsync(It.IsAny<DiagnosticContext>(), It.IsAny<CancellationToken>()) ==
                        Task.FromResult<IReadOnlyList<DiagnosticFinding>>(new[]
                        {
                            new DiagnosticFinding
                            {
                                DiagnosticCheckId = "unset",
                                Severity = DiagnosticFindingSeverity.Info,
                                Title = "alpha",
                                Detail = "detail",
                            },
                        })),
                Mock.Of<IDiagnosticCheck>(c =>
                    c.DiagnosticCheckId == "test.b" &&
                    c.RunAsync(It.IsAny<DiagnosticContext>(), It.IsAny<CancellationToken>()) ==
                        Task.FromResult<IReadOnlyList<DiagnosticFinding>>(new[]
                        {
                            new DiagnosticFinding
                            {
                                DiagnosticCheckId = "unset",
                                Severity = DiagnosticFindingSeverity.Info,
                                Title = "beta",
                                Detail = "detail",
                            },
                        })),
            ],
            _findingRepository,
            _mediaServers,
            _rootFolders,
            _downloadClients,
            _indexerFeeds,
            _settingsRepository,
            _transactionManager,
            _eventBus,
            TimeProvider.System,
            _loggerFactory.CreateLogger<DiagnosticCheckJob>());
        await seed.ExecuteAsync(_context).ConfigureAwait(false);

        // Act
        var job = new DiagnosticCheckJob(
            [
                Mock.Of<IDiagnosticCheck>(c =>
                    c.DiagnosticCheckId == "test.b" &&
                    c.RunAsync(It.IsAny<DiagnosticContext>(), It.IsAny<CancellationToken>()) ==
                        Task.FromResult<IReadOnlyList<DiagnosticFinding>>(new[]
                        {
                            new DiagnosticFinding
                            {
                                DiagnosticCheckId = "unset",
                                Severity = DiagnosticFindingSeverity.Info,
                                Title = "beta-2",
                                Detail = "detail",
                            },
                        })),
            ],
            _findingRepository,
            _mediaServers,
            _rootFolders,
            _downloadClients,
            _indexerFeeds,
            _settingsRepository,
            _transactionManager,
            _eventBus,
            TimeProvider.System,
            _loggerFactory.CreateLogger<DiagnosticCheckJob>());
        await job.ExecuteAsync(_context).ConfigureAwait(false);

        // Assert
        var persisted = (await _findingRepository.ListAllAsync().ConfigureAwait(false)).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(persisted, Has.Count.EqualTo(2));
            Assert.That(persisted.Select(f => f.DiagnosticCheckId), Is.EquivalentTo(["test.a", "test.b"]));
            Assert.That(persisted.Single(f => f.DiagnosticCheckId == "test.b").Title, Is.EqualTo("beta-2"));
            Assert.That(persisted.Single(f => f.DiagnosticCheckId == "test.a").Title, Is.EqualTo("alpha"));
        }
    }

    [Test]
    public async Task ExecuteAsync_ClearsPriorFindings_WhenDiagnosticReturnsNone()
    {
        // Arrange
        var seed = new DiagnosticCheckJob(
            [
                Mock.Of<IDiagnosticCheck>(c =>
                    c.DiagnosticCheckId == "test.a" &&
                    c.RunAsync(It.IsAny<DiagnosticContext>(), It.IsAny<CancellationToken>()) ==
                        Task.FromResult<IReadOnlyList<DiagnosticFinding>>(new[]
                        {
                            new DiagnosticFinding
                            {
                                DiagnosticCheckId = "unset",
                                Severity = DiagnosticFindingSeverity.Info,
                                Title = "alpha",
                                Detail = "detail",
                            },
                        })),
            ],
            _findingRepository,
            _mediaServers,
            _rootFolders,
            _downloadClients,
            _indexerFeeds,
            _settingsRepository,
            _transactionManager,
            _eventBus,
            TimeProvider.System,
            _loggerFactory.CreateLogger<DiagnosticCheckJob>());
        await seed.ExecuteAsync(_context).ConfigureAwait(false);

        // Act
        var job = new DiagnosticCheckJob(
            [
                Mock.Of<IDiagnosticCheck>(c =>
                    c.DiagnosticCheckId == "test.a" &&
                    c.RunAsync(It.IsAny<DiagnosticContext>(), It.IsAny<CancellationToken>()) ==
                        Task.FromResult<IReadOnlyList<DiagnosticFinding>>(Array.Empty<DiagnosticFinding>())),
            ],
            _findingRepository,
            _mediaServers,
            _rootFolders,
            _downloadClients,
            _indexerFeeds,
            _settingsRepository,
            _transactionManager,
            _eventBus,
            TimeProvider.System,
            _loggerFactory.CreateLogger<DiagnosticCheckJob>());
        await job.ExecuteAsync(_context).ConfigureAwait(false);

        // Assert
        var persisted = await _findingRepository.ListAllAsync().ConfigureAwait(false);
        Assert.That(persisted, Is.Empty);
    }
}
