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
    private Mock<IDownloadRepository> _downloadRepository = null!;
    private Mock<IJobManager> _jobManager = null!;
    private IJob _subject = null!;
    private JobContext _jobContext = null!;

    [SetUp]
    public void SetUp()
    {
        _downloadRepository = new Mock<IDownloadRepository>();
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

        _subject = new DownloadImportScanJob(
            _downloadRepository.Object,
            _jobManager.Object,
            TimeProvider.System,
            NullLogger<DownloadImportScanJob>.Instance);

        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);
    }

    [Test]
    public void Spec_DeclaresSettleMinutesParameter_WithDefault()
    {
        // Arrange
        // Act
        var parameters = DownloadImportScanJob.Spec.Parameters;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameters, Has.Count.EqualTo(1));
            Assert.That(parameters[0].Name, Is.EqualTo("settleMinutes"));
            Assert.That(parameters[0].Type, Is.EqualTo(DataType.Number));
            Assert.That(parameters[0].Required, Is.False);
            Assert.That(parameters[0].DefaultValue, Is.EqualTo("10"));
        }
    }

    [Test]
    public async Task ExecuteAsync_EnqueuesDownloadImportItem_ForEachCandidate()
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

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

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
                    r.CorrelationId == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
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
                It.IsAny<DownloadQuery>(),
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
            Assert.That(observedPaging?.PageSize, Is.EqualTo(200));
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
        var firstPage = Enumerable.Range(0, 200)
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
            ExternalId = "download-200",
            Title = "Download 200",
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
                    TotalItems = 201
                });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _downloadRepository.Verify(
                r => r.QueryAsync(
                    It.IsAny<DownloadQuery>(),
                    It.Is<PagingParameter>(paging => paging.PageNumber == 2 && paging.PageSize == 200),
                    It.IsAny<SortParameter?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
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
