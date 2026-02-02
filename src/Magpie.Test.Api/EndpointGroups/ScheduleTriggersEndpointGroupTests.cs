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
using Magpie.Abstractions.Repositories;
using Magpie.Api.EndpointGroups;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;
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
public class ScheduleTriggersEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IScheduleTriggerRepository> _scheduleTriggerRepository = null!;
    private Mock<IJobManager> _jobManager = null!;

    [SetUp]
    public void SetUp()
    {
        _scheduleTriggerRepository = new Mock<IScheduleTriggerRepository>();
        _jobManager = new Mock<IJobManager>();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders().AddTestLogger();
        builder.WebHost.UseTestServer();

        var authServiceMock = new Mock<IAuthenticationService>();
        authServiceMock
            .Setup(m => m.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity("Test")), "Test")));
        builder.Services.AddSingleton(authServiceMock.Object);
        var allowAllPolicy = new AuthorizationPolicyBuilder()
            .RequireAssertion(_ => true)
            .Build();
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(null)
            .SetDefaultPolicy(allowAllPolicy)
            .AddPolicy(nameof(Role.Viewer), allowAllPolicy)
            .AddPolicy(nameof(Role.Manager), allowAllPolicy)
            .AddPolicy(nameof(Role.Admin), allowAllPolicy);

        builder.Services.AddSingleton(_scheduleTriggerRepository.Object);
        builder.Services.AddSingleton(_jobManager.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, ScheduleTriggersEndpointGroup>();

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
    public async Task GetAllScheduleTriggers_ReturnsEmptyArray_WhenNoTriggers()
    {
        // Arrange
        _scheduleTriggerRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/schedule-triggers").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<ScheduleTrigger[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllScheduleTriggers_ReturnsScheduleTriggers()
    {
        // Arrange
        var trigger = new ScheduleTrigger
        {
            Name = "Daily Scan",
            Schedule = "0 0 * * *",
            JobType = "scan",
            Scope = JobScope.System,
        };

        _scheduleTriggerRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([trigger]);

        // Act
        var response = await _client.GetAsync("/api/schedule-triggers").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<ScheduleTrigger[]>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(ScheduleTrigger.Name)).EqualTo("Daily Scan"));
            Assert.That(content, Has.One.With.Property(nameof(ScheduleTrigger.Schedule)).EqualTo("0 0 * * *"));
        }
    }

    [Test]
    public async Task GetScheduleTriggerById_ReturnsScheduleTrigger()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        var trigger = new ScheduleTrigger
        {
            Id = triggerId,
            Name = "Hourly Backup",
            Schedule = "0 * * * *",
            JobType = "backup",
            Scope = JobScope.System
        };

        _scheduleTriggerRepository
            .Setup(m => m.GetByIdAsync(triggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trigger);

        // Act
        var response = await _client.GetAsync($"/api/schedule-triggers/{triggerId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<ScheduleTrigger>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(triggerId));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("Hourly Backup"));
            Assert.That(content, Has.Property(nameof(content.Schedule)).EqualTo("0 * * * *"));
        }
    }

    [Test]
    public async Task GetScheduleTriggerById_ReturnsNotFound_WhenTriggerDoesNotExist()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        _scheduleTriggerRepository
            .Setup(m => m.GetByIdAsync(triggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(ScheduleTrigger?));

        // Act
        var response = await _client.GetAsync($"/api/schedule-triggers/{triggerId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateScheduleTrigger_CreatesAndReturnsScheduleTrigger()
    {
        // Arrange
        var request = new ScheduleTriggerCreate
        {
            ExtensionId = null,
            JobType = "scan",
            Scope = JobScope.System,
            Name = "New Trigger",
            Schedule = "0 0 * * *",
            ExecutionMode = JobExecutionMode.Queue,
            Enabled = true,
            Parameters = new Dictionary<string, string> { ["libraryId"] = "123" }
        };

        var createdTrigger = new ScheduleTrigger
        {
            Name = "New Trigger",
            Schedule = "0 0 * * *",
            JobType = "scan",
            Scope = JobScope.System,
        };
        _jobManager
            .Setup(m => m
                .CreateScheduleTriggerAsync(It.IsAny<ScheduleTriggerCreate>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdTrigger);

        // Act
        var response = await _client.PostAsJsonAsync("/api/schedule-triggers", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<ScheduleTrigger>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("New Trigger"));
            Assert.That(content, Has.Property(nameof(content.Schedule)).EqualTo("0 0 * * *"));
        }
    }

    [Test]
    public async Task PatchScheduleTrigger_PatchesNameField()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        var request = new ScheduleTriggerPatch(triggerId, Name: new ScalarPatch<string>("Patched"));

        _jobManager
            .Setup(m => m.UpdateScheduleTriggerAsync(
                It.Is<ScheduleTriggerPatch>(p => p.Id == triggerId && p.Name!.Value == "Patched"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/schedule-triggers/{triggerId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchScheduleTrigger_PatchesEnabledField()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        var request = new ScheduleTriggerPatch(triggerId, Enabled: new ScalarPatch<bool>(false));

        _jobManager
            .Setup(m => m.UpdateScheduleTriggerAsync(
                It.Is<ScheduleTriggerPatch>(p => p.Id == triggerId && p.Enabled!.Value == false),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/schedule-triggers/{triggerId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchScheduleTrigger_PatchesScheduleField()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        var request = new ScheduleTriggerPatch(triggerId, Schedule: new ScalarPatch<string>("0 12 * * *"));

        _jobManager
            .Setup(m => m.UpdateScheduleTriggerAsync(
                It.Is<ScheduleTriggerPatch>(p => p.Id == triggerId && p.Schedule!.Value == "0 12 * * *"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/schedule-triggers/{triggerId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchScheduleTrigger_ReturnsNotFound_WhenTriggerDoesNotExist()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        var request = new ScheduleTriggerPatch(triggerId, Name: new ScalarPatch<string>("Patched"));

        _jobManager
            .Setup(m => m
                .UpdateScheduleTriggerAsync(It.IsAny<ScheduleTriggerPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/schedule-triggers/{triggerId}", request)
            .ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteScheduleTrigger_DeletesScheduleTrigger()
    {
        // Arrange
        var triggerId = Guid.NewGuid();

        _scheduleTriggerRepository
            .Setup(m => m.DeleteAsync(triggerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/schedule-triggers/{triggerId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteScheduleTrigger_ReturnsNotFound_WhenTriggerDoesNotExist()
    {
        // Arrange
        var triggerId = Guid.NewGuid();

        _scheduleTriggerRepository
            .Setup(m => m.DeleteAsync(triggerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/schedule-triggers/{triggerId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
