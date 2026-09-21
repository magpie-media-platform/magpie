using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Core.Events;
using Magpie.Models;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Events;

[TestFixture]
public sealed class EventBusTests
{
    private const string SourceName = "Magpie.Test.Core.Events.EventBusTests";

    private static readonly MagpieHostEnvironment HostEnvironment = new(
        "node-a",
        IsClustered: false,
        IsSupervised: false,
        SystemRoot: string.Empty,
        ExtensionsRoot: string.Empty,
        ConfigRoot: string.Empty,
        DataRoot: string.Empty,
        StartedAt: new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc));

    private static readonly MagpieEvent PublishedEvent = new(
        new Guid("04f98de5-4c2e-4c82-8477-0a738259abf4"),
        "download.completed",
        new Dictionary<string, string>(),
        new DateTime(2026, 6, 19, 12, 30, 0, DateTimeKind.Utc),
        new DateTime(2026, 6, 19, 12, 30, 1, DateTimeKind.Utc),
        NodeId: null);

    private Mock<IJobExecutionScopeFactory> _jobScopes = null!;
    private ActivityListener _activityListener = null!;
    private ActivitySource _activitySource = null!;
    private EventBus _subject = null!;
    private IDisposable _subscription = null!;
    private MagpieEvent? _published;

    [SetUp]
    public void SetUp()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_activityListener);
        _activitySource = new ActivitySource(SourceName);
        _jobScopes = new Mock<IJobExecutionScopeFactory>();
        _subject = new EventBus(HostEnvironment, _jobScopes.Object, TimeProvider.System);
        _published = null;
        _subscription = _subject.Events.Subscribe(candidate => _published = candidate);
    }

    [TearDown]
    public void TearDown()
    {
        _subscription.Dispose();
        _subject.Dispose();
        _activitySource.Dispose();
        _activityListener.Dispose();
        Activity.Current = null;
    }

    [Test]
    public async Task PublishAsync_StampsTraceContext_WhenAnActivityIsRecording()
    {
        // Arrange
        using var activity = _activitySource.StartActivity("publisher");
        var traceId = activity!.TraceId.ToHexString();

        // Act
        await _subject.PublishAsync(PublishedEvent).ConfigureAwait(false);

        // Assert
        Assert.That(_published?.TraceContext?["traceparent"], Does.Contain(traceId));
    }

    [Test]
    public async Task PublishAsync_LeavesTraceContextNull_WhenNoActivityIsRunning()
    {
        // Arrange
        Activity.Current = null;

        // Act
        await _subject.PublishAsync(PublishedEvent).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_published, Is.Not.Null);
            Assert.That(_published!.TraceContext, Is.Null);
        }
    }

    [Test]
    public async Task PublishAsync_KeepsTheSuppliedTraceContext_WhenTheEventAlreadyCarriesOne()
    {
        // Arrange
        using var activity = _activitySource.StartActivity("publisher");
        var carried = PublishedEvent with
        {
            TraceContext = new Dictionary<string, string>
            {
                ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
            }
        };

        // Act
        await _subject.PublishAsync(carried).ConfigureAwait(false);

        // Assert
        Assert.That(
            _published?.TraceContext?["traceparent"],
            Is.EqualTo("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"));
    }

    [Test]
    public async Task PublishAsync_StampsTheAmbientCorrelation_WhenTheEventCarriesNone()
    {
        // Arrange
        _jobScopes
            .SetupGet(scopes => scopes.CurrentExecutionId)
            .Returns(new Guid("6b1f1d2e-9f0a-4a13-9a2c-3f5b7c8d9e01"));
        _jobScopes
            .SetupGet(scopes => scopes.CurrentCorrelationId)
            .Returns(new Guid("8c2e3a44-1b5d-4e77-9f30-2a6c4d8e0b12"));

        // Act
        await _subject.PublishAsync(PublishedEvent).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _published?.CorrelationId,
                Is.EqualTo(new Guid("8c2e3a44-1b5d-4e77-9f30-2a6c4d8e0b12")));
            Assert.That(
                _published?.JobExecutionId,
                Is.EqualTo(new Guid("6b1f1d2e-9f0a-4a13-9a2c-3f5b7c8d9e01")));
        }
    }

    [Test]
    public async Task PublishAsync_KeepsTheSuppliedCorrelation_WhenTheEventAlreadyCarriesOne()
    {
        // Arrange
        _jobScopes
            .SetupGet(scopes => scopes.CurrentCorrelationId)
            .Returns(new Guid("8c2e3a44-1b5d-4e77-9f30-2a6c4d8e0b12"));
        var carried = PublishedEvent with
        {
            CorrelationId = new Guid("1d4f6b90-7c21-4e58-8a03-5e9b2c7f4a68")
        };

        // Act
        await _subject.PublishAsync(carried).ConfigureAwait(false);

        // Assert
        Assert.That(
            _published?.CorrelationId,
            Is.EqualTo(new Guid("1d4f6b90-7c21-4e58-8a03-5e9b2c7f4a68")));
    }

    [Test]
    public async Task PublishAsync_LeavesCorrelationNull_WhenNoJobScopeIsAmbient()
    {
        // Act
        await _subject.PublishAsync(PublishedEvent).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_published?.CorrelationId, Is.Null);
            Assert.That(_published?.JobExecutionId, Is.Null);
        }
    }
}
