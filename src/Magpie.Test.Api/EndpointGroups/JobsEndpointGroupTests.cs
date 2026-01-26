using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Data;
using Magpie.Api.EndpointGroups;
using Magpie.Models.Jobs;
using Magpie.Models.Result;
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
    private Mock<IJobExecutionRepository> _repositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _repositoryMock = new Mock<IJobExecutionRepository>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddTestLogger();
        var authServiceMock = new Mock<IAuthenticationService>();
        authServiceMock
            .Setup(x => x.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity("Test")), "Test")));
        builder.Services.AddSingleton(authServiceMock.Object);
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(null)
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true)
                .Build());

        builder.Services.AddSingleton(_repositoryMock.Object);
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
    public async Task GetAllJobExecutions_ReturnsEmptyArray_WhenNoExecutions()
    {
        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<JobExecution>>([]));

        var response = await _client.GetAsync("/api/jobs").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<JobExecution[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllJobExecutions_ReturnsExecutions()
    {
        var jobTemplateId = Guid.NewGuid();
        var executions = new List<JobExecution>
        {
            new(
                Id: Guid.NewGuid(),
                JobTemplateId: jobTemplateId,
                ScheduleTriggerId: null,
                EventTriggerId: null,
                EventId: null,
                Status: JobExecutionStatus.Running,
                Progress: 0.5,
                Message: "Processing",
                Parameters: new Dictionary<string, string?>(),
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: null,
                CreatedAt: DateTimeOffset.UtcNow,
                CreatedByUserId: null,
                ModifiedAt: null,
                ModifiedByUserId: null)
        };

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<JobExecution>>(executions));

        var response = await _client.GetAsync("/api/jobs").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<JobExecution[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One
                .With.Property(nameof(JobExecution.Status)).EqualTo(JobExecutionStatus.Running));
            Assert.That(content, Has.One
                .With.Property(nameof(JobExecution.Progress)).EqualTo(0.5));
            Assert.That(content, Has.One
                .With.Property(nameof(JobExecution.Message)).EqualTo("Processing"));
        }
    }

    [Test]
    public async Task GetJobExecutionById_ReturnsExecution()
    {
        var id = Guid.NewGuid();
        var jobTemplateId = Guid.NewGuid();
        var execution = new JobExecution(
            Id: id,
            JobTemplateId: jobTemplateId,
            ScheduleTriggerId: null,
            EventTriggerId: null,
            EventId: null,
            Status: JobExecutionStatus.Completed,
            Progress: 1.0,
            Message: "Done",
            Parameters: new Dictionary<string, string?>(),
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<JobExecution?>(execution));

        var response = await _client.GetAsync($"/api/jobs/{id}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<JobExecution>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(JobExecution.Id)).EqualTo(id));
            Assert.That(content, Has.Property(nameof(JobExecution.Status)).EqualTo(JobExecutionStatus.Completed));
        }
    }

    [Test]
    public async Task GetJobExecutionById_ReturnsNotFound_WhenExecutionDoesNotExist()
    {
        var id = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<JobExecution?>(null));

        var response = await _client.GetAsync($"/api/jobs/{id}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetJobExecutionById_ReturnsParameters()
    {
        var id = Guid.NewGuid();
        var jobTemplateId = Guid.NewGuid();
        var parameters = new Dictionary<string, string?> { ["key1"] = "value1", ["key2"] = "value2" };
        var execution = new JobExecution(
            Id: id,
            JobTemplateId: jobTemplateId,
            ScheduleTriggerId: null,
            EventTriggerId: null,
            EventId: null,
            Status: JobExecutionStatus.Running,
            Progress: null,
            Message: null,
            Parameters: parameters,
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<JobExecution?>(execution));

        var response = await _client.GetAsync($"/api/jobs/{id}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<JobExecution>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content!.Parameters, Has.Count.EqualTo(2));
            Assert.That(content.Parameters["key1"], Is.EqualTo("value1"));
            Assert.That(content.Parameters["key2"], Is.EqualTo("value2"));
        }
    }

    [Test]
    public async Task GetJobExecutionsByJobTemplate_ReturnsFilteredExecutions()
    {
        var jobTemplateId = Guid.NewGuid();
        var executions = new List<JobExecution>
        {
            new(
                Id: Guid.NewGuid(),
                JobTemplateId: jobTemplateId,
                ScheduleTriggerId: null,
                EventTriggerId: null,
                EventId: null,
                Status: JobExecutionStatus.Running,
                Progress: null,
                Message: null,
                Parameters: new Dictionary<string, string?>(),
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: null,
                CreatedAt: DateTimeOffset.UtcNow,
                CreatedByUserId: null,
                ModifiedAt: null,
                ModifiedByUserId: null)
        };

        _repositoryMock
            .Setup(r => r.GetByJobTemplateIdAsync(jobTemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<JobExecution>>(executions));

        var response = await _client.GetAsync($"/api/jobs/by-job-template/{jobTemplateId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<JobExecution[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(JobExecution.JobTemplateId)).EqualTo(jobTemplateId));
        }
    }

    [Test]
    public async Task GetJobExecutionsByScheduleTrigger_ReturnsFilteredExecutions()
    {
        var scheduleTriggerId = Guid.NewGuid();
        var jobTemplateId = Guid.NewGuid();
        var executions = new List<JobExecution>
        {
            new(
                Id: Guid.NewGuid(),
                JobTemplateId: jobTemplateId,
                ScheduleTriggerId: scheduleTriggerId,
                EventTriggerId: null,
                EventId: null,
                Status: JobExecutionStatus.Completed,
                Progress: null,
                Message: null,
                Parameters: new Dictionary<string, string?>(),
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: null,
                CreatedAt: DateTimeOffset.UtcNow,
                CreatedByUserId: null,
                ModifiedAt: null,
                ModifiedByUserId: null)
        };

        _repositoryMock
            .Setup(r => r.GetByScheduleTriggerIdAsync(scheduleTriggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<JobExecution>>(executions));

        var response = await _client
            .GetAsync($"/api/jobs/by-schedule-trigger/{scheduleTriggerId}")
            .ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<JobExecution[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One
                .With.Property(nameof(JobExecution.ScheduleTriggerId)).EqualTo(scheduleTriggerId));
        }
    }

    [Test]
    public async Task GetJobExecutionsByEventTrigger_ReturnsFilteredExecutions()
    {
        var eventTriggerId = Guid.NewGuid();
        var jobTemplateId = Guid.NewGuid();
        var executions = new List<JobExecution>
        {
            new(
                Id: Guid.NewGuid(),
                JobTemplateId: jobTemplateId,
                ScheduleTriggerId: null,
                EventTriggerId: eventTriggerId,
                EventId: null,
                Status: JobExecutionStatus.Completed,
                Progress: null,
                Message: null,
                Parameters: new Dictionary<string, string?>(),
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: null,
                CreatedAt: DateTimeOffset.UtcNow,
                CreatedByUserId: null,
                ModifiedAt: null,
                ModifiedByUserId: null)
        };

        _repositoryMock
            .Setup(r => r.GetByEventTriggerIdAsync(eventTriggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<JobExecution>>(executions));

        var response = await _client.GetAsync($"/api/jobs/by-event-trigger/{eventTriggerId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<JobExecution[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(JobExecution.EventTriggerId)).EqualTo(eventTriggerId));
        }
    }

    [Test]
    public async Task GetJobExecutionsByStatus_ReturnsFilteredExecutions()
    {
        var jobTemplateId = Guid.NewGuid();
        var executions = new List<JobExecution>
        {
            new(
                Id: Guid.NewGuid(),
                JobTemplateId: jobTemplateId,
                ScheduleTriggerId: null,
                EventTriggerId: null,
                EventId: null,
                Status: JobExecutionStatus.Running,
                Progress: null,
                Message: null,
                Parameters: new Dictionary<string, string?>(),
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: null,
                CreatedAt: DateTimeOffset.UtcNow,
                CreatedByUserId: null,
                ModifiedAt: null,
                ModifiedByUserId: null),
            new(
                Id: Guid.NewGuid(),
                JobTemplateId: jobTemplateId,
                ScheduleTriggerId: null,
                EventTriggerId: null,
                EventId: null,
                Status: JobExecutionStatus.Running,
                Progress: null,
                Message: null,
                Parameters: new Dictionary<string, string?>(),
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: null,
                CreatedAt: DateTimeOffset.UtcNow,
                CreatedByUserId: null,
                ModifiedAt: null,
                ModifiedByUserId: null)
        };

        _repositoryMock
            .Setup(r => r.GetByStatusAsync(JobExecutionStatus.Running, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<JobExecution>>(executions));

        var response = await _client.GetAsync("/api/jobs/by-status/Running").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<JobExecution[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(2));
            Assert.That(content, Has.All
                .With.Property(nameof(JobExecution.Status)).EqualTo(JobExecutionStatus.Running));
        }
    }

    [Test]
    public async Task CreateJobExecution_CreatesAndReturnsExecution()
    {
        var jobTemplateId = Guid.NewGuid();
        var request = new JobExecution(
            Id: Guid.Empty,
            JobTemplateId: jobTemplateId,
            ScheduleTriggerId: null,
            EventTriggerId: null,
            EventId: null,
            Status: default,
            Progress: null,
            Message: null,
            Parameters: null!,
            StartedAt: default,
            CompletedAt: null,
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<JobExecution>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobExecution je, Guid? _, CancellationToken _) => Result.Success(je));

        var response = await _client.PostAsJsonAsync("/api/jobs", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<JobExecution>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(JobExecution.JobTemplateId)).EqualTo(jobTemplateId));
            Assert.That(content, Has.Property(nameof(JobExecution.Status)).EqualTo(JobExecutionStatus.Queued));
            Assert.That(response.Headers.Location?.ToString(), Does.Contain($"/api/jobs/{content!.Id}"));
        }
    }

    [Test]
    public async Task CreateJobExecution_WithParameters_CreatesExecution()
    {
        var jobTemplateId = Guid.NewGuid();
        var parameters = new Dictionary<string, string?> { ["key1"] = "value1", ["key2"] = "value2" };
        var request = new JobExecution(
            Id: Guid.Empty,
            JobTemplateId: jobTemplateId,
            ScheduleTriggerId: null,
            EventTriggerId: null,
            EventId: null,
            Status: JobExecutionStatus.Running,
            Progress: 0.25,
            Message: "Processing",
            Parameters: parameters,
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _repositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<JobExecution>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobExecution je, Guid? _, CancellationToken _) => Result.Success(je));

        var response = await _client.PostAsJsonAsync("/api/jobs", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<JobExecution>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(JobExecution.Status)).EqualTo(JobExecutionStatus.Running));
            Assert.That(content, Has.Property(nameof(JobExecution.Progress)).EqualTo(0.25));
            Assert.That(content, Has.Property(nameof(JobExecution.Message)).EqualTo("Processing"));
            Assert.That(content!.Parameters, Has.Count.EqualTo(2));
            Assert.That(content.Parameters["key1"], Is.EqualTo("value1"));
        }
    }

    [Test]
    public async Task PatchJobExecution_UpdatesExecution()
    {
        var id = Guid.NewGuid();
        var jobTemplateId = Guid.NewGuid();
        var existing = new JobExecution(
            Id: id,
            JobTemplateId: jobTemplateId,
            ScheduleTriggerId: null,
            EventTriggerId: null,
            EventId: null,
            Status: JobExecutionStatus.Queued,
            Progress: null,
            Message: null,
            Parameters: new Dictionary<string, string?>(),
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        var request = new JobExecution(
            Id: Guid.Empty,
            JobTemplateId: jobTemplateId,
            ScheduleTriggerId: null,
            EventTriggerId: null,
            EventId: null,
            Status: JobExecutionStatus.Running,
            Progress: 0.5,
            Message: "Processing",
            Parameters: null!,
            StartedAt: existing.StartedAt,
            CompletedAt: null,
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<JobExecution?>(existing));

        _repositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<JobExecution>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var response = await _client.PatchAsJsonAsync($"/api/jobs/{id}", request).ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        _repositoryMock.Verify(r => r.UpdateAsync(
            It.Is<JobExecution>(je => je.Status == JobExecutionStatus.Running && je.Progress == 0.5),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task PatchJobExecution_ReturnsNotFound_WhenExecutionDoesNotExist()
    {
        var id = Guid.NewGuid();
        var jobTemplateId = Guid.NewGuid();
        var request = new JobExecution(
            Id: Guid.Empty,
            JobTemplateId: jobTemplateId,
            ScheduleTriggerId: null,
            EventTriggerId: null,
            EventId: null,
            Status: JobExecutionStatus.Running,
            Progress: null,
            Message: null,
            Parameters: null!,
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: null,
            CreatedAt: default,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<JobExecution?>(null));

        var response = await _client.PatchAsJsonAsync($"/api/jobs/{id}", request).ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteJobExecution_DeletesExecution()
    {
        var id = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.DeleteAsync(id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var response = await _client.DeleteAsync($"/api/jobs/{id}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteJobExecution_ReturnsNotFound_WhenExecutionDoesNotExist()
    {
        var id = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.DeleteAsync(id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        var response = await _client.DeleteAsync($"/api/jobs/{id}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteJobExecutionsByJobTemplate_DeletesAllExecutions()
    {
        var jobTemplateId = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.DeleteByJobTemplateIdAsync(jobTemplateId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(2));

        var response = await _client.DeleteAsync($"/api/jobs/by-job-template/{jobTemplateId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<int>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.EqualTo(2));
        }
    }
}
