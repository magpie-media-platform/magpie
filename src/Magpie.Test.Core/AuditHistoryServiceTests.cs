using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class AuditHistoryServiceTests
{
    private Mock<IJobExecutionRepository> _jobExecutions = null!;
    private Mock<IAuditRecordRepository> _auditRecords = null!;
    private Mock<IDownloadRepository> _downloads = null!;
    private IAuditHistoryService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _jobExecutions = new Mock<IJobExecutionRepository>();
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>());
        _auditRecords = new Mock<IAuditRecordRepository>();
        _auditRecords
            .Setup(r => r.QueryAsync(
                It.IsAny<AuditRecordQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<AuditRecord>());

        _downloads = new Mock<IDownloadRepository>();
        _downloads
            .Setup(r => r.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>());

        _subject = new AuditHistoryService(_auditRecords.Object, _jobExecutions.Object, _downloads.Object);
    }

    [Test]
    public async Task QueryAsync_ReadsOneBoundedSourcePage_WhenTheReachFitsThePagingLimit()
    {
        // Arrange
        var audit = new AuditRecord
        {
            Category = "System",
            Operation = "Update",
            Timestamp = new DateTime(2024, 5, 6, 0, 0, 0, DateTimeKind.Utc)
        };
        _auditRecords.Setup(repository => repository.QueryAsync(
                It.IsAny<AuditRecordQuery>(),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 50),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<AuditRecord>
            {
                Items = Enumerable.Repeat(audit, 50),
                TotalItems = 1001
            });

        // Act
        var result = await _subject.QueryAsync(new AuditHistoryQuery
        {
            Kind = AuditHistoryEntryKind.AuditRecord,
            Paging = new PagingParameter { PageNumber = 2, PageSize = 25 }
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalItems, Is.EqualTo(1001));
            Assert.That(result.Items.Count(), Is.EqualTo(25));
            _auditRecords.Verify(repository => repository.QueryAsync(
                It.IsAny<AuditRecordQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [TestCase(2, 1000)]
    [TestCase(int.MaxValue, 25)]
    public void QueryAsync_Throws_WhenTheReachExceedsThePagingLimit(int pageNumber, int pageSize)
    {
        // Arrange
        var query = new AuditHistoryQuery
        {
            Kind = AuditHistoryEntryKind.AuditRecord,
            Paging = new PagingParameter { PageNumber = pageNumber, PageSize = pageSize }
        };

        // Act
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(async () => await _subject.QueryAsync(query).ConfigureAwait(false), Throws.ArgumentException);
            _auditRecords.Verify(repository => repository.QueryAsync(
                It.IsAny<AuditRecordQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task QueryAsync_MergesJobsAndAuditsInTimestampOrder_WhenBothSourcesHaveRows()
    {
        // Arrange
        var baseline = DateTime.UtcNow;
        var job = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Completed,
            CreatedAt = baseline.AddMinutes(-2),
            CompletedAt = baseline
        };
        var audit = new AuditRecord
        {
            Id = Guid.NewGuid(),
            Category = "Content",
            Operation = "Update",
            Timestamp = baseline.AddMinutes(-1)
        };
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [job], TotalItems = 1 });
        _auditRecords
            .Setup(r => r.QueryAsync(
                It.IsAny<AuditRecordQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<AuditRecord> { Items = [audit], TotalItems = 1 });

        // Act
        var page = await _subject.QueryAsync(new AuditHistoryQuery
        {
            Paging = new PagingParameter { PageNumber = 1, PageSize = 25 }
        }).ConfigureAwait(false);

        // Assert
        var items = page.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.TotalItems, Is.EqualTo(2));
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Kind, Is.EqualTo(AuditHistoryEntryKind.JobExecution));
            Assert.That(items[0].Id, Is.EqualTo(job.Id));
            Assert.That(items[1].Kind, Is.EqualTo(AuditHistoryEntryKind.AuditRecord));
        }
    }

    [Test]
    public async Task QueryAsync_ReadsOnlyTheRowsThatReachThePage_WhenNoFilterIsGiven()
    {
        // Arrange
        var baseline = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var job = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "FirstJob",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            CreatedAt = baseline.AddMinutes(-6),
            CompletedAt = baseline
        };
        var laterJob = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "SecondJob",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            CreatedAt = baseline.AddMinutes(-6),
            CompletedAt = baseline.AddMinutes(-2)
        };
        var audit = new AuditRecord
        {
            Id = Guid.NewGuid(),
            Category = "Content",
            Operation = "Update",
            Timestamp = baseline.AddMinutes(-1)
        };
        var laterAudit = new AuditRecord
        {
            Id = Guid.NewGuid(),
            Category = "Content",
            Operation = "Update",
            Timestamp = baseline.AddMinutes(-3)
        };
        _jobExecutions
            .Setup(repository => repository.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 2),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [job, laterJob], TotalItems = 900 });
        _jobExecutions
            .Setup(repository => repository.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.Is<PagingParameter>(paging => paging.PageNumber > 1 || paging.PageSize > 2),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The service read past the requested page of job executions."));
        _auditRecords
            .Setup(repository => repository.QueryAsync(
                It.IsAny<AuditRecordQuery>(),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 2),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<AuditRecord> { Items = [audit, laterAudit], TotalItems = 700 });
        _auditRecords
            .Setup(repository => repository.QueryAsync(
                It.IsAny<AuditRecordQuery>(),
                It.Is<PagingParameter>(paging => paging.PageNumber > 1 || paging.PageSize > 2),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The service read past the requested page of audit records."));

        // Act
        var page = await _subject.QueryAsync(new AuditHistoryQuery
        {
            Paging = new PagingParameter { PageNumber = 1, PageSize = 2 }
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Select(entry => entry.Id), Is.EqualTo([job.Id, audit.Id]));
            Assert.That(page.TotalItems, Is.EqualTo(1600));
        }

        _jobExecutions.Verify(
            repository => repository.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.Is<SortParameter?>(sort =>
                    sort != null &&
                    sort.PropertyName == "ExecutionTimestamp" &&
                    sort.Direction == SortDirection.Descending),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _auditRecords.Verify(
            repository => repository.QueryAsync(
                It.IsAny<AuditRecordQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task QueryAsync_ReturnsLaterEntries_WhenBothSourcesSpanPages()
    {
        // Arrange
        var baseline = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var from = baseline.AddDays(-1);
        var to = baseline.AddMinutes(1);
        var laterJob = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ThirdJob",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            CreatedAt = baseline.AddMinutes(-4),
            CompletedAt = baseline.AddMinutes(-4)
        };
        var laterAudit = new AuditRecord
        {
            Id = Guid.NewGuid(),
            Category = "Content",
            Operation = "Update",
            Timestamp = baseline.AddMinutes(-5)
        };
        _jobExecutions
            .Setup(repository => repository.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.ExecutionTimestamp != null &&
                    query.ExecutionTimestamp.Min == from &&
                    query.ExecutionTimestamp.Max == to),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 6),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        Id = Guid.NewGuid(),
                        JobType = "FirstJob",
                        Scope = JobScope.System,
                        Status = JobExecutionStatus.Completed,
                        CreatedAt = baseline,
                        CompletedAt = baseline
                    },
                    new JobExecution
                    {
                        Id = Guid.NewGuid(),
                        JobType = "SecondJob",
                        Scope = JobScope.System,
                        Status = JobExecutionStatus.Completed,
                        CreatedAt = baseline.AddMinutes(-2),
                        CompletedAt = baseline.AddMinutes(-2)
                    },
                    laterJob
                ],
                TotalItems = 3
            });
        _auditRecords
            .Setup(repository => repository.QueryAsync(
                It.Is<AuditRecordQuery>(query =>
                    query.Timestamp != null &&
                    query.Timestamp.Min == from &&
                    query.Timestamp.Max == to),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 6),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<AuditRecord>
            {
                Items =
                [
                    new AuditRecord
                    {
                        Id = Guid.NewGuid(),
                        Category = "Content",
                        Operation = "Update",
                        Timestamp = baseline.AddMinutes(-1)
                    },
                    new AuditRecord
                    {
                        Id = Guid.NewGuid(),
                        Category = "Content",
                        Operation = "Update",
                        Timestamp = baseline.AddMinutes(-3)
                    },
                    laterAudit
                ],
                TotalItems = 3
            });

        // Act
        var page = await _subject.QueryAsync(new AuditHistoryQuery
        {
            Paging = new PagingParameter { PageNumber = 3, PageSize = 2 },
            From = from,
            To = to
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.TotalItems, Is.EqualTo(6));
            Assert.That(page.Items.Select(entry => entry.Id), Is.EqualTo([laterJob.Id, laterAudit.Id]));
        }

        _jobExecutions.Verify(
            repository => repository.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _auditRecords.Verify(
            repository => repository.QueryAsync(
                It.IsAny<AuditRecordQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyMatchingJobsAndDropsAudits_WhenStatusFiltered()
    {
        // Arrange
        var failed = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ContentMetadataRefreshItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Failed,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(q => q.Status == JobExecutionStatus.Failed),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [failed], TotalItems = 1 });
        _auditRecords
            .Setup(r => r.QueryAsync(
                It.IsAny<AuditRecordQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<AuditRecord>
            {
                Items = [
                    new AuditRecord
                    {
                        Id = Guid.NewGuid(),
                        Category = "Content",
                        Operation = "Update",
                        Timestamp = DateTime.UtcNow
                    }
                ],
                TotalItems = 1
            });

        // Act
        var page = await _subject.QueryAsync(new AuditHistoryQuery
        {
            Paging = new PagingParameter { PageNumber = 1, PageSize = 25 },
            Status = "Failed"
        }).ConfigureAwait(false);

        // Assert
        var items = page.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Kind, Is.EqualTo(AuditHistoryEntryKind.JobExecution));
            Assert.That(items[0].Status, Is.EqualTo(JobExecutionStatus.Failed.ToString()));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyJobs_WhenKindFilteredToJobExecution()
    {
        // Arrange
        var job = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Completed,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution> { Items = [job], TotalItems = 1 });

        // Act
        var page = await _subject.QueryAsync(new AuditHistoryQuery
        {
            Paging = new PagingParameter { PageNumber = 1, PageSize = 25 },
            Kind = AuditHistoryEntryKind.JobExecution
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Select(e => e.Kind), Is.All.EqualTo(AuditHistoryEntryKind.JobExecution));
            Assert.That(page.TotalItems, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyTheAuditsOfTheExecution_WhenJobExecutionIdFiltered()
    {
        // Arrange
        var jobExecutionId = Guid.NewGuid();
        var written = new AuditRecord
        {
            Id = Guid.NewGuid(),
            Category = "Content",
            Operation = "Update",
            JobExecutionId = jobExecutionId,
            Timestamp = DateTime.UtcNow
        };
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items =
                [
                    new JobExecution
                    {
                        Id = jobExecutionId,
                        JobType = "ReleaseSearchItem",
                        Scope = JobScope.Content,
                        Status = JobExecutionStatus.Completed,
                        CreatedAt = DateTime.UtcNow,
                        CompletedAt = DateTime.UtcNow
                    }
                ],
                TotalItems = 1
            });
        _auditRecords
            .Setup(r => r.QueryAsync(
                It.Is<AuditRecordQuery>(query => query.JobExecutionId == jobExecutionId),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<AuditRecord> { Items = [written], TotalItems = 1 });

        // Act
        var page = await _subject.QueryAsync(new AuditHistoryQuery
        {
            Paging = new PagingParameter { PageNumber = 1, PageSize = 25 },
            JobExecutionId = jobExecutionId
        }).ConfigureAwait(false);

        // Assert
        var items = page.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Id, Is.EqualTo(written.Id));
            Assert.That(items[0].Kind, Is.EqualTo(AuditHistoryEntryKind.AuditRecord));
            Assert.That(items[0].JobExecutionId, Is.EqualTo(jobExecutionId));
        }

        _jobExecutions.Verify(
            repository => repository.QueryAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task QueryAsync_IncludesScopedAndEventTriggeredImportJobs_WhenContentIdFiltered()
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var downloadId = Guid.NewGuid();
        var firstUnrelatedDownloadId = Guid.NewGuid();
        var secondUnrelatedDownloadId = Guid.NewGuid();
        var baseline = DateTime.UtcNow;
        var downloadIdEventContextKey = "download.id".ToEventContextKey();
        var parameterImportJob = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Failed,
            Parameters = new() { ["downloadId"] = downloadId.ToString("D") },
            CreatedAt = baseline.AddMinutes(-2),
            CompletedAt = baseline.AddMinutes(-2),
            StatusMessage = "The output path does not exist."
        };
        var eventTriggeredImportJob = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Failed,
            EventTriggerId = Guid.NewGuid(),
            TriggerContext = new() { [downloadIdEventContextKey] = downloadId.ToString("D") },
            CreatedAt = baseline.AddMinutes(-1),
            CompletedAt = baseline.AddMinutes(-1),
            StatusMessage = "The output path does not exist."
        };
        var contentAudit = new AuditRecord
        {
            Id = Guid.NewGuid(),
            EntityId = contentId.ToString("D"),
            EntityType = nameof(ContentHierarchyItem),
            Category = "Content",
            Operation = "Update",
            Timestamp = baseline.AddMinutes(-4)
        };
        _downloads
            .SetupSequence(r => r.QueryAsync(
                It.Is<DownloadQuery>(query =>
                    query.ContentHierarchyItemIds != null &&
                    query.ContentHierarchyItemIds.SequenceEqual(new[] { contentId })),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Download>
            {
                Items =
                [
                    new Download
                    {
                        Id = firstUnrelatedDownloadId,
                        DownloadClientId = Guid.NewGuid(),
                        ExternalId = "download-1",
                        Title = "The Matrix",
                        Status = DownloadStatus.Imported,
                        CreatedAt = baseline.AddMinutes(-6)
                    },
                    new Download
                    {
                        Id = secondUnrelatedDownloadId,
                        DownloadClientId = Guid.NewGuid(),
                        ExternalId = "download-2",
                        Title = "The Matrix Reloaded",
                        Status = DownloadStatus.Imported,
                        CreatedAt = baseline.AddMinutes(-5)
                    }
                ]
            })
            .ReturnsAsync(new PagedList<Download>
            {
                Items =
                [
                    new Download
                    {
                        Id = downloadId,
                        DownloadClientId = Guid.NewGuid(),
                        ExternalId = "download-3",
                        Title = "The Matrix Revolutions",
                        Status = DownloadStatus.Failed,
                        CreatedAt = baseline.AddMinutes(-4)
                    }
                ]
            });
        _jobExecutions
            .Setup(r => r.QueryAsync(
                It.Is<JobExecutionQuery>(query =>
                    query.ParameterValueReferences != null &&
                    query.ParameterValueReferences.Contains(contentId.ToString("D")) &&
                    query.ParameterValueReferences.Contains(downloadId.ToString("D"))),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items = [eventTriggeredImportJob, parameterImportJob],
                TotalItems = 3
            });
        _auditRecords
            .Setup(r => r.QueryAsync(
                It.IsAny<AuditRecordQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<AuditRecord> { Items = [contentAudit], TotalItems = 1 });

        // Act
        var page = await _subject.QueryAsync(new AuditHistoryQuery
        {
            Paging = new PagingParameter { PageNumber = 1, PageSize = 2 },
            ContentId = contentId
        }).ConfigureAwait(false);

        // Assert
        var entries = page.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries.Select(entry => entry.Id), Does.Contain(parameterImportJob.Id));
            Assert.That(entries.Select(entry => entry.Id), Does.Contain(eventTriggeredImportJob.Id));
            Assert.That(
                entries.Single(entry => entry.Id == parameterImportJob.Id).EntityId,
                Is.EqualTo(downloadId.ToString("D")));
            Assert.That(
                entries.Single(entry => entry.Id == eventTriggeredImportJob.Id).EntityId,
                Is.EqualTo(downloadId.ToString("D")));
            Assert.That(
                entries.Single(entry => entry.Id == eventTriggeredImportJob.Id).Detail,
                Is.EqualTo("The output path does not exist."));
            Assert.That(page.TotalItems, Is.EqualTo(4));
        }

        _auditRecords.Verify(
            records => records.QueryAsync(
                It.Is<AuditRecordQuery>(query => query.EntityId == contentId.ToString("D")),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _downloads.Verify(
            downloads => downloads.QueryAsync(
                It.Is<DownloadQuery>(query =>
                    query.ContentHierarchyItemIds != null &&
                    query.ContentHierarchyItemIds.SequenceEqual(new[] { contentId })),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 2),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _downloads.Verify(
            downloads => downloads.QueryAsync(
                It.Is<DownloadQuery>(query =>
                    query.ContentHierarchyItemIds != null &&
                    query.ContentHierarchyItemIds.SequenceEqual(new[] { contentId })),
                It.Is<PagingParameter>(paging => paging.PageNumber == 2 && paging.PageSize == 2),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task QueryAsync_ReturnsContentAudits_WhenAuditOnlyAndDownloadScopeFails()
    {
        // Arrange
        var contentId = Guid.NewGuid();
        var audit = new AuditRecord
        {
            Id = Guid.NewGuid(),
            EntityId = contentId.ToString("D"),
            EntityType = nameof(ContentHierarchyItem),
            Category = "Content",
            Operation = "Update",
            Timestamp = DateTime.UtcNow
        };
        _downloads
            .Setup(r => r.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Download lookup failed."));
        _auditRecords
            .Setup(r => r.QueryAsync(
                It.Is<AuditRecordQuery>(query => query.EntityId == contentId.ToString("D")),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<AuditRecord> { Items = [audit], TotalItems = 1 });

        // Act
        var page = await _subject.QueryAsync(new AuditHistoryQuery
        {
            Paging = new PagingParameter { PageNumber = 1, PageSize = 25 },
            ContentId = contentId,
            Kind = AuditHistoryEntryKind.AuditRecord
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Select(entry => entry.Id), Is.EqualTo([audit.Id]));
            Assert.That(page.TotalItems, Is.EqualTo(1));
        }

        _downloads.Verify(
            downloads => downloads.QueryAsync(
                It.IsAny<DownloadQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task QueryAsync_ReturnsTheRequestedPage_WhenOnlyOneSourceIsIncluded()
    {
        // Arrange
        var baseline = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var first = new AuditRecord
        {
            Id = Guid.NewGuid(),
            Category = "Content",
            Operation = "Update",
            Timestamp = baseline
        };
        var second = new AuditRecord
        {
            Id = Guid.NewGuid(),
            Category = "Content",
            Operation = "Update",
            Timestamp = baseline.AddMinutes(-1)
        };
        var third = new AuditRecord
        {
            Id = Guid.NewGuid(),
            Category = "Content",
            Operation = "Update",
            Timestamp = baseline.AddMinutes(-2)
        };
        var fourth = new AuditRecord
        {
            Id = Guid.NewGuid(),
            Category = "Content",
            Operation = "Update",
            Timestamp = baseline.AddMinutes(-3)
        };
        _auditRecords
            .Setup(r => r.QueryAsync(
                It.IsAny<AuditRecordQuery>(),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 4),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<AuditRecord>
            {
                Items = [first, second, third, fourth],
                TotalItems = 4
            });

        // Act
        var page = await _subject.QueryAsync(new AuditHistoryQuery
        {
            Paging = new PagingParameter { PageNumber = 2, PageSize = 2 },
            Kind = AuditHistoryEntryKind.AuditRecord
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.TotalItems, Is.EqualTo(4));
            Assert.That(page.Items.Select(entry => entry.Id), Is.EqualTo([third.Id, fourth.Id]));
        }

        _auditRecords.Verify(
            records => records.QueryAsync(
                It.IsAny<AuditRecordQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task QueryAsync_PassesTheRangeToTheStore_WhenTimestampRangeIsSpecified()
    {
        // Arrange
        var baseline = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var inRange = new AuditRecord
        {
            Id = Guid.NewGuid(),
            Category = "Content",
            Operation = "Update",
            Timestamp = baseline.AddHours(-1)
        };
        var from = baseline.AddDays(-1);
        var to = baseline.AddMinutes(1);
        _auditRecords
            .Setup(r => r.QueryAsync(
                It.Is<AuditRecordQuery>(query =>
                    query.Timestamp != null &&
                    query.Timestamp.Min == from &&
                    query.Timestamp.Max == to),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<AuditRecord> { Items = [inRange], TotalItems = 1 });

        // Act
        var page = await _subject.QueryAsync(new AuditHistoryQuery
        {
            Paging = new PagingParameter { PageNumber = 1, PageSize = 25 },
            Kind = AuditHistoryEntryKind.AuditRecord,
            From = from,
            To = to
        }).ConfigureAwait(false);

        // Assert
        var items = page.Items.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Id, Is.EqualTo(inRange.Id));
        }
    }
}
