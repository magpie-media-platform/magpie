using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Fulfillment;

[TestFixture]
public sealed class DownloadImportScanJobTests
{
    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    private Mock<IDownloadRepository> _downloadRepository = null!;
    private Mock<IJobManager> _jobManager = null!;
    private Mock<IJobExecutionRepository> _executions = null!;
    private IJob _subject = null!;
    private JobContext _jobContext = null!;

    [SetUp]
    public void SetUp()
    {
        _downloadRepository = new Mock<IDownloadRepository> { CallBase = true };
        _downloadRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download> { Items = [] });
        _jobManager = new Mock<IJobManager>();
        _jobManager
            .Setup(m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _executions = new Mock<IJobExecutionRepository> { CallBase = true };
        _executions.Setup(repository => repository.QueryAsync(It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [] });
        _subject = new DownloadImportScanJob(
            _downloadRepository.Object,
            _executions.Object,
            _jobManager.Object,
            TimeProvider.System,
            NullLogger<DownloadImportScanJob>.Instance,
            _progressReporter);

        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);
    }

    [TestCase(JobExecutionStatus.Queued)]
    [TestCase(JobExecutionStatus.Running)]
    [TestCase(JobExecutionStatus.CancellationRequested)]
    public async Task ExecuteAsync_ProtectsImport_WhenExecutionIsStillActive(JobExecutionStatus status)
    {
        // Arrange
        var id = Guid.NewGuid();
        _downloadRepository.Setup(repository => repository.QueryAsync(It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items = [new() { Id = id, Title = "Active", ExternalId = "active", Status = DownloadStatus.Importing }]
            });
        _executions.Setup(repository => repository.QueryAsync(It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items = [new()
                {
                    JobType = "DownloadImportItem", Status = status,
                    TriggerContext = new() { ["event.context.download.id"] = id.ToString("D") },
                    Parameters = new() { ["downloadId"] = id.ToString("D") }
                }]
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase("fail")]
    [TestCase("skip")]
    [TestCase("replace")]
    [TestCase(null)]
    public async Task ExecuteAsync_RetainsExplicitPolicy_WhenPreviousAttemptNeedsRecovery(string? mode)
    {
        // Arrange
        var id = Guid.NewGuid();
        var parameters = new Dictionary<string, string> { ["downloadId"] = id.ToString("D") };
        if (mode is not null)
        {
            parameters["collisionMode"] = mode;
        }

        _downloadRepository.Setup(repository => repository.QueryAsync(It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items = [new()
                {
                    Id = id, Title = "Recovery", ExternalId = "recovery", Status = DownloadStatus.Downloaded
                }]
            });
        _executions.Setup(repository => repository.QueryAsync(It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new()
                    {
                        JobType = "DownloadImportItem", Status = JobExecutionStatus.Cancelled,
                        Parameters = parameters, CreatedAt = new DateTime(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc)
                    },
                    new()
                    {
                        JobType = "DownloadImportItem", Status = JobExecutionStatus.Completed,
                        Parameters = new() { ["downloadId"] = id.ToString("D"), ["dryRun"] = "true",
                            ["collisionMode"] = "fail" },
                        CreatedAt = new DateTime(2026, 9, 8, 11, 0, 0, DateTimeKind.Utc)
                    }
                ]
            });
        JobEnqueueRequest? queued = null;
        _jobManager.Setup(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => queued = request)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That(queued!.Parameters, Is.EquivalentTo(parameters));
    }

    [Test]
    public async Task ExecuteAsync_ProtectsImport_WhenExecutionStartsAfterHistoryRead()
    {
        // Arrange
        var id = new Guid("abcdefff-1234-5678-90ab-cdef12345678");
        _downloadRepository.Setup(repository => repository.QueryAsync(It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items = [new() { Id = id, Title = "Active", ExternalId = "active", Status = DownloadStatus.Importing }]
            });
        _executions.Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query => query.Statuses != null),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items = [new()
                {
                    JobType = "DownloadImportItem", Status = JobExecutionStatus.Running,
                    Parameters = new() { ["downloadId"] = " abcdefff-1234-5678-90ab-cdef12345678 " }
                }]
            });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _jobManager.Verify(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _executions.Verify(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query => query.Statuses == null),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()), Times.Once);
            _executions.Verify(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query => query.Statuses != null && query.ParameterValueReferences == null),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [TestCase(ImportCollisionMode.Fail, "Fail")]
    [TestCase(ImportCollisionMode.Skip, "Skip")]
    [TestCase(ImportCollisionMode.Replace, "Replace")]
    [TestCase(null, null)]
    public async Task ExecuteAsync_RetainsStoredPolicy_WhenExecutionHistoryWasRemoved(
        ImportCollisionMode? mode, string? expected)
    {
        // Arrange
        var id = Guid.NewGuid();
        _downloadRepository.Setup(repository => repository.QueryAsync(It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items = [new()
                {
                    Id = id, Title = "Recovery", ExternalId = "recovery", Status = DownloadStatus.Importing,
                    ImportCollisionMode = mode
                }]
            });
        JobEnqueueRequest? queued = null;
        _jobManager.Setup(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => queued = request)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        Assert.That(queued!.Parameters!.GetValueOrDefault("collisionMode"), Is.EqualTo(expected));
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesDownloadImportItem_WhenImportCandidatesExist()
    {
        // Arrange
        var download = new Download
        {
            Id = Guid.NewGuid(),
            DownloadClientId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = "Test Download",
            Status = DownloadStatus.Downloaded,
            ModifiedAt = DateTime.UtcNow - TimeSpan.FromMinutes(10) - TimeSpan.FromMinutes(5),
            CreatedAt = DateTime.UtcNow
        };
        _downloadRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download> { Items = [download], TotalItems = 1 });

        var correlationId = Guid.NewGuid();

        // Act
        await _subject.ExecuteAsync(_jobContext with { CorrelationId = correlationId }).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(
                It.Is<JobEnqueueRequest>(r =>
                    r.JobType == "DownloadImportItem" &&
                    r.ExtensionId == null &&
                    r.Parameters != null &&
                    r.Parameters["downloadId"] == download.Id.ToString("D") &&
                    r.Mode == JobExecutionMode.Queue &&
                    r.UserId == null &&
                    r.CorrelationId == correlationId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_QueuesImportInTheRecoveryCorrelation_WhenAPreviousAttemptHasAnotherCorrelation()
    {
        // Arrange
        var id = Guid.NewGuid();
        var recoveryCorrelationId = Guid.NewGuid();
        _downloadRepository.Setup(repository => repository.QueryAsync(It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items = [new()
                {
                    Id = id, Title = "Recovery", ExternalId = "recovery", Status = DownloadStatus.Downloaded
                }]
            });
        _executions.Setup(repository => repository.QueryAsync(It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items = [new()
                {
                    JobType = "DownloadImportItem", Status = JobExecutionStatus.Cancelled,
                    Parameters = new() { ["downloadId"] = id.ToString("D") }, CorrelationId = Guid.NewGuid()
                }]
            });
        JobEnqueueRequest? queued = null;
        _jobManager.Setup(manager => manager.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()))
            .Callback<JobEnqueueRequest, CancellationToken>((request, _) => queued = request)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _subject.ExecuteAsync(_jobContext with { CorrelationId = recoveryCorrelationId }).ConfigureAwait(false);

        // Assert
        Assert.That(queued?.CorrelationId, Is.EqualTo(recoveryCorrelationId));
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesNothing_WhenNoCandidatesExist()
    {
        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        _jobManager.Verify(
            m => m.ExecuteAsync(It.IsAny<JobEnqueueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_UsesDefaultSettleCutoff_WhenNoParameterProvided()
    {
        // Arrange
        DownloadQuery? observedQuery = null;
        PagingParameter? observedPaging = null;
        SortParameter? observedSort = null;
        _downloadRepository
            .Setup(r => r.QueryAsync(
                It.Is<DownloadQuery>(query => query.HasImportRecoveryState == null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .Callback<DownloadQuery, PagingParameter, SortParameter?, CancellationToken>((query, paging, sort, _) =>
            {
                observedQuery = query;
                observedPaging = paging;
                observedSort = sort;
            })
            .ReturnsAsync(new PagedList<Download> { Items = [] });

        // Act
        var before = DateTime.UtcNow;
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);
        var after = DateTime.UtcNow;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                observedQuery?.LastChangedAt?.Max,
                Is.InRange(before - TimeSpan.FromMinutes(10), after - TimeSpan.FromMinutes(10)));
            Assert.That(observedQuery?.LastChangedAt?.Min, Is.Null);
            Assert.That(
                observedQuery?.Statuses,
                Is.EquivalentTo([DownloadStatus.Downloaded, DownloadStatus.Importing]));
            Assert.That(observedPaging?.PageNumber, Is.EqualTo(1));
            Assert.That(observedPaging?.PageSize, Is.EqualTo(500));
            Assert.That(observedSort?.PropertyName, Is.EqualTo(nameof(DownloadQuery.LastChangedAt)));
            Assert.That(observedSort?.Direction, Is.EqualTo(SortDirection.Ascending));
        }
    }

    [Test]
    public async Task ExecuteAsync_UsesSettleMinutes_WhenParameterIsSpecified()
    {
        // Arrange
        var download = new Download
        {
            Id = Guid.NewGuid(),
            DownloadClientId = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = "Test Download",
            Status = DownloadStatus.Downloaded,
            ModifiedAt = DateTime.UtcNow - TimeSpan.FromMinutes(2),
            CreatedAt = DateTime.UtcNow
        };
        DownloadQuery? observedQuery = null;
        _downloadRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .Callback<DownloadQuery, PagingParameter, SortParameter?, CancellationToken>((query, _, _, _) =>
                observedQuery = query)
            .ReturnsAsync(new PagedList<Download> { Items = [download], TotalItems = 1 });
        var jobContext = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["settleMinutes"] = "1"
            }
        };

        // Act
        var before = DateTime.UtcNow;
        await _subject.ExecuteAsync(jobContext).ConfigureAwait(false);
        var after = DateTime.UtcNow;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                observedQuery?.LastChangedAt?.Max,
                Is.InRange(before - TimeSpan.FromMinutes(1), after - TimeSpan.FromMinutes(1)));
            _jobManager.Verify(
                m => m.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(r =>
                        r.JobType == "DownloadImportItem" &&
                        r.Parameters != null &&
                        r.Parameters["downloadId"] == download.Id.ToString("D")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_TraversesSecondPage_WhenFirstPageIsFull()
    {
        // Arrange
        var firstPage = Enumerable.Range(0, 500)
            .Select(index => new Download
            {
                Id = Guid.NewGuid(),
                DownloadClientId = Guid.NewGuid(),
                ExternalId = $"download-{index}",
                Title = $"Download {index}",
                Status = DownloadStatus.Downloaded,
                CreatedAt = DateTime.UtcNow
            })
            .ToList();
        var secondPageDownload = new Download
        {
            Id = Guid.NewGuid(),
            DownloadClientId = Guid.NewGuid(),
            ExternalId = "download-500",
            Title = "Download 500",
            Status = DownloadStatus.Importing,
            CreatedAt = DateTime.UtcNow
        };
        _downloadRepository
            .Setup(r => r.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DownloadQuery _, PagingParameter paging, SortParameter? _, CancellationToken _) =>
                new PagedList<Download>
                {
                    Items = paging.PageNumber == 1 ? firstPage : [secondPageDownload],
                    PageNumber = paging.PageNumber,
                    PageSize = paging.PageSize,
                    TotalItems = 501
                });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadRepository.Verify(
                r => r.QueryAsync(
                    It.IsAny<DownloadQuery>(),
                    It.Is<PagingParameter>(paging => paging.PageNumber == 2 && paging.PageSize == 500),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            _executions.Verify(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query => query.JobType == "DownloadImportItem" &&
                    query.Statuses == null && query.ParameterValueReferences == null),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()), Times.Once);
            _jobManager.Verify(
                manager => manager.ExecuteAsync(
                    It.Is<JobEnqueueRequest>(request =>
                        request.Parameters != null &&
                        request.Parameters["downloadId"] ==
                            secondPageDownload.Id.ToString("D")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
