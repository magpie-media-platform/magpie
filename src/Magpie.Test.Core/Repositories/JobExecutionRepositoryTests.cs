using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Fulfillment;
using Magpie.Core.Jobs.Metadata;
using Magpie.Core.Jobs.Runtime;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Models.Paging;
using Magpie.Sdk.Events;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class JobExecutionRepositoryTests
{
    private static readonly DateTimeOffset EventNow = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private List<MagpieEvent> _events = null!;
    private ILoggerFactory _loggerFactory = null!;
    private IJobExecutionRepository _subject = null!;
    private FakeTimeProvider _timeProvider = null!;
    private ITransactionManager _transactionManager = null!;
    private Guid _userId;

    private static IEnumerable<TestCaseData> RefreshScopes()
    {
        yield return new TestCaseData(
            new Dictionary<string, string> { ["event.context.content.hierarchyItemId"] = "item-b" },
            new Dictionary<string, string> { ["event.context.content.hierarchyItemId"] = "item-c" },
            null,
            "Original reason",
            1).SetArgDisplayNames("item-b", "item-c");
        yield return new TestCaseData(
            new Dictionary<string, string> { ["event.context.content.hierarchyItemId"] = "item-b" },
            new Dictionary<string, string> { ["event.context.content.hierarchyItemId"] = "item-b" },
            "item-b",
            "Updated reason",
            0).SetArgDisplayNames("item-b", "item-b");
        yield return new TestCaseData(
            new Dictionary<string, string> { ["event.context.content.hierarchyItemId"] = "item-b" },
            new Dictionary<string, string>(),
            null,
            "Original reason",
            1).SetArgDisplayNames("item-b", "absent");
        yield return new TestCaseData(
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["event.context.content.hierarchyItemId"] = "item-c" },
            null,
            "Original reason",
            0).SetArgDisplayNames("absent", "item-c");
        yield return new TestCaseData(
            new Dictionary<string, string> { ["event.context.content.hierarchyItemId"] = "" },
            new Dictionary<string, string> { ["event.context.content.hierarchyItemId"] = "item-c" },
            "",
            "Original reason",
            0).SetArgDisplayNames("empty", "item-c");
    }

    [Test]
    public async Task TryDeferRunningAsync_PreservesAttemptAndDelaysClaims_WhenUpstreamThrottles()
    {
        // Arrange
        var execution = await _subject.CreateAsync(new JobExecution
        {
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "content=throttled",
            Attempt = 3
        }, null).ConfigureAwait(false);
        await _subject.TryStartAsync(execution.Id, "owner").ConfigureAwait(false);
        var auditsBefore = await _context.AuditRecords.CountAsync().ConfigureAwait(false);

        // Act
        var deferred = await _subject.TryDeferRunningAsync(execution.Id, "owner",
            new DateTime(2026, 7, 17, 12, 2, 0, DateTimeKind.Utc)).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var queued = await _subject.GetByIdAsync(execution.Id).ConfigureAwait(false);
        var earlyClaim = await _subject.TryClaimAsync(execution.Id, "other").ConfigureAwait(false);
        var earlyStart = await _subject.TryStartAsync(execution.Id, "other").ConfigureAwait(false);
        _timeProvider.Advance(TimeSpan.FromMinutes(2));
        var auditsAfterDefer = await _context.AuditRecords.CountAsync().ConfigureAwait(false);
        var dueClaim = await _subject.TryClaimAsync(execution.Id, "other").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deferred, Is.True);
            Assert.That(queued!.Status, Is.EqualTo(JobExecutionStatus.Queued));
            Assert.That(queued.Attempt, Is.EqualTo(3));
            Assert.That(queued.NodeId, Is.Null);
            Assert.That(queued.CompletedAt, Is.Null);
            Assert.That(queued.StatusMessage, Does.Contain("incomplete"));
            Assert.That(queued.NotBeforeAt, Is.EqualTo(new DateTime(2026, 7, 17, 12, 2, 0, DateTimeKind.Utc)));
            Assert.That(earlyClaim, Is.Null);
            Assert.That(earlyStart, Is.False);
            Assert.That(dueClaim, Is.Not.Null);
            Assert.That(dueClaim!.Status, Is.EqualTo(JobExecutionStatus.Running));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(auditsAfterDefer, Is.EqualTo(auditsBefore));
        }
    }

    [TestCase("true", null)]
    [TestCase("false", "The work is incomplete while its upstream is throttled.")]
    public async Task CreateAsync_ClearsTheThrottleMarkerOnlyForAForcedSearch_WhenTheSearchJoinsADeferredExecution(
        string force,
        string? expectedStatusMessage)
    {
        // Arrange
        var execution = await _subject.CreateAsync(new JobExecution
        {
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "content=deferred",
            Parameters = new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "deferred",
                ["force"] = "false",
            },
        }, null).ConfigureAwait(false);
        await _subject.TryStartAsync(execution.Id, "owner").ConfigureAwait(false);
        await _subject.TryDeferRunningAsync(execution.Id, "owner",
            new DateTime(2026, 7, 17, 12, 2, 0, DateTimeKind.Utc)).ConfigureAwait(false);
        _timeProvider.Advance(TimeSpan.FromMinutes(3));

        // Act
        var joined = await _subject.CreateAsync(new JobExecution
        {
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Immediate,
            LockPartition = "content=deferred",
            Parameters = new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "deferred",
                ["force"] = force,
            },
        }, null).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var persisted = await _subject.GetByIdAsync(execution.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(joined.Id, Is.EqualTo(execution.Id));
            Assert.That(persisted!.Parameters["force"], Is.EqualTo(force));
            Assert.That(persisted.StatusMessage, Is.EqualTo(expectedStatusMessage));
        }
    }

    [Test]
    public async Task ExecuteAsync_ResumesPersistedWorkWithoutFailure_WhenItsUpstreamCooldownEnds()
    {
        // Arrange
        var execution = await _subject.CreateAsync(new JobExecution
        {
            JobType = "test-cooldown",
            Status = JobExecutionStatus.Queued,
            Attempt = 3,
            LockPartition = "throttled",
            ExecutionMode = JobExecutionMode.Queue
        }, null).ConfigureAwait(false);
        var healthy = await _subject.CreateAsync(new JobExecution
        {
            JobType = "test-cooldown",
            Status = JobExecutionStatus.Queued,
            LockPartition = "healthy",
            ExecutionMode = JobExecutionMode.Queue
        }, null).ConfigureAwait(false);
        var services = new ServiceCollection();
        services.AddSingleton(_subject);
        services.AddSingleton(_transactionManager);
        services.AddSingleton(Mock.Of<IJobManager>());
        services.AddSingleton(Mock.Of<IJobRetryPolicy>());
        using var provider = services.BuildServiceProvider();
        var spec = new JobSpec
        {
            JobType = "test-cooldown",
            DisplayName = "test-cooldown",
            Category = "Test",
            Scope = JobScope.System,
            OnDemand = true,
            FailurePolicy = JobFailurePolicy.Fail
        };
        var registry = new Mock<IJobRegistry>();
        registry.Setup(value => value.TryGetJobSpec("test-cooldown", out spec)).Returns(true);
        var handler = new Mock<IJob>();
        handler.Setup(value => value.ExecuteAsync(It.Is<JobContext>(context => context.ExecutionId == healthy.Id),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        handler.SetupSequence(value => value.ExecuteAsync(
                It.Is<JobContext>(context => context.ExecutionId == execution.Id), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UpstreamThrottledException(Now.AddMinutes(2)))
            .Returns(Task.CompletedTask);
        var job = handler.Object;
        var scope = new Mock<IJobExecutionScope>();
        scope.Setup(value => value.TryGetHandler(spec, out job)).Returns(true);
        var scopes = new Mock<IJobExecutionScopeFactory>();
        scopes.Setup(value => value.CreateScope(It.IsAny<Guid>())).Returns(scope.Object);
        using var runtime = new DefaultJobRuntime(scopes.Object, provider.GetRequiredService<IServiceScopeFactory>(),
            registry.Object, new MagpieHostEnvironment("owner", false, false, "", "", "", "", Now.UtcDateTime),
            Mock.Of<IEventBus>(), new JobShutdownSignal(), _timeProvider, NullLogger<DefaultJobRuntime>.Instance);

        // Act
        await runtime.ExecuteAsync(execution.Id).ConfigureAwait(false);
        var deferred = await _subject.GetByIdAsync(execution.Id).ConfigureAwait(false);
        await runtime.ExecuteAsync(execution.Id).ConfigureAwait(false);
        await runtime.ExecuteAsync(healthy.Id).ConfigureAwait(false);
        var completedHealthy = await _subject.GetByIdAsync(healthy.Id).ConfigureAwait(false);
        _timeProvider.Advance(TimeSpan.FromMinutes(3));
        await runtime.ExecuteAsync(execution.Id).ConfigureAwait(false);
        var recovered = await _subject.GetByIdAsync(execution.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deferred!.Status, Is.EqualTo(JobExecutionStatus.Queued));
            Assert.That(deferred.NodeId, Is.Null);
            Assert.That(deferred.Attempt, Is.EqualTo(3));
            Assert.That(completedHealthy!.Status, Is.EqualTo(JobExecutionStatus.Completed));
            Assert.That(recovered!.Status, Is.EqualTo(JobExecutionStatus.Completed));
            Assert.That(recovered.Attempt, Is.EqualTo(3));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(_events.Any(value => value.Name.Contains("failed", StringComparison.Ordinal)), Is.False);
            handler.Verify(value => value.ExecuteAsync(
                It.Is<JobContext>(context => context.ExecutionId == execution.Id), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
    }

    [TestCase("other", false)]
    [TestCase("owner", true)]
    public async Task TryDeferRunningAsync_PreservesCancellationAndOwnership_WhenDeferralLosesRace(string node,
        bool cancel)
    {
        // Arrange
        var execution = await _subject.CreateAsync(new JobExecution
        {
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Queued,
            LockPartition = "content=throttled"
        }, null).ConfigureAwait(false);
        await _subject.TryStartAsync(execution.Id, "owner").ConfigureAwait(false);
        if (cancel)
        {
            await _subject.CancelAsync(execution.Id).ConfigureAwait(false);
        }

        // Act
        var deferred = await _subject.TryDeferRunningAsync(execution.Id, node,
            new DateTime(2026, 7, 17, 12, 2, 0, DateTimeKind.Utc)).ConfigureAwait(false);

        // Assert
        Assert.That(deferred, Is.False);
    }

    [TestCase(null, "Original reason", "target")]
    [TestCase("Updated reason", "Updated reason", "target")]
    [TestCase("Updated reason", "Original reason", "other-target")]
    public async Task CreateAsync_PreservesRetryState_WhenQueuedReasonIsCoalesced(string? incomingReason,
        string expectedReason, string incomingTarget)
    {
        // Arrange
        var original = await _subject.CreateAsync(
            new JobExecution
            {
                JobType = "ReleaseDownloadItem",
                Status = JobExecutionStatus.Queued,
                LockPartition = "release=1",
                NotBeforeAt = Now.UtcDateTime.AddMinutes(10),
                StatusMessage = "Original reason",
                Parameters = new Dictionary<string, string>
                {
                    ["retryContext"] = "original-context",
                    ["contentHierarchyItemId"] = "target"
                }
            },
            null).ConfigureAwait(false);

        // Act
        var coalesced = await _subject.CreateAsync(
            new JobExecution
            {
                JobType = "ReleaseDownloadItem",
                Status = JobExecutionStatus.Queued,
                LockPartition = "release=1",
                NotBeforeAt = Now.UtcDateTime.AddMinutes(20),
                StatusMessage = incomingReason,
                Parameters = new Dictionary<string, string>
                {
                    ["retryContext"] = "must-not-replace",
                    ["contentHierarchyItemId"] = incomingTarget
                }
            },
            null).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var persisted = await _subject.GetByIdAsync(original.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(coalesced.Id, Is.EqualTo(original.Id));
            Assert.That(persisted!.StatusMessage, Is.EqualTo(expectedReason));
            Assert.That(persisted.Status, Is.EqualTo(JobExecutionStatus.Queued));
            Assert.That(persisted.NotBeforeAt, Is.EqualTo(new DateTime(2026, 7, 17, 12, 10, 0, DateTimeKind.Utc)));
            Assert.That(persisted.Parameters["retryContext"], Is.EqualTo("original-context"));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
        }
    }

    [TestCase("ReleaseSearchItem", "false", "true", "true", 1)]
    [TestCase("ReleaseSearchItem", "true", "false", "true", 0)]
    [TestCase("ReleaseSearchItem", "false", "false", "false", 0)]
    [TestCase("ReleaseSearchItem", "true", "true", "true", 0)]
    [TestCase("RootFolderContentDiscovery", "false", "true", "false", 0)]
    public async Task CreateAsync_KeepsTheForcedSearch_WhenAReleaseSearchCoalescesOntoAQueuedSearch(
        string jobType,
        string queuedForce,
        string incomingForce,
        string expectedForce,
        int expectedForceUpgrades)
    {
        // Arrange
        var original = await _subject.CreateAsync(new JobExecution
        {
            JobType = jobType,
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "target",
            Parameters = new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "target",
                ["force"] = queuedForce
            }
        }, null).ConfigureAwait(false);

        // Act
        var coalesced = await _subject.CreateAsync(new JobExecution
        {
            JobType = jobType,
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Immediate,
            LockPartition = "target",
            Parameters = new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "target",
                ["force"] = incomingForce
            }
        }, null).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var persisted = await _subject.GetByIdAsync(original.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(coalesced.Id, Is.EqualTo(original.Id));
            Assert.That(coalesced.Parameters["force"], Is.EqualTo(expectedForce));
            Assert.That(persisted!.Parameters["force"], Is.EqualTo(expectedForce));
            Assert.That(persisted.Parameters["contentHierarchyItemId"], Is.EqualTo("target"));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(
                await _context.AuditRecords
                    .Where(record => record.Operation == "Coalesce")
                    .Select(record => record.Detail)
                    .ToListAsync()
                    .ConfigureAwait(false),
                Has.Exactly(expectedForceUpgrades).EndsWith("; set force to true"));
        }
    }

    [TestCase("RootFolderContentDiscovery", null, 1)]
    [TestCase("MediaInfoExtractScan", "item-a", 0)]
    public async Task CreateAsync_WidensOnlyADiscovery_WhenAnotherTargetCoalescesOntoItsQueuedRun(
        string jobType,
        string? expectedTarget,
        int expectedWidenings)
    {
        // Arrange
        var original = await _subject.CreateAsync(new JobExecution
        {
            JobType = jobType,
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "shared",
            Parameters = new Dictionary<string, string> { ["contentHierarchyItemId"] = "item-a" }
        }, null).ConfigureAwait(false);

        // Act
        var coalesced = await _subject.CreateAsync(new JobExecution
        {
            JobType = jobType,
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "shared",
            Parameters = new Dictionary<string, string> { ["contentHierarchyItemId"] = "item-b" }
        }, null).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var persisted = await _subject.GetByIdAsync(original.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(coalesced.Id, Is.EqualTo(original.Id));
            Assert.That(persisted!.Parameters.GetValueOrDefault("contentHierarchyItemId"), Is.EqualTo(expectedTarget));
            Assert.That(
                await _context.AuditRecords
                    .Where(record => record.Operation == "Coalesce")
                    .Select(record => record.Detail)
                    .ToListAsync()
                    .ConfigureAwait(false),
                Has.Exactly(expectedWidenings).EndsWith("; removed contentHierarchyItemId"));
        }
    }

    [TestCaseSource(nameof(RefreshScopes))]
    public async Task CreateAsync_KeepsTheEventTargetOnlyForTheSameItem_WhenARefreshCoalescesOntoAQueuedRefresh(
        Dictionary<string, string> queuedContext,
        Dictionary<string, string> incomingContext,
        string? expectedTarget,
        string expectedReason,
        int expectedWidenings)
    {
        // Arrange
        var original = await _subject.CreateAsync(
            new JobExecution
            {
                JobType = "MediaServerRefresh",
                Status = JobExecutionStatus.Queued,
                ExecutionMode = JobExecutionMode.Queue,
                LockPartition = "type:MediaServerRefresh",
                StatusMessage = "Original reason",
                TriggerContext = queuedContext
            },
            null).ConfigureAwait(false);

        // Act
        var coalesced = await _subject.CreateAsync(
            new JobExecution
            {
                JobType = "MediaServerRefresh",
                Status = JobExecutionStatus.Queued,
                ExecutionMode = JobExecutionMode.Queue,
                LockPartition = "type:MediaServerRefresh",
                StatusMessage = "Updated reason",
                TriggerContext = incomingContext
            },
            null).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var persisted = await _subject.GetByIdAsync(original.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(coalesced.Id, Is.EqualTo(original.Id));
            Assert.That(
                persisted!.TriggerContext.GetValueOrDefault("event.context.content.hierarchyItemId"),
                Is.EqualTo(expectedTarget));
            Assert.That(persisted.StatusMessage, Is.EqualTo(expectedReason));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(
                await _context.AuditRecords
                    .Where(record => record.Operation == "Coalesce")
                    .Select(record => record.Detail)
                    .ToListAsync()
                    .ConfigureAwait(false),
                Has.Exactly(expectedWidenings).EndsWith("; removed contentHierarchyItemId"));
        }
    }

    [TestCase("true", "item-b", "true", null, "; set flag to true, removed scope")]
    [TestCase("false", "item-a", "false", "item-a", "; updated queue eligibility or the waiting reason")]
    [TestCase("true", null, "true", null, "; set flag to true, removed scope")]
    [TestCase("false", "ITEM-A", "false", "item-a", "; updated queue eligibility or the waiting reason")]
    public async Task CreateAsync_MergesTheSpecsCoalesceFields_WhenAnyJobTypeCoalescesOntoAQueuedRun(
        string incomingFlag,
        string? incomingScope,
        string expectedFlag,
        string? expectedScope,
        string expectedAuditEnding)
    {
        // Arrange
        IJobExecutionRepository subject = new JobExecutionRepository(
            _context,
            new AuditRecordRepository(
                _context,
                Mock.Of<IEventBus>(),
                NullLogger<AuditRecordRepository>.Instance,
                _transactionManager,
                Mock.Of<IJobExecutionScopeFactory>(),
                _timeProvider),
            _transactionManager,
            Mock.Of<IEventBus>(),
            NullLogger<JobExecutionRepository>.Instance,
            _timeProvider,
            new JobRegistry(
                [
                    new(
                        new JobSpec
                        {
                            JobType = "MergeJob",
                            DisplayName = "Merge job",
                            Category = "Test",
                            Scope = JobScope.System,
                            CoalesceTrueWinsFields = ["flag"],
                            CoalesceWideningFields = ["scope"]
                        },
                        typeof(IJob))
                ],
                NullLogger<JobRegistry>.Instance));
        var original = await subject.CreateAsync(new JobExecution
        {
            JobType = "MergeJob",
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "shared",
            Parameters = new Dictionary<string, string> { ["flag"] = "false", ["scope"] = "item-a" }
        }, null).ConfigureAwait(false);
        var incomingParameters = new Dictionary<string, string> { ["flag"] = incomingFlag };
        if (incomingScope is not null)
        {
            incomingParameters["scope"] = incomingScope;
        }

        // Act
        var coalesced = await subject.CreateAsync(new JobExecution
        {
            JobType = "MergeJob",
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Immediate,
            LockPartition = "shared",
            Parameters = incomingParameters,
            StatusMessage = "Waiting for capacity."
        }, null).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var persisted = await subject.GetByIdAsync(original.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(coalesced.Id, Is.EqualTo(original.Id));
            Assert.That(persisted!.Parameters["flag"], Is.EqualTo(expectedFlag));
            Assert.That(persisted.Parameters.GetValueOrDefault("scope"), Is.EqualTo(expectedScope));
            Assert.That(
                await _context.AuditRecords
                    .Where(record => record.Operation == "Coalesce")
                    .Select(record => record.Detail)
                    .ToListAsync()
                    .ConfigureAwait(false),
                Has.Exactly(1).EndsWith(expectedAuditEnding));
        }
    }

    [TestCase("true", null, false, false)]
    [TestCase("false", "Waiting for capacity.", false, false)]
    [TestCase("false", null, true, true)]
    public async Task CreateAsync_EnqueuesANewExecution_WhenTheQueuedExecutionStartsBeforeTheCoalesceWrites(
        string incomingForce,
        string? incomingStatusMessage,
        bool queuedDeferred,
        bool incomingDeferred)
    {
        // Arrange
        var claim = new Mock<DbCommandInterceptor> { CallBase = true };
        claim
            .Setup(interceptor => interceptor.NonQueryExecutingAsync(
                It.Is<DbCommand>(command =>
                    command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal) &&
                    command.CommandText.Contains("\"StatusMessage\"", StringComparison.Ordinal)),
                It.IsAny<CommandEventData>(),
                It.IsAny<InterceptionResult<int>>(),
                It.IsAny<CancellationToken>()))
            .Callback((DbCommand command, CommandEventData _, InterceptionResult<int> _, CancellationToken _) =>
            {
                using var start = command.Connection!.CreateCommand();
                start.Transaction = command.Transaction;
                start.CommandText = "UPDATE job_execution SET Status = 2 WHERE Status = 1";
                start.ExecuteNonQuery();
            })
            .Returns((DbCommand _, CommandEventData _, InterceptionResult<int> result, CancellationToken _) =>
                ValueTask.FromResult(result));
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        using var context = new MagpieDbContext(options.AddInterceptors(claim.Object).Options);
        await context.Database.OpenConnectionAsync().ConfigureAwait(false);
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
        var transactions = new TransactionManager(context, NullLogger<TransactionManager>.Instance);
        IJobExecutionRepository subject = new JobExecutionRepository(
            context,
            new AuditRecordRepository(
                context,
                Mock.Of<IEventBus>(),
                NullLogger<AuditRecordRepository>.Instance,
                transactions,
                Mock.Of<IJobExecutionScopeFactory>(),
                _timeProvider),
            transactions,
            Mock.Of<IEventBus>(),
            NullLogger<JobExecutionRepository>.Instance,
            _timeProvider,
            new JobRegistry(
                [new(ReleaseSearchItemJob.Spec, typeof(ReleaseSearchItemJob))],
                NullLogger<JobRegistry>.Instance));
        var original = await subject.CreateAsync(new JobExecution
        {
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "target",
            NotBeforeAt = queuedDeferred ? Now.UtcDateTime.AddMinutes(10) : null,
            StatusMessage = "Queued.",
            Parameters = new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "target",
                ["force"] = "false"
            }
        }, null).ConfigureAwait(false);

        // Act
        var result = await subject.CreateAsync(new JobExecution
        {
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "target",
            NotBeforeAt = incomingDeferred ? Now.UtcDateTime.AddMinutes(5) : null,
            StatusMessage = incomingStatusMessage,
            Parameters = new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "target",
                ["force"] = incomingForce
            }
        }, null).ConfigureAwait(false);
        context.ChangeTracker.Clear();
        var started = await context.JobExecutions
            .AsNoTracking()
            .SingleAsync(execution => execution.Id == original.Id)
            .ConfigureAwait(false);
        var queued = await context.JobExecutions
            .AsNoTracking()
            .SingleAsync(execution => execution.Id != original.Id)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Id, Is.EqualTo(queued.Id));
            Assert.That(started.Status, Is.EqualTo(JobExecutionStatus.Running));
            Assert.That(started.Parameters["force"], Is.EqualTo("false"));
            Assert.That(started.StatusMessage, Is.EqualTo("Queued."));
            Assert.That(started.NotBeforeAt, Is.EqualTo(original.NotBeforeAt));
            Assert.That(queued.Status, Is.EqualTo(JobExecutionStatus.Queued));
            Assert.That(queued.Parameters["force"], Is.EqualTo(incomingForce));
            Assert.That(queued.StatusMessage, Is.EqualTo(incomingStatusMessage));
            Assert.That(
                await context.AuditRecords
                    .CountAsync(record => record.Operation == "Coalesce")
                    .ConfigureAwait(false),
                Is.Zero);
        }
    }

    [Test]
    public async Task CancelAsync_LeavesARunningExecutionAlone_WhenItStartedAfterItWasRead()
    {
        // Arrange
        var queued = await _subject.CreateAsync(new JobExecution
        {
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "target",
            Parameters = new Dictionary<string, string> { ["contentHierarchyItemId"] = "target" }
        }, null).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var read = await _subject.GetByIdAsync(queued.Id).ConfigureAwait(false);
        await _subject.TryClaimAsync(queued.Id, "node-a", CancellationToken.None).ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var result = await _subject.CancelAsync(read!).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var persisted = await _subject.GetByIdAsync(queued.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            Assert.That(persisted!.Status, Is.EqualTo(JobExecutionStatus.Running));
        }
    }

    [TestCase(true, JobExecutionStatus.Cancelled, true)]
    [TestCase(false, JobExecutionStatus.Queued, false)]
    public async Task CancelAsync_CancelsTheQueuedExecution_WhenItIsUnchangedSinceItWasRead(
        bool readAfterTheUpgrade,
        JobExecutionStatus expectedStatus,
        bool expectedCancelled)
    {
        // Arrange
        var queued = await _subject.CreateAsync(new JobExecution
        {
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "target",
            Parameters = new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "target",
                ["force"] = "false"
            }
        }, null).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var readBeforeTheUpgrade = await _subject.GetByIdAsync(queued.Id).ConfigureAwait(false);
        _timeProvider.Advance(TimeSpan.FromTicks(1_234_567));
        await _subject.CreateAsync(new JobExecution
        {
            JobType = "ReleaseSearchItem",
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "target",
            Parameters = new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "target",
                ["force"] = "true"
            }
        }, null).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var read = readAfterTheUpgrade
            ? await _subject.GetByIdAsync(queued.Id).ConfigureAwait(false)
            : readBeforeTheUpgrade;

        // Act
        var result = await _subject.CancelAsync(read!).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        var persisted = await _subject.GetByIdAsync(queued.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result is not null, Is.EqualTo(expectedCancelled));
            Assert.That(persisted!.Status, Is.EqualTo(expectedStatus));
            Assert.That(read!.ModifiedAt is null, Is.EqualTo(!readAfterTheUpgrade));
        }
    }

    [TestCase("sqlite")]
    [TestCase("postgresql")]
    [TestCase("sqlserver")]
    public void Model_MatchesMigrationWithoutDeadline_WhenProviderIsSupported(string provider)
    {
        // Arrange
        using var context = provider switch
        {
            "postgresql" => Magpie.Data.PostgreSQL.MagpieDbContextFactory.Create(
                "Host=127.0.0.1;Database=unused", [], []),
            "sqlserver" => Magpie.Data.SqlServer.MagpieDbContextFactory.Create(
                "Server=127.0.0.1;Database=unused", [], []),
            _ => MagpieDbContextFactory.Create("Data Source=:memory:", [], [])
        };

        // Act
        var pendingChanges = context.Database.HasPendingModelChanges();
        var createScript = context.Database.GenerateCreateScript();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pendingChanges, Is.False);
            Assert.That(createScript, Does.Not.Contain("TimeoutAt"));
        }
    }

    [TestCase(JobExecutionStatus.Running, false)]
    [TestCase(JobExecutionStatus.Running, true)]
    [TestCase(JobExecutionStatus.CancellationRequested, false)]
    [TestCase(JobExecutionStatus.CancellationRequested, true)]
    public async Task TryRecoverInactiveAsync_PreservesExecution_WhenHeartbeatOrOwnerChanges(
        JobExecutionStatus status, bool ownerChanges)
    {
        // Arrange
        var observed = new JobExecution
        {
            JobType = "long-running",
            Status = status,
            NodeId = "old-owner",
            LastHeartbeat = Now.UtcDateTime.AddHours(-1),
            CreatedAt = Now.UtcDateTime.AddHours(-2)
        };
        _context.JobExecutions.Add(observed);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        if (ownerChanges)
        {
            await _context.JobExecutions.Where(item => item.Id == observed.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.NodeId, "new-owner"))
                .ConfigureAwait(false);
        }
        else
        {
            await _subject.TouchHeartbeatAsync(observed.Id, "old-owner").ConfigureAwait(false);
        }

        // Act
        var recovered = await _subject.TryRecoverInactiveAsync(observed, "inactive worker").ConfigureAwait(false);

        // Assert
        var stored = await _subject.GetByIdAsync(observed.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(recovered, Is.False);
            Assert.That(stored!.Status, Is.EqualTo(status));
            Assert.That(_events, Is.Empty);
            Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.EqualTo(0));
        }
    }

    [TestCase(JobExecutionStatus.Running, JobExecutionStatus.Failed, 0)]
    [TestCase(JobExecutionStatus.CancellationRequested, JobExecutionStatus.Cancelled, 1)]
    public async Task TryRecoverInactiveAsync_TransitionsOnce_WhenObservedLeaseIsUnchanged(
        JobExecutionStatus status, JobExecutionStatus expectedStatus, int auditCount)
    {
        // Arrange
        var observed = new JobExecution
        {
            JobType = "inactive",
            Status = status,
            NodeId = "owner",
            LastHeartbeat = Now.UtcDateTime.AddHours(-1),
            CreatedAt = Now.UtcDateTime.AddHours(-2)
        };
        _context.JobExecutions.Add(observed);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        var first = await _subject.TryRecoverInactiveAsync(observed, "inactive worker").ConfigureAwait(false);
        var second = await _subject.TryRecoverInactiveAsync(observed, "inactive worker").ConfigureAwait(false);

        // Assert
        var stored = await _subject.GetByIdAsync(observed.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.False);
            Assert.That(stored!.Status, Is.EqualTo(expectedStatus));
            Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.EqualTo(auditCount));
            Assert.That(_events, Has.Count.EqualTo(auditCount));
        }
    }

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        using var context = new MagpieDbContext(options.UseSqlite(_template).Options);
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
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        _context = new MagpieDbContext(options.UseSqlite(connection, contextOwnsConnection: true).Options);

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

        _events = [];
        var eventBus = new Mock<IEventBus>(MockBehavior.Strict);
        eventBus
            .Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent magpieEvent, CancellationToken _) => _events.Add(magpieEvent))
            .Returns(Task.CompletedTask);

        _loggerFactory = NullLoggerFactory.Instance;
        _timeProvider = new FakeTimeProvider(Now);
        _transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        _subject = new JobExecutionRepository(
            _context,
            new AuditRecordRepository(
                _context,
                Mock.Of<IEventBus>(),
                NullLogger<AuditRecordRepository>.Instance,
                _transactionManager,
                Mock.Of<IJobExecutionScopeFactory>(),
                _timeProvider),
            _transactionManager,
            eventBus.Object,
            NullLogger<JobExecutionRepository>.Instance,
            _timeProvider,
            new JobRegistry(
                [
                    new(DownloadImportItemJob.Spec, typeof(DownloadImportItemJob)),
                    new(MediaInfoExtractScanJob.Spec, typeof(MediaInfoExtractScanJob)),
                    new(MediaServerRefreshJob.Spec, typeof(MediaServerRefreshJob)),
                    new(ReleaseSearchItemJob.Spec, typeof(ReleaseSearchItemJob)),
                    new(RootFolderContentDiscoveryJob.Spec, typeof(RootFolderContentDiscoveryJob))
                ],
                NullLogger<JobRegistry>.Instance));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task CreateAsync_OneDeferredSuccessor_WhenSameJobIsRunning()
    {
        // Arrange
        var running = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadRefresh",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Running,
            LockPartition = "type:DownloadRefresh",
            CreatedAt = Now.UtcDateTime,
        };
        _context.JobExecutions.Add(running);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(running).State = EntityState.Detached;
        var firstRequest = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadRefresh",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "type:DownloadRefresh",
            NotBeforeAt = Now.UtcDateTime.AddSeconds(20),
            ParentExecutionId = running.Id,
        };
        var laterRequest = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadRefresh",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "type:DownloadRefresh",
            NotBeforeAt = Now.UtcDateTime.AddSeconds(30),
            ParentExecutionId = running.Id,
        };

        // Act
        var successor = await _subject.CreateAsync(firstRequest, createdByUserId: null).ConfigureAwait(false);
        var coalesced = await _subject.CreateAsync(laterRequest, createdByUserId: null).ConfigureAwait(false);

        // Assert
        var executions = await _context.JobExecutions
            .AsNoTracking()
            .OrderBy(execution => execution.CreatedAt)
            .ToListAsync()
            .ConfigureAwait(false);
        var persistedSuccessor = executions.Single(execution => execution.Id == successor.Id);
        _timeProvider.Advance(TimeSpan.FromSeconds(21));
        var blockedCandidates = await _subject.GetClaimableCandidatesAsync(
            new PagingParameter { PageNumber = 1, PageSize = 500 }).ConfigureAwait(false);
        await _subject.TryMarkRunningCompletedAsync(running.Id).ConfigureAwait(false);
        var unblockedCandidates = await _subject.GetClaimableCandidatesAsync(
            new PagingParameter { PageNumber = 1, PageSize = 500 }).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(successor.Id, Is.Not.EqualTo(running.Id));
            Assert.That(coalesced.Id, Is.EqualTo(successor.Id));
            Assert.That(executions, Has.Count.EqualTo(2));
            Assert.That(persistedSuccessor.NotBeforeAt, Is.EqualTo(firstRequest.NotBeforeAt));
            Assert.That(persistedSuccessor.ParentExecutionId, Is.EqualTo(running.Id));
            Assert.That(blockedCandidates.Select(candidate => candidate.Id), Does.Not.Contain(successor.Id));
            Assert.That(unblockedCandidates.Select(candidate => candidate.Id), Does.Contain(successor.Id));
        }
    }

    [TestCase(JobExecutionStatus.Queued)]
    [TestCase(JobExecutionStatus.Running)]
    [TestCase(JobExecutionStatus.CancellationRequested)]
    public async Task CreateIfInactiveAsync_PreservesOwner_WhenPartitionHasWork(JobExecutionStatus status)
    {
        // Arrange
        var owner = new JobExecution
        {
            JobType = "DownloadImportItem",
            Status = status,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "type:DownloadImportItem|downloadId=abcdefff-1234-5678-90ab-cdef12345678",
            NotBeforeAt = new DateTime(2026, 7, 17, 12, 30, 0, DateTimeKind.Utc),
            Parameters = new() { ["downloadId"] = "abcdefff-1234-5678-90ab-cdef12345678", ["collisionMode"] = "skip" },
            CreatedAt = new DateTime(2026, 7, 17, 11, 0, 0, DateTimeKind.Utc)
        };
        _context.JobExecutions.Add(owner);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var recovery = new JobExecution
        {
            JobType = "DownloadImportItem",
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "type:DownloadImportItem|downloadId=abcdefff-1234-5678-90ab-cdef12345678",
            Parameters = new()
            {
                ["downloadId"] = "abcdefff-1234-5678-90ab-cdef12345678",
                ["collisionMode"] = "replace"
            }
        };

        // Act
        var result = await _subject.CreateIfInactiveAsync(recovery, _userId).ConfigureAwait(false);

        // Assert
        var persisted = await _context.JobExecutions.AsNoTracking().SingleAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Id, Is.EqualTo(owner.Id));
            Assert.That(persisted.Status, Is.EqualTo(status));
            Assert.That(persisted.NotBeforeAt, Is.EqualTo(new DateTime(2026, 7, 17, 12, 30, 0, DateTimeKind.Utc)));
            Assert.That(persisted.Parameters["collisionMode"], Is.EqualTo("skip"));
            Assert.That(persisted.CreatedAt, Is.EqualTo(new DateTime(2026, 7, 17, 11, 0, 0, DateTimeKind.Utc)));
            Assert.That(persisted.ModifiedAt, Is.Null);
            Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.EqualTo(0));
            Assert.That(_events, Is.Empty);
        }
    }

    [TestCase(JobExecutionStatus.Completed)]
    [TestCase(JobExecutionStatus.Failed)]
    [TestCase(JobExecutionStatus.Cancelled)]
    public async Task CreateIfInactiveAsync_CreatesRecovery_WhenPreviousOwnerIsTerminal(JobExecutionStatus status)
    {
        // Arrange
        _context.JobExecutions.Add(new JobExecution
        {
            JobType = "DownloadImportItem",
            Status = status,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "type:DownloadImportItem|downloadId=abcdefff-1234-5678-90ab-cdef12345678",
            CreatedAt = new DateTime(2026, 7, 17, 11, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var recovery = new JobExecution
        {
            JobType = "DownloadImportItem",
            Status = JobExecutionStatus.Queued,
            ExecutionMode = JobExecutionMode.Queue,
            LockPartition = "type:DownloadImportItem|downloadId=abcdefff-1234-5678-90ab-cdef12345678"
        };

        // Act
        var result = await _subject.CreateIfInactiveAsync(recovery, _userId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(JobExecutionStatus.Queued));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(_events, Has.Count.EqualTo(2));
        }
    }

    [TestCase(null, JobExecutionMode.Queue, JobExecutionStatus.Queued)]
    [TestCase("", JobExecutionMode.Queue, JobExecutionStatus.Queued)]
    [TestCase("partition", JobExecutionMode.Immediate, JobExecutionStatus.Queued)]
    [TestCase("partition", JobExecutionMode.Queue, JobExecutionStatus.Running)]
    public async Task CreateIfInactiveAsync_RejectsRequest_WhenOwnershipCannotBeChecked(
        string? partition, JobExecutionMode mode, JobExecutionStatus status)
    {
        // Arrange
        var request = new JobExecution
        {
            JobType = "DownloadImportItem",
            LockPartition = partition,
            ExecutionMode = mode,
            Status = status
        };

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(async () =>
            await _subject.CreateIfInactiveAsync(request, null).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("model"));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(0));
            Assert.That(_events, Is.Empty);
        }
    }

    [Test]
    public async Task CreateAsync_QueuesBothRequests_WhenMatchingExecutionIsRunning()
    {
        // Arrange
        const string jobType = "LibraryImportMatchResolution";
        var running = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = jobType,
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Running,
            LockPartition = $"type:{jobType}",
            CreatedAt = Now.UtcDateTime,
        };
        _context.JobExecutions.Add(running);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(running).State = EntityState.Detached;

        var undeferredRequeue = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = jobType,
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Queued,
            LockPartition = $"type:{jobType}",
        };
        var deferredRequeue = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = jobType,
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Queued,
            LockPartition = $"type:{jobType}",
            NotBeforeAt = Now.UtcDateTime.AddSeconds(1),
        };

        // Act
        var undeferredResult = await _subject.CreateAsync(undeferredRequeue, createdByUserId: null)
            .ConfigureAwait(false);
        var deferredResult = await _subject.CreateAsync(deferredRequeue, createdByUserId: null)
            .ConfigureAwait(false);

        // Assert
        var executions = await _context.JobExecutions.CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(undeferredResult.Id, Is.EqualTo(undeferredRequeue.Id));
            Assert.That(
                deferredResult.Id,
                Is.Not.EqualTo(running.Id),
                "the deferred self-requeue must insert a new row");
            Assert.That(executions, Is.EqualTo(3));
        }
    }

    [TestCase(JobExecutionStatus.Queued, true, 1)]
    [TestCase(JobExecutionStatus.Running, false, 2)]
    public async Task CreateAsync_CoalescesOnlyQueuedImport_WhenASecondDownloadCompletedEventArrives(
        JobExecutionStatus status, bool coalesced, int expectedCount)
    {
        // Arrange
        var first = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = status,
            LockPartition = "type:DownloadImportItem|downloadId=3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19",
            DedupKey = "event-trigger:0f2c9a517d644b189c335a6e8f0d2b47:8c2e3a441b5d4e779f302a6c4d8e0b12",
            CreatedAt = Now.UtcDateTime,
        };
        _context.JobExecutions.Add(first);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(first).State = EntityState.Detached;
        var second = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Queued,
            LockPartition = "type:DownloadImportItem|downloadId=3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19",
            DedupKey = "event-trigger:0f2c9a517d644b189c335a6e8f0d2b47:1d4f6b907c214e588a035e9b2c7f4a68",
        };

        // Act
        var result = await _subject.CreateAsync(second, createdByUserId: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Id == first.Id, Is.EqualTo(coalesced));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(expectedCount));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesASecondImport_WhenTheFirstImportAlreadyCompleted()
    {
        // Arrange
        var first = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Completed,
            LockPartition = "type:DownloadImportItem|downloadId=3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19",
            DedupKey = "event-trigger:0f2c9a517d644b189c335a6e8f0d2b47:8c2e3a441b5d4e779f302a6c4d8e0b12",
            CreatedAt = Now.UtcDateTime,
        };
        _context.JobExecutions.Add(first);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(first).State = EntityState.Detached;
        var second = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Queued,
            LockPartition = "type:DownloadImportItem|downloadId=3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19",
            DedupKey = "event-trigger:0f2c9a517d644b189c335a6e8f0d2b47:1d4f6b907c214e588a035e9b2c7f4a68",
        };

        // Act
        var result = await _subject.CreateAsync(second, createdByUserId: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Id, Is.Not.EqualTo(first.Id));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task CreateAsync_ReturnsTheCompletedImport_WhenTheSameEventIsRedelivered()
    {
        // Arrange
        var first = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Completed,
            LockPartition = "type:DownloadImportItem|downloadId=3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19",
            DedupKey = "event-trigger:0f2c9a517d644b189c335a6e8f0d2b47:8c2e3a441b5d4e779f302a6c4d8e0b12",
            CreatedAt = Now.UtcDateTime,
        };
        _context.JobExecutions.Add(first);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(first).State = EntityState.Detached;
        var redelivered = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Queued,
            LockPartition = "type:DownloadImportItem|downloadId=3d7b1e88-2a4c-4f51-9a6d-0b8e5c4f2a19",
            DedupKey = "event-trigger:0f2c9a517d644b189c335a6e8f0d2b47:8c2e3a441b5d4e779f302a6c4d8e0b12",
        };

        // Act
        var result = await _subject.CreateAsync(redelivered, createdByUserId: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Id, Is.EqualTo(first.Id));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task CreateAsync_NewDeferredExecution_WhenImmediateQueuedWorkExists()
    {
        // Arrange
        var immediate = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadRefresh",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "type:DownloadRefresh",
            CreatedAt = Now.UtcDateTime,
        };
        _context.JobExecutions.Add(immediate);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(immediate).State = EntityState.Detached;
        var incoming = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadRefresh",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "type:DownloadRefresh",
            NotBeforeAt = Now.UtcDateTime.AddSeconds(20),
        };

        // Act
        var created = await _subject.CreateAsync(incoming, createdByUserId: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Id, Is.Not.EqualTo(immediate.Id));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
        }
    }

    [TestCase(JobExecutionStatus.Queued, true, 1)]
    [TestCase(JobExecutionStatus.Running, false, 2)]
    public async Task CreateAsync_CoalescesOnlyQueuedExecution_WhenImmediateSameJobWorkExists(
        JobExecutionStatus status, bool coalesced, int expectedCount)
    {
        // Arrange
        var existing = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ContentMetadataRefreshItem",
            Scope = JobScope.System,
            Status = status,
            LockPartition = "contentHierarchyItemId=item-1",
            CreatedAt = Now.UtcDateTime,
        };
        _context.JobExecutions.Add(existing);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(existing).State = EntityState.Detached;
        var incoming = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ContentMetadataRefreshItem",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "contentHierarchyItemId=item-1",
        };

        // Act
        var result = await _subject.CreateAsync(incoming, createdByUserId: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Id == existing.Id, Is.EqualTo(coalesced));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(expectedCount));
        }
    }

    [Test]
    public async Task CreateAsync_NewImmediateExecution_WhenDeferredQueuedWorkExists()
    {
        // Arrange
        var deferred = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadRefresh",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "type:DownloadRefresh",
            NotBeforeAt = Now.UtcDateTime.AddMinutes(5),
            CreatedAt = Now.UtcDateTime,
        };
        _context.JobExecutions.Add(deferred);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(deferred).State = EntityState.Detached;
        var incoming = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadRefresh",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "type:DownloadRefresh",
        };

        // Act
        var created = await _subject.CreateAsync(incoming, createdByUserId: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Id, Is.Not.EqualTo(deferred.Id));
            Assert.That(created.NotBeforeAt, Is.Null);
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task CreateAsync_NewExecution_WhenDifferentJobSharesPartition()
    {
        // Arrange
        var holder = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ContentMetadataRefreshItem",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Running,
            LockPartition = "contentHierarchyItemId=item-1",
            CreatedAt = Now.UtcDateTime,
        };
        _context.JobExecutions.Add(holder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(holder).State = EntityState.Detached;
        var incoming = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "contentHierarchyItemId=item-1",
        };

        // Act
        var created = await _subject.CreateAsync(incoming, createdByUserId: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Id, Is.Not.EqualTo(holder.Id));
            Assert.That(created.JobType, Is.EqualTo(incoming.JobType));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetClaimableCandidatesAsync_ExcludesDifferentJob_WhenPartitionIsRunning()
    {
        // Arrange
        var running = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ContentMetadataRefreshItem",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Running,
            LockPartition = "contentHierarchyItemId=item-1",
            CreatedAt = Now.UtcDateTime,
        };
        var queued = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "ReleaseSearchItem",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "contentHierarchyItemId=item-1",
            CreatedAt = Now.UtcDateTime,
        };
        _context.JobExecutions.Add(running);
        _context.JobExecutions.Add(queued);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(running).State = EntityState.Detached;
        _context.Entry(queued).State = EntityState.Detached;

        // Act
        var candidates = await _subject.GetClaimableCandidatesAsync(
            new PagingParameter { PageNumber = 1, PageSize = 500 }).ConfigureAwait(false);

        // Assert
        Assert.That(candidates.Select(candidate => candidate.Id), Does.Not.Contain(queued.Id));
    }

    [TestCase("item-a", "item-b", null)]
    [TestCase("item-a", null, null)]
    [TestCase("item-a", "item-a", "item-a")]
    [TestCase(null, "item-b", null)]
    public async Task ExecuteAsync_KeepsEveryRequestedFolderInOneDiscovery_WhenScopesInTheSameRootFolderCoalesce(
        string? firstItem, string? secondItem, string? expectedItem)
    {
        // Arrange
        IJobManager manager = new JobManager(
            Mock.Of<IJobRuntime>(),
            Mock.Of<IJobRegistry>(registry =>
                registry.ListJobSpecs() == new[] { RootFolderContentDiscoveryJob.Spec }),
            _subject,
            Mock.Of<IScheduleTriggerRepository>(),
            Mock.Of<IEventTriggerRepository>(),
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            _timeProvider,
            NullLogger<JobManager>.Instance, _transactionManager);
        var rootFolderId = Guid.NewGuid();
        var itemIds = new Dictionary<string, string>
        {
            ["item-a"] = Guid.NewGuid().ToString("D"),
            ["item-b"] = Guid.NewGuid().ToString("D")
        };
        var first = new Dictionary<string, string> { ["rootFolderId"] = rootFolderId.ToString("D") };
        var second = new Dictionary<string, string>(first);
        if (firstItem is not null)
        {
            first["contentHierarchyItemId"] = itemIds[firstItem];
        }

        if (secondItem is not null)
        {
            second["contentHierarchyItemId"] = itemIds[secondItem];
        }

        // Act
        var firstId = await manager.ExecuteAsync(
            new JobEnqueueRequest { JobType = "RootFolderContentDiscovery", Parameters = first })
            .ConfigureAwait(false);
        var secondId = await manager.ExecuteAsync(
            new JobEnqueueRequest { JobType = "RootFolderContentDiscovery", Parameters = second })
            .ConfigureAwait(false);

        // Assert
        var execution = await _context.JobExecutions.AsNoTracking().SingleAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(secondId, Is.EqualTo(firstId));
            Assert.That(
                execution.Parameters.GetValueOrDefault("contentHierarchyItemId"),
                Is.EqualTo(expectedItem is null ? null : itemIds[expectedItem]));
        }
    }

    [Test]
    public async Task ExecuteAsync_CoalescesSameRootFolder_WhenTargetsUseParameterAndEventContext()
    {
        // Arrange
        IJobManager manager = new JobManager(
            Mock.Of<IJobRuntime>(),
            Mock.Of<IJobRegistry>(registry =>
                registry.ListJobSpecs() == new[] { RootFolderContentDiscoveryJob.Spec }),
            _subject,
            Mock.Of<IScheduleTriggerRepository>(),
            Mock.Of<IEventTriggerRepository>(),
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            _timeProvider,
            NullLogger<JobManager>.Instance, _transactionManager);
        var rootFolderId = Guid.NewGuid();
        var otherRootFolderId = Guid.NewGuid();

        // Act
        var parameterExecutionId = await manager.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = "RootFolderContentDiscovery",
                Parameters = new Dictionary<string, string>
                {
                    ["rootFolderId"] = rootFolderId.ToString()
                }
            }).ConfigureAwait(false);
        var eventExecutionId = await manager.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = "RootFolderContentDiscovery",
                TriggerContext = new Dictionary<string, string>
                {
                    ["rootFolder.id".ToEventContextKey()] = rootFolderId.ToString()
                }
            }).ConfigureAwait(false);
        var otherExecutionId = await manager.ExecuteAsync(
            new JobEnqueueRequest
            {
                JobType = "RootFolderContentDiscovery",
                Parameters = new Dictionary<string, string>
                {
                    ["rootFolderId"] = otherRootFolderId.ToString()
                }
            }).ConfigureAwait(false);

        // Assert
        var executions = await _context.JobExecutions.AsNoTracking().ToListAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(eventExecutionId, Is.EqualTo(parameterExecutionId));
            Assert.That(otherExecutionId, Is.Not.EqualTo(parameterExecutionId));
            Assert.That(executions, Has.Count.EqualTo(2));
            Assert.That(
                executions.Single(execution => execution.Id == parameterExecutionId).LockPartition,
                Is.EqualTo(
                    "type:RootFolderContentDiscovery|" +
                    $"rootFolderId={rootFolderId}"));
            Assert.That(
                executions.Single(execution => execution.Id == otherExecutionId).LockPartition,
                Is.EqualTo(
                    "type:RootFolderContentDiscovery|" +
                    $"rootFolderId={otherRootFolderId}"));
        }
    }

    [Test]
    public async Task CreateAsync_ExistingFirstExecution_WhenOuterTransactionUsesFirstSecondFirstIdentities()
    {
        // Arrange
        var firstRequest = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadRefresh",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "partition:first",
            NotBeforeAt = Now.UtcDateTime.AddSeconds(20),
        };
        var secondRequest = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadRefresh",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "partition:second",
            NotBeforeAt = Now.UtcDateTime.AddSeconds(20),
        };
        var repeatedRequest = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadRefresh",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "partition:first",
            NotBeforeAt = Now.UtcDateTime.AddSeconds(30),
        };
        JobExecution? first = null;
        JobExecution? second = null;
        JobExecution? repeated = null;

        // Act
        await _transactionManager.ExecuteWithTransactionAsync(async ct =>
        {
            first = await _subject.CreateAsync(firstRequest, createdByUserId: null, ct).ConfigureAwait(false);
            second = await _subject.CreateAsync(secondRequest, createdByUserId: null, ct).ConfigureAwait(false);
            repeated = await _subject.CreateAsync(repeatedRequest, createdByUserId: null, ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(repeated!.Id, Is.EqualTo(first!.Id));
            Assert.That(second!.Id, Is.Not.EqualTo(first.Id));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(
                await _context.Set<Dictionary<string, object>>("JobEnqueueCoalescingLock")
                    .CountAsync().ConfigureAwait(false),
                Is.Zero);
        }
    }

    [Test]
    public async Task CreateAsync_NoExecutionsOrLocks_WhenOuterTransactionRollsBack()
    {
        // Arrange
        var request = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadRefresh",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "partition:rollback",
        };

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _transactionManager.ExecuteWithTransactionAsync(async ct =>
            {
                await _subject.CreateAsync(request, createdByUserId: null, ct).ConfigureAwait(false);
                throw new InvalidOperationException("Rollback the caller-owned transaction.");
            }).ConfigureAwait(false));

        // Assert
        var executionCount = await _context.JobExecutions.CountAsync().ConfigureAwait(false);
        var lockCount = await _context.Set<Dictionary<string, object>>("JobEnqueueCoalescingLock")
            .CountAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("Rollback the caller-owned transaction."));
            Assert.That(executionCount, Is.Zero);
            Assert.That(lockCount, Is.Zero);
        }
    }

    [Test]
    public async Task CreateAsync_SamePartition_WhenRetryClone()
    {
        // Arrange
        var subject = new JobExecutionRepository(
            _context,
            new AuditRecordRepository(
                _context,
                Mock.Of<IEventBus>(),
                _loggerFactory.CreateLogger<AuditRecordRepository>(),
                new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
                Mock.Of<IJobExecutionScopeFactory>(),
                timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IEventBus>(),
            _loggerFactory.CreateLogger<JobExecutionRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)),
            new JobRegistry([], NullLogger<JobRegistry>.Instance));
        var original = await subject.CreateAsync(
            new JobExecution
            {
                JobType = "SearchItem",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Queued,
                LockPartition = "contentItemId=item-99",
                Parameters = new() { ["contentItemId"] = "item-99" }
            },
            _userId,
            default).ConfigureAwait(false);

        // Act
        var retry = new JobExecution
        {
            JobType = "SearchItem",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            OriginalExecutionId = original.Id,
            LockPartition = "contentItemId=item-99",
            Parameters = new() { ["contentItemId"] = "item-99" },
            Attempt = 2,
            NotBeforeAt = DateTime.UtcNow
        };
        var persistedRetry = await subject.CreateAsync(retry, _userId, default).ConfigureAwait(false);

        // Assert
        Assert.That(persistedRetry.LockPartition, Is.EqualTo(original.LockPartition));
    }

    [Test]
    public async Task CreateAsync_SamePartition_WhenRequeueClone()
    {
        // Arrange
        var subject = new JobExecutionRepository(
            _context,
            new AuditRecordRepository(
                _context,
                Mock.Of<IEventBus>(),
                _loggerFactory.CreateLogger<AuditRecordRepository>(),
                new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
                Mock.Of<IJobExecutionScopeFactory>(),
                timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IEventBus>(),
            _loggerFactory.CreateLogger<JobExecutionRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)),
            new JobRegistry([], NullLogger<JobRegistry>.Instance));
        var original = await subject.CreateAsync(
            new JobExecution
            {
                JobType = "WishlistRefreshItem",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Queued,
                LockPartition = $"unique:{Guid.NewGuid():N}"
            },
            _userId,
            default).ConfigureAwait(false);

        // Act
        var requeue = new JobExecution
        {
            JobType = "WishlistRefreshItem",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            OriginalExecutionId = original.Id,
            LockPartition = original.LockPartition
        };
        var persistedRequeue = await subject.CreateAsync(requeue, _userId, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(original.LockPartition, Is.Not.Null);
            Assert.That(persistedRequeue.LockPartition, Is.EqualTo(original.LockPartition));
        }
    }

    [Test]
    public async Task GetClaimableCandidatesAsync_ExcludesQueuedSibling_WhenPartitionHeldByCancellationRequested()
    {
        // Arrange
        IJobExecutionRepository subject = new JobExecutionRepository(
            _context,
            new AuditRecordRepository(
                _context,
                Mock.Of<IEventBus>(),
                _loggerFactory.CreateLogger<AuditRecordRepository>(),
                new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
                Mock.Of<IJobExecutionScopeFactory>(),
                timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IEventBus>(),
            _loggerFactory.CreateLogger<JobExecutionRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)),
            new JobRegistry([], NullLogger<JobRegistry>.Instance));
        var holder = new JobExecution
        {
            JobType = "FullScan",
            Scope = JobScope.System,
            Status = JobExecutionStatus.CancellationRequested,
            LockPartition = "type:FullScan",
            NodeId = "node-1",
            CreatedAt = DateTime.UtcNow
        };
        var queued = new JobExecution
        {
            JobType = "FullScan",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "type:FullScan",
            NodeId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(holder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(holder).State = EntityState.Detached;
        _context.JobExecutions.Add(queued);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(queued).State = EntityState.Detached;

        // Act
        var candidates = await subject.GetClaimableCandidatesAsync(
            new PagingParameter { PageNumber = 1, PageSize = 500 }).ConfigureAwait(false);

        // Assert
        Assert.That(candidates.Select(c => c.Id), Does.Not.Contain(queued.Id));
    }

    [Test]
    public async Task TryClaimAsync_Null_WhenPartitionHeldByCancellationRequested()
    {
        // Arrange
        var subject = new JobExecutionRepository(
            _context,
            new AuditRecordRepository(
                _context,
                Mock.Of<IEventBus>(),
                _loggerFactory.CreateLogger<AuditRecordRepository>(),
                new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
                Mock.Of<IJobExecutionScopeFactory>(),
                timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IEventBus>(),
            _loggerFactory.CreateLogger<JobExecutionRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)),
            new JobRegistry([], NullLogger<JobRegistry>.Instance));
        var holder = new JobExecution
        {
            JobType = "FullScan",
            Scope = JobScope.System,
            Status = JobExecutionStatus.CancellationRequested,
            LockPartition = "type:FullScan",
            NodeId = "node-1",
            CreatedAt = DateTime.UtcNow
        };
        var queued = new JobExecution
        {
            JobType = "FullScan",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "type:FullScan",
            NodeId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(holder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(holder).State = EntityState.Detached;
        _context.JobExecutions.Add(queued);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(queued).State = EntityState.Detached;

        // Act
        var claimed = await subject.TryClaimAsync(queued.Id, "node-2", default).ConfigureAwait(false);

        // Assert
        Assert.That(claimed, Is.Null);
    }

    [Test]
    public async Task TouchHeartbeatAsync_True_WhenTheNodeOwnsTheExecution()
    {
        // Arrange
        var subject = new JobExecutionRepository(
            _context,
            new AuditRecordRepository(
                _context,
                Mock.Of<IEventBus>(),
                _loggerFactory.CreateLogger<AuditRecordRepository>(),
                new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
                Mock.Of<IJobExecutionScopeFactory>(),
                timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IEventBus>(),
            _loggerFactory.CreateLogger<JobExecutionRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)),
            new JobRegistry([], NullLogger<JobRegistry>.Instance));
        var running = new JobExecution
        {
            JobType = "FullScan",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Running,
            NodeId = "node-1",
            LastHeartbeat = new DateTime(2026, 9, 8, 11, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(running);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(running).State = EntityState.Detached;

        // Act
        var owned = await subject.TouchHeartbeatAsync(running.Id, "node-1", default).ConfigureAwait(false);

        // Assert
        var stored = await _context.JobExecutions.AsNoTracking().SingleAsync(e => e.Id == running.Id)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(owned, Is.True);
            Assert.That(stored.LastHeartbeat, Is.EqualTo(new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc)));
        }
    }

    [Test]
    public async Task TouchHeartbeatAsync_False_WhenAnotherNodeOwnsTheExecution()
    {
        // Arrange
        var subject = new JobExecutionRepository(
            _context,
            new AuditRecordRepository(
                _context,
                Mock.Of<IEventBus>(),
                _loggerFactory.CreateLogger<AuditRecordRepository>(),
                new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
                Mock.Of<IJobExecutionScopeFactory>(),
                timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IEventBus>(),
            _loggerFactory.CreateLogger<JobExecutionRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)),
            new JobRegistry([], NullLogger<JobRegistry>.Instance));
        var running = new JobExecution
        {
            JobType = "FullScan",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Running,
            NodeId = "node-1",
            LastHeartbeat = new DateTime(2026, 9, 8, 11, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(running);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(running).State = EntityState.Detached;

        // Act
        var owned = await subject.TouchHeartbeatAsync(running.Id, "node-2", default).ConfigureAwait(false);

        // Assert
        var stored = await _context.JobExecutions.AsNoTracking().SingleAsync(e => e.Id == running.Id)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(owned, Is.False);
            Assert.That(stored.LastHeartbeat, Is.EqualTo(new DateTime(2026, 9, 8, 11, 0, 0, DateTimeKind.Utc)));
        }
    }

    [Test]
    public async Task TryStartAsync_False_WhenPartitionHeldByCancellationRequested()
    {
        // Arrange
        var subject = new JobExecutionRepository(
            _context,
            new AuditRecordRepository(
                _context,
                Mock.Of<IEventBus>(),
                _loggerFactory.CreateLogger<AuditRecordRepository>(),
                new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
                Mock.Of<IJobExecutionScopeFactory>(),
                timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IEventBus>(),
            _loggerFactory.CreateLogger<JobExecutionRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)),
            new JobRegistry([], NullLogger<JobRegistry>.Instance));
        var holder = new JobExecution
        {
            JobType = "FullScan",
            Scope = JobScope.System,
            Status = JobExecutionStatus.CancellationRequested,
            LockPartition = "type:FullScan",
            NodeId = "node-1",
            CreatedAt = DateTime.UtcNow
        };
        var queued = new JobExecution
        {
            JobType = "FullScan",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "type:FullScan",
            NodeId = null,
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(holder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(holder).State = EntityState.Detached;
        _context.JobExecutions.Add(queued);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(queued).State = EntityState.Detached;

        // Act
        var started = await subject.TryStartAsync(queued.Id, "node-2", default).ConfigureAwait(false);

        // Assert
        Assert.That(started, Is.False);
    }

    [Test]
    public async Task CreateAsync_QueuesSuccessor_WhenPartitionHeldByCancellationRequested()
    {
        // Arrange
        var subject = new JobExecutionRepository(
            _context,
            new AuditRecordRepository(
                _context,
                Mock.Of<IEventBus>(),
                _loggerFactory.CreateLogger<AuditRecordRepository>(),
                new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
                Mock.Of<IJobExecutionScopeFactory>(),
                timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IEventBus>(),
            _loggerFactory.CreateLogger<JobExecutionRepository>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)),
            new JobRegistry([], NullLogger<JobRegistry>.Instance));
        var holder = new JobExecution
        {
            JobType = "FullScan",
            Scope = JobScope.System,
            Status = JobExecutionStatus.CancellationRequested,
            LockPartition = "type:FullScan",
            NodeId = "node-1",
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(holder);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(holder).State = EntityState.Detached;

        // Act
        var created = await subject.CreateAsync(
            new JobExecution
            {
                JobType = "FullScan",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Queued,
                LockPartition = "type:FullScan"
            },
            _userId,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Id, Is.Not.EqualTo(holder.Id));
            Assert.That(created.Status, Is.EqualTo(JobExecutionStatus.Queued));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task CreateAsync_CreatedThenQueuedEvents_WhenQueuedExecutionInserted()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };

        // Act
        var created = await _subject.CreateAsync(execution, createdByUserId: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _events.Select(magpieEvent => magpieEvent.Name),
                Is.EqualTo(
                [
                    "job.execution.created",
                    "job.execution.queued"
                ]));
            Assert.That(
                _events.Select(magpieEvent => magpieEvent.Context["execution.id"]),
                Is.All.EqualTo(created.Id.ToString("D")));
        }
    }

    [Test]
    public async Task CreateAsync_CreatedThenQueuedEvents_WhenQueuedExecutionIsDeferred()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };
        execution.NotBeforeAt = EventNow.UtcDateTime.AddHours(1);

        // Act
        await _subject.CreateAsync(execution, createdByUserId: null).ConfigureAwait(false);

        // Assert
        Assert.That(
            _events.Select(magpieEvent => magpieEvent.Name),
            Is.EqualTo(
            [
                "job.execution.created",
                "job.execution.queued"
            ]));
    }

    [Test]
    public async Task CreateAsync_NoEvents_WhenDedupKeyReturnsExistingExecution()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var existing = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };
        existing.DedupKey = "dedup-key";
        await _subject.CreateAsync(existing, createdByUserId: null).ConfigureAwait(false);
        _events.Clear();

        var duplicate = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };
        duplicate.DedupKey = existing.DedupKey;

        // Act
        var result = await _subject.CreateAsync(duplicate, createdByUserId: null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Id, Is.EqualTo(existing.Id));
            Assert.That(_events, Is.Empty);
        }
    }

    [Test]
    public async Task CreateAsync_NoEvents_WhenCoalesceAdvancesDeferral()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var existing = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };
        existing.LockPartition = "type:test-job";
        existing.NotBeforeAt = EventNow.UtcDateTime.AddHours(2);
        await _subject.CreateAsync(existing, createdByUserId: null).ConfigureAwait(false);
        _events.Clear();

        var incoming = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };
        incoming.LockPartition = existing.LockPartition;
        incoming.NotBeforeAt = EventNow.UtcDateTime.AddHours(1);

        // Act
        var result = await _subject.CreateAsync(incoming, createdByUserId: null).ConfigureAwait(false);

        // Assert
        var persisted = await _context.JobExecutions
            .AsNoTracking()
            .SingleAsync(execution => execution.Id == existing.Id)
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Id, Is.EqualTo(existing.Id));
            Assert.That(persisted.NotBeforeAt, Is.EqualTo(incoming.NotBeforeAt));
            Assert.That(_events, Is.Empty);
        }
    }

    [Test]
    public async Task TryClaimAsync_StartedEvent_WhenQueuedExecutionClaimed()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };
        execution.CreatedAt = EventNow.UtcDateTime;
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(execution).State = EntityState.Detached;
        _events.Clear();

        // Act
        var claimed = await _subject.TryClaimAsync(execution.Id, "node-a").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claimed, Is.Not.Null);
            Assert.That(_events, Has.Count.EqualTo(1));
            Assert.That(_events[0].Name, Is.EqualTo("job.execution.started"));
            Assert.That(_events[0].Context["execution.id"],
                Is.EqualTo(execution.Id.ToString("D")));
        }

        _events.Clear();

        // Act
        var secondStart = await _subject.TryStartAsync(execution.Id, "node-a").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(secondStart, Is.False);
            Assert.That(_events, Is.Empty);
        }
    }

    [Test]
    public async Task TryStartAsync_StartedEvent_WhenQueuedExecutionStarted()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };
        execution.CreatedAt = EventNow.UtcDateTime;
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(execution).State = EntityState.Detached;
        _events.Clear();

        // Act
        var started = await _subject.TryStartAsync(execution.Id, "node-a").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started, Is.True);
            Assert.That(_events, Has.Count.EqualTo(1));
            Assert.That(_events[0].Name, Is.EqualTo("job.execution.started"));
            Assert.That(_events[0].Context["execution.id"],
                Is.EqualTo(execution.Id.ToString("D")));
        }
    }

    [Test]
    public async Task UpdateProgressAsync_ProgressChangedEvent_WhenRunningExecutionUpdated()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Running
        };
        execution.CreatedAt = EventNow.UtcDateTime;
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(execution).State = EntityState.Detached;
        _events.Clear();

        // Act
        var updated = await _subject.UpdateProgressAsync(execution.Id, 25, "Working").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.True);
            Assert.That(_events, Has.Count.EqualTo(1));
            Assert.That(_events[0].Name, Is.EqualTo("job.execution.progress.changed"));
            Assert.That(_events[0].Context["execution.id"],
                Is.EqualTo(execution.Id.ToString("D")));
        }

        _events.Clear();
        await _context.JobExecutions
            .Where(candidate => candidate.Id == execution.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                candidate => candidate.Status,
                JobExecutionStatus.Completed))
            .ConfigureAwait(false);

        // Act
        var terminalUpdate = await _subject.UpdateProgressAsync(execution.Id, 50, "Too late").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(terminalUpdate, Is.False);
            Assert.That(_events, Is.Empty);
        }
    }

    [Test]
    public async Task TryMarkRunningFailedAsync_DoesNotTransition_WhenCancellationWasRequested()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Running
        };
        execution.CreatedAt = EventNow.UtcDateTime;
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(execution).State = EntityState.Detached;
        _events.Clear();
        await _subject.CancelAsync(execution.Id).ConfigureAwait(false);
        _events.Clear();

        // Act
        var transitioned = await _subject
            .TryMarkRunningFailedAsync(execution.Id, "Timed out")
            .ConfigureAwait(false);

        // Assert
        var persisted = await _context.JobExecutions.FindAsync(execution.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(transitioned, Is.False);
            Assert.That(persisted!.Status, Is.EqualTo(JobExecutionStatus.CancellationRequested));
            Assert.That(_events, Is.Empty);
        }
    }

    [Test]
    public async Task TryMarkQueuedFailedAsync_TransitionsQueuedExecution_WhenExecutionIsQueued()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };
        execution.CreatedAt = EventNow.UtcDateTime;
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(execution).State = EntityState.Detached;
        _events.Clear();

        // Act
        var transitioned = await _subject
            .TryMarkQueuedFailedAsync(execution.Id, "No registered handler")
            .ConfigureAwait(false);

        // Assert
        var persisted = await _context.JobExecutions.FindAsync(execution.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(transitioned, Is.True);
            Assert.That(persisted!.Status, Is.EqualTo(JobExecutionStatus.Failed));
        }
    }

    [Test]
    public async Task TryMarkQueuedFailedAsync_DoesNotTransition_WhenCancellationWon()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };
        execution.CreatedAt = EventNow.UtcDateTime;
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(execution).State = EntityState.Detached;
        _events.Clear();
        await _subject.CancelAsync(execution.Id).ConfigureAwait(false);
        _events.Clear();

        // Act
        var transitioned = await _subject
            .TryMarkQueuedFailedAsync(execution.Id, "No registered handler")
            .ConfigureAwait(false);

        // Assert
        var persisted = await _context.JobExecutions.FindAsync(execution.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(transitioned, Is.False);
            Assert.That(persisted!.Status, Is.EqualTo(JobExecutionStatus.Cancelled));
            Assert.That(_events, Is.Empty);
        }
    }

    [Test]
    public async Task CancelAsync_CancelledEvent_WhenQueuedExecutionCancelled()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };
        execution.CreatedAt = EventNow.UtcDateTime;
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(execution).State = EntityState.Detached;
        _events.Clear();

        // Act
        var result = await _subject.CancelAsync(execution.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.CancelledBeforeStart, Is.True);
            Assert.That(_events, Has.Count.EqualTo(1));
            Assert.That(_events[0].Name, Is.EqualTo("job.execution.cancelled"));
            Assert.That(_events[0].Context["execution.id"],
                Is.EqualTo(execution.Id.ToString("D")));
        }
    }

    [Test]
    public async Task CancelAsync_CancellationRequestedEventOnce_WhenRunningExecutionRequested()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Running
        };
        execution.NodeId = "node-a";
        execution.CreatedAt = EventNow.UtcDateTime;
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(execution).State = EntityState.Detached;
        _events.Clear();

        // Act
        var result = await _subject.CancelAsync(execution.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.CancelledBeforeStart, Is.False);
            Assert.That(_events, Has.Count.EqualTo(1));
            Assert.That(_events[0].Name, Is.EqualTo("job.execution.cancellationRequested"));
            Assert.That(_events[0].Context["execution.id"],
                Is.EqualTo(execution.Id.ToString("D")));
        }

        _events.Clear();

        // Act
        var repeated = await _subject.CancelAsync(execution.Id).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(repeated, Is.Null);
            Assert.That(_events, Is.Empty);
        }
    }

    [Test]
    public async Task TryMarkCancellationRequestedCancelledAsync_PublishesOnce_WhenExecutionTransitions()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "job-owner",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = EventNow.UtcDateTime
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var correlationId = Guid.NewGuid();
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.CancellationRequested
        };
        execution.CreatedByUserId = user.Id;
        execution.CorrelationId = correlationId;
        execution.CreatedAt = EventNow.UtcDateTime;
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(execution).State = EntityState.Detached;
        _events.Clear();

        // Act
        var transitioned = await _subject
            .TryMarkCancellationRequestedCancelledAsync(execution.Id, "Owner node stopped")
            .ConfigureAwait(false);

        // Assert
        var audit = await _context.AuditRecords
            .AsNoTracking()
            .SingleAsync(record => record.EntityId == execution.Id.ToString())
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(transitioned, Is.True);
            Assert.That(_events, Has.Count.EqualTo(1));
            Assert.That(_events[0].Name, Is.EqualTo("job.execution.cancelled"));
            Assert.That(_events[0].Context["execution.id"],
                Is.EqualTo(execution.Id.ToString("D")));
            Assert.That(audit.Category, Is.EqualTo(AuditCategories.Jobs));
            Assert.That(audit.Operation, Is.EqualTo("JobCancelled"));
            Assert.That(audit.EntityType, Is.EqualTo(nameof(JobExecution)));
            Assert.That(audit.EntityId, Is.EqualTo(execution.Id.ToString()));
            Assert.That(audit.Detail, Is.EqualTo($"Canceled job run {execution.Id}."));
            Assert.That(audit.UserId, Is.EqualTo(user.Id));
            Assert.That(audit.CorrelationId, Is.EqualTo(correlationId));
        }

        _events.Clear();

        // Act
        var repeated = await _subject
            .TryMarkCancellationRequestedCancelledAsync(execution.Id, "Repeated")
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(repeated, Is.False);
            Assert.That(_events, Is.Empty);
        }
    }

    [Test]
    public async Task TryMarkCancellationRequestedCancelledAsync_ReturnsFalse_WhenExecutionAlreadyFailed()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Failed
        };
        execution.CreatedAt = EventNow.UtcDateTime;
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.Entry(execution).State = EntityState.Detached;
        _events.Clear();

        // Act
        var transitioned = await _subject
            .TryMarkCancellationRequestedCancelledAsync(execution.Id, "Too late")
            .ConfigureAwait(false);

        // Assert
        var persisted = await _context.JobExecutions.FindAsync(execution.Id).ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(transitioned, Is.False);
            Assert.That(persisted!.Status, Is.EqualTo(JobExecutionStatus.Failed));
            Assert.That(_events, Is.Empty);
        }
    }

    [Test]
    public async Task CreateAsync_DefersEventsUntilCommit_WhenWrappedInTransaction()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };
        var eventsDuringTransaction = -1;

        // Act
        await _transactionManager.ExecuteWithTransactionAsync(async ct =>
        {
            await _subject.CreateAsync(execution, createdByUserId: null, ct).ConfigureAwait(false);
            eventsDuringTransaction = _events.Count;
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(eventsDuringTransaction, Is.Zero);
            Assert.That(
                _events.Select(magpieEvent => magpieEvent.Name),
                Is.EqualTo(
                [
                    "job.execution.created",
                    "job.execution.queued"
                ]));
        }
    }

    [Test]
    public void CreateAsync_DropsEvents_WhenWrappedTransactionRollsBack()
    {
        // Arrange
        _timeProvider.AdjustTime(EventNow);
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "test-job",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued
        };

        // Act
        var act = (AsyncTestDelegate)(async () =>
            await _transactionManager.ExecuteWithTransactionAsync(async ct =>
            {
                await _subject.CreateAsync(execution, createdByUserId: null, ct).ConfigureAwait(false);
                throw new InvalidOperationException("boom");
            }).ConfigureAwait(false));

        // Assert
        Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.That(_events, Is.Empty);
    }

    [TestCase("D")]
    [TestCase("N")]
    [TestCase("B")]
    [TestCase("P")]
    public async Task QueryAsync_ReturnsTriggerContextReferences_WhenParameterValueReferencesSpecified(string format)
    {
        // Arrange
        var downloadId = Guid.NewGuid().ToString(format);
        var downloadIdEventContextKey = "download.id".ToEventContextKey();
        var matching = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Completed,
            TriggerContext = new() { [downloadIdEventContextKey] = downloadId },
            CreatedAt = DateTime.UtcNow
        };
        var unrelated = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Completed,
            TriggerContext = new() { [downloadIdEventContextKey] = Guid.NewGuid().ToString("D") },
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.AddRange(matching, unrelated);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.QueryAsync(
            new JobExecutionQuery { ParameterValueReferences = [downloadId.ToUpperInvariant()] },
            new PagingParameter { PageNumber = 1, PageSize = 25 },
            sort: null,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Select(execution => execution.Id), Is.EqualTo([matching.Id]));
            Assert.That(page.TotalItems, Is.EqualTo(1));
        }
    }

    [TestCase(MagpieJobs.Types.MediaInfoExtractScan, true)]
    [TestCase(MagpieJobs.Types.MediaServerRefresh, true)]
    [TestCase(MagpieJobs.Types.RootFolderContentDiscovery, false)]
    public async Task QueryAsync_MatchesTriggerContext_WhenTheJobTypeTargetsThatEventKey(
        string jobType,
        bool expected)
    {
        // Arrange
        var contentHierarchyItemId = Guid.NewGuid().ToString("D");
        var execution = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = jobType,
            Scope = JobScope.Administrative,
            Status = JobExecutionStatus.Completed,
            TriggerContext = new()
            {
                [MagpieEvents.Keys.ContentHierarchyItemId.ToEventContextKey()] = contentHierarchyItemId
            },
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(execution);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.QueryAsync(
            new JobExecutionQuery { ParameterValueReferences = [contentHierarchyItemId] },
            new PagingParameter { PageNumber = 1, PageSize = 25 },
            sort: null,
            default).ConfigureAwait(false);

        // Assert
        Assert.That(page.Items.Any(item => item.Id == execution.Id), Is.EqualTo(expected));
    }

    [TestCase("D")]
    [TestCase("N")]
    [TestCase("B")]
    [TestCase("P")]
    public async Task QueryAsync_ReturnsParameterReferences_WhenParameterValueReferencesSpecified(string format)
    {
        // Arrange
        var downloadId = Guid.NewGuid().ToString(format);
        var matching = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Completed,
            Parameters = new() { ["downloadId"] = downloadId },
            CreatedAt = DateTime.UtcNow
        };
        var unrelated = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Completed,
            Parameters = new() { ["downloadId"] = Guid.NewGuid().ToString("D") },
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.AddRange(matching, unrelated);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.QueryAsync(
            new JobExecutionQuery { ParameterValueReferences = [downloadId] },
            new PagingParameter { PageNumber = 1, PageSize = 25 },
            sort: null,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Select(execution => execution.Id), Is.EqualTo([matching.Id]));
            Assert.That(page.TotalItems, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsParameterReferences_WhenParameterIdValueExceedsTheColumnBound()
    {
        // Arrange
        var downloadId = new string('a', 300);
        var matching = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Completed,
            Parameters = new() { ["downloadId"] = downloadId },
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(matching);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.QueryAsync(
            new JobExecutionQuery { ParameterValueReferences = [downloadId] },
            new PagingParameter { PageNumber = 1, PageSize = 25 },
            sort: null,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Select(execution => execution.Id), Is.EqualTo([matching.Id]));
            Assert.That(page.TotalItems, Is.EqualTo(1));
        }
    }

    [TestCase(80)]
    [TestCase(256)]
    [TestCase(257)]
    [TestCase(4001)]
    public async Task QueryAsync_ReturnsParameterReferences_WhenParameterIdValueHasSpecifiedLength(int length)
    {
        // Arrange
        var downloadId = new string('a', length);
        var matching = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Completed,
            Parameters = new() { ["downloadId"] = downloadId },
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(matching);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.QueryAsync(
            new JobExecutionQuery { ParameterValueReferences = [downloadId] },
            new PagingParameter { PageNumber = 1, PageSize = 25 },
            sort: null,
            default).ConfigureAwait(false);
        var deleted = await _subject.DeleteWhereAsync(
            new JobExecutionQuery { ParameterValueReferences = [downloadId] }, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Select(execution => execution.Id), Is.EqualTo([matching.Id]));
            Assert.That(page.TotalItems, Is.EqualTo(1));
            Assert.That(deleted, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsParameterReferences_WhenParameterIdValueIsNotAGuid()
    {
        // Arrange
        var downloadId = "Client-ABC123";
        var matching = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Completed,
            Parameters = new() { ["downloadId"] = downloadId },
            CreatedAt = DateTime.UtcNow
        };
        _context.JobExecutions.Add(matching);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.QueryAsync(
            new JobExecutionQuery { ParameterValueReferences = [downloadId] },
            new PagingParameter { PageNumber = 1, PageSize = 25 },
            sort: null,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Items.Select(execution => execution.Id), Is.EqualTo([matching.Id]));
            Assert.That(page.TotalItems, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task QueryAsync_UsesIndexedIdPathColumns_WhenParameterValueReferencesSpecified()
    {
        // Arrange
        var commands = new List<string>();
        var optionsBuilder = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(optionsBuilder, "Data Source=:memory:", [], []);
        optionsBuilder.LogTo(commands.Add, [DbLoggerCategory.Database.Command.Name]);
        using var loggedContext = new MagpieDbContext(optionsBuilder.Options);
        await loggedContext.Database.OpenConnectionAsync().ConfigureAwait(false);
        try
        {
            await loggedContext.Database.EnsureCreatedAsync().ConfigureAwait(false);
            var transactionManager = new TransactionManager(
                loggedContext, NullLogger<TransactionManager>.Instance);
            IJobExecutionRepository subject = new JobExecutionRepository(
                loggedContext,
                new AuditRecordRepository(
                    loggedContext,
                    Mock.Of<IEventBus>(),
                    NullLogger<AuditRecordRepository>.Instance,
                    transactionManager,
                    Mock.Of<IJobExecutionScopeFactory>(),
                    timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero))),
                transactionManager,
                Mock.Of<IEventBus>(),
                NullLogger<JobExecutionRepository>.Instance,
                timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)),
                new JobRegistry([], NullLogger<JobRegistry>.Instance));
            commands.Clear();

            // Act
            await subject.QueryAsync(
                new JobExecutionQuery { ParameterValueReferences = [Guid.NewGuid().ToString("D")] },
                new PagingParameter { PageNumber = 1, PageSize = 25 },
                sort: null,
                default).ConfigureAwait(false);

            // Assert
            var statements = commands.Where(command => command.Contains("SELECT", StringComparison.Ordinal))
                .ToList();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(statements, Is.Not.Empty);
                Assert.That(statements, Has.Some.Contains("ParameterDownloadId"));
                Assert.That(statements, Has.None.Contains("LIKE"));
            }
        }
        finally
        {
            await loggedContext.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsExecutions_WhenExecutionTimestampIsInRange()
    {
        // Arrange
        var baseline = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
        var completedInRange = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "CompletedInRange",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            CreatedAt = baseline.AddDays(-2),
            StartedAt = baseline.AddHours(-2),
            CompletedAt = baseline.AddMinutes(-30)
        };
        var startedInRange = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "StartedInRange",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Running,
            CreatedAt = baseline.AddDays(-2),
            StartedAt = baseline.AddMinutes(-45)
        };
        var createdInRange = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "CreatedInRange",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            CreatedAt = baseline.AddMinutes(-15)
        };
        var completedOutsideRange = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "CompletedOutsideRange",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            CreatedAt = baseline.AddMinutes(-15),
            StartedAt = baseline.AddMinutes(-10),
            CompletedAt = baseline.AddHours(2)
        };
        var startedOutsideRange = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "StartedOutsideRange",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Running,
            CreatedAt = baseline.AddMinutes(-15),
            StartedAt = baseline.AddHours(-2)
        };
        var createdOutsideRange = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "CreatedOutsideRange",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            CreatedAt = baseline.AddHours(-2)
        };
        _context.JobExecutions.AddRange(
            completedInRange,
            startedInRange,
            createdInRange,
            completedOutsideRange,
            startedOutsideRange,
            createdOutsideRange);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var page = await _subject.QueryAsync(
            new JobExecutionQuery
            {
                ExecutionTimestamp = new(baseline.AddHours(-1), baseline)
            },
            new PagingParameter { PageNumber = 1, PageSize = 25 },
            sort: null,
            default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                page.Items.Select(execution => execution.Id),
                Is.EquivalentTo([completedInRange.Id, startedInRange.Id, createdInRange.Id]));
            Assert.That(page.TotalItems, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task GetClaimableCandidatesAsync_ReturnsCallerSizedPages_WhenMoreCandidatesExist()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var first = new JobExecution
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            JobType = "First",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            CreatedAt = createdAt
        };
        var second = new JobExecution
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            JobType = "Second",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            CreatedAt = createdAt
        };
        var third = new JobExecution
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
            JobType = "Third",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            CreatedAt = createdAt.AddSeconds(1)
        };
        _context.JobExecutions.AddRange(third, second, first);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var firstPage = await _subject.GetClaimableCandidatesAsync(
            new PagingParameter { PageNumber = 1, PageSize = 2 }).ConfigureAwait(false);
        var secondPage = await _subject.GetClaimableCandidatesAsync(
            new PagingParameter { PageNumber = 2, PageSize = 2 }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstPage.Select(candidate => candidate.Id), Is.EqualTo([first.Id, second.Id]));
            Assert.That(secondPage.Select(candidate => candidate.Id), Is.EqualTo([third.Id]));
        }
    }

    [Test]
    public async Task GetClaimableCandidatesAsync_ReturnsBothJobTypes_WhenOneJobTypeFloodsTheBacklog()
    {
        // Arrange
        var createdAt = Now.UtcDateTime;
        var flood = Enumerable.Range(0, 1200)
            .Select(index => new JobExecution
            {
                Id = Guid.NewGuid(),
                JobType = "Flood",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Queued,
                CreatedAt = createdAt.AddSeconds(index)
            })
            .ToList();
        var minority = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "Minority",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            CreatedAt = createdAt.AddSeconds(1200)
        };
        _context.JobExecutions.AddRange(flood);
        _context.JobExecutions.Add(minority);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var candidates = await _subject.GetClaimableCandidatesAsync(
            new PagingParameter { PageNumber = 1, PageSize = 500 }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidates, Has.Count.EqualTo(251));
            Assert.That(candidates.Count(candidate => candidate.JobType == "Flood"), Is.EqualTo(250));
            Assert.That(candidates.Select(candidate => candidate.Id), Does.Contain(minority.Id));
        }
    }

    [Test]
    public async Task GetLineageAsync_ReturnsCompleteLineage_WhenDepthExceedsSixtyFour()
    {
        // Arrange
        var executions = new JobExecution[70];
        Guid? parentId = null;
        for (var index = 0; index < executions.Length; index++)
        {
            executions[index] = new()
            {
                Id = Guid.NewGuid(),
                JobType = "Lineage",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Completed,
                ParentExecutionId = parentId,
                CreatedAt = DateTime.UtcNow.AddSeconds(index)
            };
            parentId = executions[index].Id;
        }

        _context.JobExecutions.AddRange(executions);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetLineageAsync(executions[^1].Id, 2).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(70));
            Assert.That(result.Select(execution => execution.Id), Does.Contain(executions[0].Id));
            Assert.That(result.Select(execution => execution.Id), Does.Contain(executions[^1].Id));
        }
    }

    [Test]
    public async Task GetLineageAsync_ReturnsCompleteLineage_WhenDirectChildrenExceedPageSize()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var root = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "Lineage",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            CreatedAt = createdAt
        };
        var firstChild = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "Lineage",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            ParentExecutionId = root.Id,
            CreatedAt = createdAt.AddSeconds(1)
        };
        var secondChild = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "Lineage",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            ParentExecutionId = root.Id,
            CreatedAt = createdAt.AddSeconds(2)
        };
        var thirdChild = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "Lineage",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            ParentExecutionId = root.Id,
            CreatedAt = createdAt.AddSeconds(3)
        };
        var grandchild = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "Lineage",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            ParentExecutionId = thirdChild.Id,
            CreatedAt = createdAt.AddSeconds(4)
        };
        _context.JobExecutions.AddRange(root, firstChild, secondChild, thirdChild, grandchild);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetLineageAsync(root.Id, 2).ConfigureAwait(false);

        // Assert
        Assert.That(
            result.Select(execution => execution.Id),
            Is.EquivalentTo([root.Id, firstChild.Id, secondChild.Id, thirdChild.Id, grandchild.Id]));
    }

    [Test]
    public async Task GetLineageAsync_ReturnsEachExecutionOnce_WhenLineageContainsCycle()
    {
        // Arrange
        var first = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "Lineage",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        var second = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "Lineage",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            ParentExecutionId = first.Id,
            CreatedAt = DateTime.UtcNow.AddSeconds(1)
        };
        _context.JobExecutions.AddRange(first, second);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        await _context.JobExecutions
            .Where(execution => execution.Id == first.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(execution => execution.ParentExecutionId, second.Id))
            .ConfigureAwait(false);

        // Act
        var result = await _subject.GetLineageAsync(first.Id, 1).ConfigureAwait(false);

        // Assert
        Assert.That(result.Select(execution => execution.Id), Is.EquivalentTo([first.Id, second.Id]));
    }

    [Test]
    public async Task GetLineageAsync_ReturnsTheRequestedExecutionsChildren_WhenTheOlderSiblingsFillTheNodeLimit()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var root = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "Lineage",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            CreatedAt = createdAt
        };
        var firstSibling = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "Lineage",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            ParentExecutionId = root.Id,
            CreatedAt = createdAt.AddSeconds(1)
        };
        var secondSibling = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "Lineage",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            ParentExecutionId = root.Id,
            CreatedAt = createdAt.AddSeconds(2)
        };
        var requested = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "Lineage",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            ParentExecutionId = root.Id,
            CreatedAt = createdAt.AddSeconds(3)
        };
        var requestedChild = new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "Lineage",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Completed,
            ParentExecutionId = requested.Id,
            CreatedAt = createdAt.AddSeconds(4)
        };
        _context.JobExecutions.AddRange(root, firstSibling, secondSibling, requested, requestedChild);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetLineageAsync(requested.Id, 500, 4).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(4));
            Assert.That(result.Select(execution => execution.Id), Does.Contain(requestedChild.Id));
        }
    }

    [Test]
    public async Task GetLineageAsync_StopsAtMaxNodes_WhenTheChainExceedsTheLimit()
    {
        // Arrange
        var executions = new JobExecution[40];
        Guid? parentId = null;
        for (var index = 0; index < executions.Length; index++)
        {
            executions[index] = new()
            {
                Id = Guid.NewGuid(),
                JobType = "Lineage",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Completed,
                ParentExecutionId = parentId,
                CreatedAt = DateTime.UtcNow.AddSeconds(index)
            };
            parentId = executions[index].Id;
        }

        _context.JobExecutions.AddRange(executions);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.GetLineageAsync(executions[^1].Id, 10, 4).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(4));
            Assert.That(result.Select(execution => execution.Id), Does.Contain(executions[^1].Id));
        }
    }

    [Test]
    public async Task CreateAsync_KeepsOneQueuedExecution_WhenOneSubjectIsEnqueuedForManyAppearances()
    {
        // Arrange
        var jobManager = new JobManager(
            Mock.Of<IJobRuntime>(),
            Mock.Of<IJobRegistry>(registry =>
                registry.ListJobSpecs() == new[] { SubjectMetadataRefreshItemJob.Spec }),
            _subject,
            Mock.Of<IScheduleTriggerRepository>(),
            Mock.Of<IEventTriggerRepository>(),
            Mock.Of<IJobConcurrencyScheduler>(),
            Mock.Of<IJobExecutionScopeFactory>(),
            _timeProvider,
            NullLogger<JobManager>.Instance, _transactionManager);
        var subjectId = Guid.NewGuid();
        var enqueuedIds = new List<Guid>();

        // Act
        for (var appearance = 0; appearance < 12; appearance++)
        {
            enqueuedIds.Add(await jobManager.ExecuteAsync(
                new JobEnqueueRequest
                {
                    JobType = "SubjectMetadataRefreshItem",
                    Parameters = new Dictionary<string, string> { ["subjectId"] = subjectId.ToString("D") },
                    Mode = JobExecutionMode.Queue,
                },
                CancellationToken.None).ConfigureAwait(false));
        }

        // Assert
        var stored = await _context.JobExecutions
            .AsNoTracking()
            .Where(execution => execution.JobType == "SubjectMetadataRefreshItem")
            .ToListAsync()
            .ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(stored[0].Status, Is.EqualTo(JobExecutionStatus.Queued));
            Assert.That(
                stored[0].LockPartition,
                Is.EqualTo($"type:SubjectMetadataRefreshItem|subjectId={subjectId:D}"));
            Assert.That(enqueuedIds.Distinct(), Has.Exactly(1).Items);
        }
    }

    [Test]
    public async Task SaveChangesAsync_WritesTheExecution_WhenTheContextHasNoProviderModelCustomizer()
    {
        // Arrange
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"generated-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 5
        }.ToString();
        using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync().ConfigureAwait(false);
        using (var schemaContext = MagpieDbContextFactory.Create(connectionString, [], []))
        {
            await schemaContext.Database.EnsureCreatedAsync().ConfigureAwait(false);
        }

        using var plainContext = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connectionString)
            .Options);
        plainContext.JobExecutions.Add(new JobExecution
        {
            Id = Guid.NewGuid(),
            JobType = "DownloadImportItem",
            Scope = JobScope.Content,
            Status = JobExecutionStatus.Queued,
            Parameters = new() { ["downloadId"] = Guid.NewGuid().ToString("D") },
            CreatedAt = Now.UtcDateTime
        });

        // Act
        await plainContext.SaveChangesAsync().ConfigureAwait(false);

        // Assert
        var stored = await plainContext.JobExecutions.AsNoTracking().CountAsync().ConfigureAwait(false);
        Assert.That(stored, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TryRunAsync_ReturnsRaceLoss_WhenPartitionBecomesOccupiedDuringUpdate(bool start)
    {
        // Arrange
        var candidate = new JobExecution
        {
            JobType = "candidate",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "partition-race",
            CreatedAt = Now.UtcDateTime
        };
        var competitor = new JobExecution
        {
            JobType = "competitor",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "partition-race",
            CreatedAt = Now.UtcDateTime
        };
        _context.JobExecutions.AddRange(candidate, competitor);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        await _context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER claim_race BEFORE UPDATE OF Status ON job_execution
            WHEN NEW.JobType = 'candidate'
            BEGIN
                UPDATE job_execution SET Status = 2 WHERE JobType = 'competitor';
            END;
            """).ConfigureAwait(false);

        // Act
        var started = start
            ? await _subject.TryStartAsync(candidate.Id, "node-a").ConfigureAwait(false)
            : await _subject.TryClaimAsync(candidate.Id, "node-a").ConfigureAwait(false) is not null;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started, Is.False);
            Assert.That(_events, Is.Empty);
            Assert.That(await _context.JobExecutions.AsNoTracking().Where(e => e.Id == candidate.Id)
                .Select(e => e.Status).SingleAsync().ConfigureAwait(false), Is.EqualTo(JobExecutionStatus.Queued));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TryRunAsync_Throws_WhenUpdateFailsForAnotherReason(bool start)
    {
        // Arrange
        var candidate = new JobExecution
        {
            JobType = "candidate",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Queued,
            LockPartition = "partition-error",
            CreatedAt = Now.UtcDateTime
        };
        _context.JobExecutions.Add(candidate);
        _context.SaveChanges();
        _context.Database.ExecuteSqlRaw("""
            CREATE TRIGGER claim_failure BEFORE UPDATE OF Status ON job_execution
            BEGIN
                SELECT RAISE(ABORT, 'Unrelated failure.');
            END;
            """);

        // Act
        var exception = Assert.ThrowsAsync<SqliteException>(async () =>
        {
            if (start)
            {
                await _subject.TryStartAsync(candidate.Id, "node-a").ConfigureAwait(false);
            }
            else
            {
                await _subject.TryClaimAsync(candidate.Id, "node-a").ConfigureAwait(false);
            }
        });

        // Assert
        Assert.That(exception!.Message, Does.Contain("Unrelated failure."));
    }

    [Test]
    public async Task CreateAsync_RollsBackEnqueue_WhenCoalescingLockIsMissing()
    {
        // Arrange
        await _context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER remove_coalescing_lock AFTER INSERT ON job_execution
            BEGIN
                DELETE FROM job_enqueue_coalescing_lock;
            END;
            """).ConfigureAwait(false);

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await _subject.CreateAsync(
            new JobExecution
            {
                JobType = "test",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Queued,
                LockPartition = "partition-error"
            }, null, default).ConfigureAwait(false));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.StartWith("Expected one job execution coalescing lock row"));
            Assert.That(exception.Message, Does.EndWith("but deleted 0."));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.Zero);
            Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.Zero);
            Assert.That(_events, Is.Empty);
        }
    }

    [Test]
    public void CreateAsync_PreservesOriginalException_WhenLockReleaseAlsoFails()
    {
        // Arrange
        _context.Database.ExecuteSqlRaw("""
            CREATE TRIGGER lock_release_failure BEFORE DELETE ON job_enqueue_coalescing_lock
            BEGIN
                SELECT RAISE(ABORT, 'Release failed.');
            END;
            """);
        var audit = new Mock<IAuditRecordRepository>();
        audit.Setup(a => a.CreateAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Audit failed."));
        var subject = new JobExecutionRepository(
            _context,
            audit.Object,
            _transactionManager,
            Mock.Of<IEventBus>(),
            NullLogger<JobExecutionRepository>.Instance,
            _timeProvider,
            new JobRegistry([], NullLogger<JobRegistry>.Instance));

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await subject.CreateAsync(
            new JobExecution
            {
                JobType = "test",
                Scope = JobScope.System,
                Status = JobExecutionStatus.Queued,
                LockPartition = "partition-error"
            }, null, default).ConfigureAwait(false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Audit failed."));
    }
}
