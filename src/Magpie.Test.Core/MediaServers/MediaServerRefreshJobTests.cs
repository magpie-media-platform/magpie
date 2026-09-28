using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Content;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.MediaServers;

[TestFixture]
public sealed class MediaServerRefreshJobTests
{
    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    private Mock<IMediaServerRepository> _mediaServers = null!;
    private Mock<IContentHierarchyItemRepository> _contentHierarchyItems = null!;
    private Mock<IContentFileService> _contentFiles = null!;
    private Mock<IMaterializedMetadataRepository> _materializedMetadata = null!;
    private List<IMediaServerIntegration> _integrations = null!;
    private JobContext _jobContext = null!;
    private IJob _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _mediaServers = new Mock<IMediaServerRepository>();
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _contentHierarchyItems = new Mock<IContentHierarchyItemRepository>();
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _contentFiles = new Mock<IContentFileService>();
        _materializedMetadata = new Mock<IMaterializedMetadataRepository>();
        _materializedMetadata
            .Setup(r => r.QueryAsync(
                It.IsAny<MetadataCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MaterializedMetadata>());
        _integrations = [];

        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: Guid.NewGuid(),
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>(),
            Event: null);

        _subject = new MediaServerRefreshJob(
            _mediaServers.Object,
            _contentHierarchyItems.Object,
            _contentFiles.Object,
            _materializedMetadata.Object,
            _integrations,
            NullLogger<MediaServerRefreshJob>.Instance,
            _progressReporter);
    }

    [TestCase(
        true,
        IntegrationHealthStatus.Healthy,
        1,
        TestName = "ExecuteAsync_RefreshesTheServer_WhenItIsEnabledAndHealthy")]
    [TestCase(
        true,
        IntegrationHealthStatus.Degraded,
        1,
        TestName = "ExecuteAsync_RefreshesTheServer_WhenItIsEnabledAndDegraded")]
    [TestCase(
        false,
        IntegrationHealthStatus.Healthy,
        0,
        TestName = "ExecuteAsync_SkipsTheServer_WhenItIsDisabled")]
    [TestCase(
        true,
        IntegrationHealthStatus.Unhealthy,
        0,
        TestName = "ExecuteAsync_SkipsTheServer_WhenItIsUnhealthy")]
    public async Task ExecuteAsync_RefreshesEligibleServers_WhenServerEligibilityDiffers(
        bool enabled,
        IntegrationHealthStatus status,
        int expectedCalls)
    {
        // Arrange
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "Server",
                    ProviderType = "fake",
                    Enabled = enabled,
                    Status = status,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                }
            ]);
        var integration = new Mock<IMediaServerIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        var refreshable = integration.As<IRefreshableMediaServerIntegration>();
        refreshable
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        refreshable.Verify(
            i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedCalls));
    }

    [Test]
    public async Task ExecuteAsync_RefreshesNothing_WhenNoMediaServerIsConfigured()
    {
        // Arrange
        var integration = new Mock<IMediaServerIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        var refreshable = integration.As<IRefreshableMediaServerIntegration>();
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        refreshable.Verify(
            i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Test]
    public async Task ExecuteAsync_SkipsServer_WhenIntegrationIsNotRefreshable()
    {
        // Arrange
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "Server",
                    ProviderType = "fake",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        var nonRefreshable = new Mock<IMediaServerIntegration>();
        nonRefreshable.SetupGet(i => i.ProviderType).Returns("fake");

        var refreshableIntegration = new Mock<IMediaServerIntegration>();
        refreshableIntegration.SetupGet(i => i.ProviderType).Returns("fake");
        var refreshable = refreshableIntegration.As<IRefreshableMediaServerIntegration>();
        refreshable
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        _integrations.Add(nonRefreshable.Object);
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);
        _integrations.Clear();
        _integrations.Add(refreshableIntegration.Object);
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        refreshable.Verify(
            i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SkipsServer_WhenNoIntegrationIsRegisteredForProviderType()
    {
        // Arrange
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "A-Unknown",
                    ProviderType = "missing",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                },
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "B-Known",
                    ProviderType = "fake",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        var integration = new Mock<IMediaServerIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        var refreshable = integration.As<IRefreshableMediaServerIntegration>();
        refreshable
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        refreshable.Verify(
            i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_ContinuesProcessingSiblings_WhenSingleServerThrows()
    {
        // Arrange
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "A-Failing",
                    ProviderType = "fake-fail",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                },
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "B-Working",
                    ProviderType = "fake-ok",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        var failingIntegration = new Mock<IMediaServerIntegration>();
        failingIntegration.SetupGet(i => i.ProviderType).Returns("fake-fail");
        var failing = failingIntegration.As<IRefreshableMediaServerIntegration>();
        failing
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Integration failed."));

        var workingIntegration = new Mock<IMediaServerIntegration>();
        workingIntegration.SetupGet(i => i.ProviderType).Returns("fake-ok");
        var working = workingIntegration.As<IRefreshableMediaServerIntegration>();
        working
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _integrations.Add(failingIntegration.Object);
        _integrations.Add(workingIntegration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            failing.Verify(
                i => i.RefreshForContentAsync(
                    It.IsAny<MediaServer>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            working.Verify(
                i => i.RefreshForContentAsync(
                    It.IsAny<MediaServer>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public void ExecuteAsync_PropagatesCancellation_WhenRefreshIsCanceled()
    {
        // Arrange
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "Server",
                    ProviderType = "fake",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                }
            ]);
        var integration = new Mock<IMediaServerIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        var refreshable = integration.As<IRefreshableMediaServerIntegration>();
        _integrations.Add(integration.Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        async Task Act() => await _subject.ExecuteAsync(_jobContext, cts.Token).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Act, Throws.InstanceOf<OperationCanceledException>());
            refreshable.Verify(
                i => i.RefreshForContentAsync(
                    It.IsAny<MediaServer>(),
                    It.IsAny<IReadOnlyDictionary<string, string>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task ExecuteAsync_IsolatesIntegrationOperationCanceled_WhenCallerTokenNotCancelled()
    {
        // Arrange
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "A-Failing",
                    ProviderType = "fake-fail",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                },
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "B-Working",
                    ProviderType = "fake-ok",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        var failingIntegration = new Mock<IMediaServerIntegration>();
        failingIntegration.SetupGet(i => i.ProviderType).Returns("fake-fail");
        failingIntegration.As<IRefreshableMediaServerIntegration>()
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var workingIntegration = new Mock<IMediaServerIntegration>();
        workingIntegration.SetupGet(i => i.ProviderType).Returns("fake-ok");
        var working = workingIntegration.As<IRefreshableMediaServerIntegration>();
        working
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _integrations.Add(failingIntegration.Object);
        _integrations.Add(workingIntegration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        working.Verify(
            i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_DoesNotCoalesce_WhenUpstreamRefreshDidNotHappen()
    {
        // Arrange
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "Server",
                    ProviderType = "fake",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        var returnRefreshed = false;
        var integration = new Mock<IMediaServerIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        var refreshable = integration.As<IRefreshableMediaServerIntegration>();
        refreshable
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => returnRefreshed);
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext with { ExecutionId = Guid.NewGuid() }).ConfigureAwait(false);
        returnRefreshed = true;
        await _subject.ExecuteAsync(_jobContext with { ExecutionId = Guid.NewGuid() }).ConfigureAwait(false);

        // Assert
        refreshable.Verify(
            i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task ExecuteAsync_SkipsServer_WhenServerContentTypesExcludeScope()
    {
        // Arrange
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "Server",
                    ProviderType = "fake",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = ["type-c"],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "type-a",
            Level = "level",
            Slug = "item",
            Name = "Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);

        var integration = new Mock<IMediaServerIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        var refreshable = integration.As<IRefreshableMediaServerIntegration>();
        refreshable
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _integrations.Add(integration.Object);

        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["contentHierarchyItemId"] = item.Id.ToString("D")
            }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        refreshable.Verify(
            i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_RefreshesServer_WhenServerContentTypesIncludeScope()
    {
        // Arrange
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "Server",
                    ProviderType = "fake",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = ["type-a", "type-c"],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "type-a",
            Level = "level",
            Slug = "item",
            Name = "Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);

        var integration = new Mock<IMediaServerIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration.SetupGet(i => i.SupportedContentTypes).Returns([]);
        var refreshable = integration.As<IRefreshableMediaServerIntegration>();
        refreshable
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _integrations.Add(integration.Object);

        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["contentHierarchyItemId"] = item.Id.ToString("D")
            }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        refreshable.Verify(
            i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_RefreshesServer_WhenServerContentTypesEmpty()
    {
        // Arrange
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "Server",
                    ProviderType = "fake",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "anything",
            Level = "level",
            Slug = "item",
            Name = "Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);

        var integration = new Mock<IMediaServerIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration.SetupGet(i => i.SupportedContentTypes).Returns([]);
        var refreshable = integration.As<IRefreshableMediaServerIntegration>();
        refreshable
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _integrations.Add(integration.Object);

        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["contentHierarchyItemId"] = item.Id.ToString("D")
            }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        refreshable.Verify(
            i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_SkipsServer_WhenIntegrationDoesNotSupportContentType()
    {
        // Arrange
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "Server",
                    ProviderType = "fake",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "type-c",
            Level = "level",
            Slug = "item",
            Name = "Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);

        var integration = new Mock<IMediaServerIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration.SetupGet(i => i.SupportedContentTypes).Returns(["type-a"]);
        var refreshable = integration.As<IRefreshableMediaServerIntegration>();
        refreshable
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _integrations.Add(integration.Object);

        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["contentHierarchyItemId"] = item.Id.ToString("D")
            }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        refreshable.Verify(
            i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task ExecuteAsync_RefreshesServer_WhenContentTypeMatches()
    {
        // Arrange
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "Server",
                    ProviderType = "fake",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "type-a",
            Level = "level",
            Slug = "item",
            Name = "Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);

        var integration = new Mock<IMediaServerIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration.SetupGet(i => i.SupportedContentTypes).Returns(["type-a"]);
        var refreshable = integration.As<IRefreshableMediaServerIntegration>();
        refreshable
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _integrations.Add(integration.Object);

        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["contentHierarchyItemId"] = item.Id.ToString("D")
            }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        refreshable.Verify(
            i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_RefreshesServer_WhenIntegrationSupportsAllContentTypes()
    {
        // Arrange
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "Server",
                    ProviderType = "fake",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = Guid.NewGuid(),
            ContentType = "anything",
            Level = "level",
            Slug = "item",
            Name = "Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);

        var integration = new Mock<IMediaServerIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration.SetupGet(i => i.SupportedContentTypes).Returns([]);
        var refreshable = integration.As<IRefreshableMediaServerIntegration>();
        refreshable
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _integrations.Add(integration.Object);

        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["contentHierarchyItemId"] = item.Id.ToString("D")
            }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        refreshable.Verify(
            i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_RefreshesServer_WhenContentHierarchyItemIdIsNull()
    {
        // Arrange
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MediaServer
                {
                    Id = Guid.NewGuid(),
                    Name = "Server",
                    ProviderType = "fake",
                    Enabled = true,
                    Status = IntegrationHealthStatus.Healthy,
                    ContentTypes = [],
                    Settings = [],
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        var integration = new Mock<IMediaServerIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration.SetupGet(i => i.SupportedContentTypes).Returns(["type-a"]);
        var refreshable = integration.As<IRefreshableMediaServerIntegration>();
        refreshable
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _integrations.Add(integration.Object);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        refreshable.Verify(
            i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_HandsOverComposedScopePath_WhenRootLevelDeclaresSubfolder()
    {
        // Arrange
        var server = new MediaServer
        {
            Id = Guid.NewGuid(),
            Name = "Server",
            ProviderType = "fake",
            Enabled = true,
            Status = IntegrationHealthStatus.Healthy,
            ContentTypes = [],
            Settings = [],
            CreatedAt = DateTime.UtcNow
        };
        _mediaServers
            .Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([server]);

        var rootFolder = new RootFolder
        {
            Id = Guid.NewGuid(),
            Name = "Root",
            Path = "/data/type-a",
            ContentType = "type-a",
            CreatedAt = DateTime.UtcNow
        };
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = Guid.NewGuid(),
            RootFolderId = rootFolder.Id,
            EffectiveRootFolderId = rootFolder.Id,
            ContentType = "type-a",
            Level = "root",
            Slug = "item",
            Name = "Item",
            Path = "Item",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _contentHierarchyItems
            .Setup(r => r.GetLineageAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([item]);
        _contentFiles
            .Setup(service => service.GetEffectivePathAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("D:/library/items/Item");

        var integration = new Mock<IMediaServerIntegration>();
        integration.SetupGet(i => i.ProviderType).Returns("fake");
        integration.SetupGet(i => i.SupportedContentTypes).Returns([]);
        var refreshable = integration.As<IRefreshableMediaServerIntegration>();
        refreshable
            .Setup(i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _integrations.Add(integration.Object);

        var context = _jobContext with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["contentHierarchyItemId"] = item.Id.ToString("D")
            }
        };

        // Act
        await _subject.ExecuteAsync(context).ConfigureAwait(false);

        // Assert
        refreshable.Verify(
            i => i.RefreshForContentAsync(
                It.IsAny<MediaServer>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        var refresh = integration.Invocations
            .Single(i => i.Method.Name == nameof(IRefreshableMediaServerIntegration.RefreshForContentAsync));
        using (Assert.EnterMultipleScope())
        {
            Assert.That((string)refresh.Arguments[2], Is.EqualTo("type-a"));
            Assert.That(
                (string)refresh.Arguments[3],
                Is.EqualTo("D:/library/items/Item"));
            Assert.That(((MediaServer)refresh.Arguments[0]).Id, Is.EqualTo(server.Id));
        }
    }
}
