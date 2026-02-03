using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Jobs;
using Magpie.Api.EndpointGroups;
using Magpie.Models.Api;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Test.Shared;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Api.EndpointGroups;

[TestFixture]
public class JobsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IJobManager> _jobManager = null!;

    [SetUp]
    public void SetUp()
    {
        _jobManager = new Mock<IJobManager>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddTestLogger();
        var authServiceMock = new Mock<IAuthenticationService>();
        authServiceMock
            .Setup(m => m.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.Success(new(
                new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                        new Claim(ClaimTypes.Role, "Admin")
                    ],
                    "Test")),
                "Test")));
        builder.Services.AddSingleton(authServiceMock.Object);
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(null)
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true)
                .Build())
            .AddPolicy(nameof(Role.Viewer), policy => policy.RequireAssertion(_ => true))
            .AddPolicy(nameof(Role.Manager), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton(_jobManager.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, JobsEndpointGroup>();

        var app = builder.Build();

        var apiGroup = app.MapGroup("/api");
        var endpointGroups = app.Services.GetServices<IApiEndpointGroup>();
        foreach (var group in endpointGroups)
        {
            var groupRoute = apiGroup.MapGroup(group.Name);
            group.MapEndpoints(groupRoute);
        }

        app.UseAuthorization();

        _host = app;
        _host.Start();
        _client = _host.GetTestClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
        _host?.Dispose();
    }

    [Test]
    public async Task QueryJobExecutions_ReturnsEmptyList_WhenNoExecutions()
    {
        // Arrange
        _jobManager
            .Setup(m => m.QueryExecutionsAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter?>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 10,
                TotalItems = 0
            });

        // Act
        var response = await _client.GetAsync("/api/jobs").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<PagedList<JobExecution>>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(content.Items)).Empty);
        }
    }

    [Test]
    public async Task QueryJobExecutions_ReturnsExecutions()
    {
        // Arrange
        var execution = new JobExecution { JobType = "TestJob", Status = JobExecutionStatus.Running, Progress = 0.5 };

        _jobManager
            .Setup(m => m.QueryExecutionsAsync(
                It.IsAny<JobExecutionQuery>(),
                It.IsAny<PagingParameter?>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items = [execution],
                PageNumber = 1,
                PageSize = 10,
                TotalItems = 1
            });

        // Act
        var response = await _client.GetAsync("/api/jobs").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<PagedList<JobExecution>>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(content.Items)).With.Count.EqualTo(1));
            Assert.That(content, Has.Property(nameof(content.Items)).With.One
                .With.Property(nameof(JobExecution.Status)).EqualTo(JobExecutionStatus.Running));
            Assert.That(content, Has.Property(nameof(content.Items)).With.One
                .With.Property(nameof(JobExecution.Progress)).EqualTo(0.5));
        }
    }

    [Test]
    public async Task GetJobExecutionById_ReturnsExecution()
    {
        // Arrange
        var id = Guid.NewGuid();
        var execution = new JobExecution
        {
            Id = id,
            JobType = "TestJob",
            Status = JobExecutionStatus.Completed,
            Progress = 100
        };

        _jobManager
            .Setup(m => m.GetExecutionAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        // Act
        var response = await _client.GetAsync($"/api/jobs/{id}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<JobExecution>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(id));
            Assert.That(content, Has.Property(nameof(content.Status)).EqualTo(JobExecutionStatus.Completed));
        }
    }

    [Test]
    public async Task GetJobExecutionById_ReturnsNotFound_WhenExecutionDoesNotExist()
    {
        // Arrange
        var id = Guid.NewGuid();

        _jobManager
            .Setup(m => m.GetExecutionAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(JobExecution?));

        // Act
        var response = await _client.GetAsync($"/api/jobs/{id}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RunJob_ReturnsAccepted()
    {
        // Arrange
        var executionId = Guid.NewGuid();
        var request = new RunJobRequest(
            JobType: "TestJob",
            ExtensionId: null,
            Parameters: new() { ["key1"] = "value1" },
            Mode: JobExecutionMode.Queue);

        _jobManager
            .Setup(m => m.ExecuteAsync(
                request.JobType,
                request.ExtensionId,
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                request.Mode,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executionId);

        // Act
        var response = await _client.PostAsJsonAsync("/api/jobs", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Guid>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Accepted));
            Assert.That(content, Is.EqualTo(executionId));
        }
    }

    [Test]
    public async Task RequestCancellation_ReturnsNoContent()
    {
        // Arrange
        var id = Guid.NewGuid();

        _jobManager
            .Setup(m => m.RequestCancellationAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PostAsync($"/api/jobs/{id}/cancel", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task RequestCancellation_ReturnsNotFound_WhenExecutionDoesNotExist()
    {
        // Arrange
        var id = Guid.NewGuid();

        _jobManager
            .Setup(m => m.RequestCancellationAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PostAsync($"/api/jobs/{id}/cancel", null).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task QueryJobExecutions_WithFilters_PassesFiltersToManager()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var scheduleTriggerId = Guid.NewGuid();

        _jobManager
            .Setup(m => m.QueryExecutionsAsync(
                It.Is<JobExecutionQuery>(q =>
                    q.JobType == "TestJob" &&
                    q.ExtensionId == extensionId &&
                    q.ScheduleTriggerId == scheduleTriggerId &&
                    q.Status == JobExecutionStatus.Running),
                It.IsAny<PagingParameter?>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<JobExecution>
            {
                Items = [],
                PageNumber = 1,
                PageSize = 10,
                TotalItems = 0
            });

        // Act
        var response = await _client.GetAsync(
            $"/api/jobs?jobType=TestJob&extensionId={extensionId}&scheduleTriggerId={scheduleTriggerId}&status=Running")
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
        _jobManager.Verify(
            m => m.QueryExecutionsAsync(
                It.Is<JobExecutionQuery>(q =>
                    q.JobType == "TestJob" &&
                    q.ExtensionId == extensionId &&
                    q.ScheduleTriggerId == scheduleTriggerId &&
                    q.Status == JobExecutionStatus.Running),
                It.IsAny<PagingParameter?>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
