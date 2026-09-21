using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Runtime;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Runtime;

[TestFixture]
public sealed class DefaultJobRetryPolicyTests
{
    private const string TraceSourceName = "Magpie.Test.Core.Jobs.Runtime.DefaultJobRetryPolicyTests";

    private const string PublisherTraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    private ActivityListener _activityListener = null!;
    private ActivitySource _activitySource = null!;
    private Activity _activity = null!;
    private DefaultJobRetryPolicy _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == TraceSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_activityListener);
        _activitySource = new ActivitySource(TraceSourceName);
        _activity = _activitySource.StartActivity("retry-scheduler")!;
        _subject = new DefaultJobRetryPolicy(
            Mock.Of<ISettingsRepository>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<DefaultJobRetryPolicy>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _activity.Dispose();
        _activitySource.Dispose();
        _activityListener.Dispose();
    }

    [Test]
    public async Task BuildRetryAsync_KeepsTheOriginatingEventTrace_WhenTheFailedExecutionCarriesOne()
    {
        // Arrange
        var failed = new JobExecution
        {
            Id = new Guid("74c53913-fce2-4f29-a7f1-58c1ce338b89"),
            JobType = "EventTriggeredJob",
            EventTriggerId = new Guid("04f98de5-4c2e-4c82-8477-0a738259abf4"),
            Attempt = 1,
            Status = JobExecutionStatus.Failed,
            TriggerContext = new Dictionary<string, string>
            {
                ["traceparent"] = PublisherTraceParent
            }
        };

        // Act
        var retry = await _subject.BuildRetryAsync(failed).ConfigureAwait(false);

        // Assert
        Assert.That(retry?.TriggerContext["traceparent"], Is.EqualTo(PublisherTraceParent));
    }

    [Test]
    public async Task BuildRetryAsync_InjectsTheCurrentTrace_WhenTheFailedExecutionCarriesNone()
    {
        // Arrange
        var failed = new JobExecution
        {
            Id = new Guid("74c53913-fce2-4f29-a7f1-58c1ce338b89"),
            JobType = "ScheduledJob",
            Attempt = 1,
            Status = JobExecutionStatus.Failed
        };
        var traceId = _activity.TraceId.ToHexString();

        // Act
        var retry = await _subject.BuildRetryAsync(failed).ConfigureAwait(false);

        // Assert
        Assert.That(retry?.TriggerContext["traceparent"], Does.Contain(traceId));
    }

    [Test]
    public async Task BuildRetryAsync_PreservesLineage_WhenTheFailedExecutionHasAParent()
    {
        // Arrange
        var failed = new JobExecution
        {
            Id = new Guid("65311772-d2ad-48c2-bbef-1421e0f9e0de"),
            JobType = "TestJob",
            Scope = JobScope.System,
            Status = JobExecutionStatus.Failed,
            Attempt = 1,
            ParentExecutionId = new Guid("656b52cc-4717-4dfe-955b-9720e2228b82"),
            CorrelationId = new Guid("e55a4d52-6b80-49a5-ac95-034f3f98a239"),
            LockPartition = "TestJob"
        };

        // Act
        var retry = await _subject.BuildRetryAsync(failed).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retry?.ParentExecutionId, Is.EqualTo(new Guid("656b52cc-4717-4dfe-955b-9720e2228b82")));
            Assert.That(retry?.OriginalExecutionId, Is.EqualTo(new Guid("65311772-d2ad-48c2-bbef-1421e0f9e0de")));
            Assert.That(retry?.Status, Is.EqualTo(JobExecutionStatus.Queued));
            Assert.That(retry?.CorrelationId, Is.EqualTo(new Guid("e55a4d52-6b80-49a5-ac95-034f3f98a239")));
            Assert.That(retry?.LockPartition, Is.EqualTo("TestJob"));
        }
    }
}
