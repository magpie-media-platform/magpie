using System;
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
public class EventTriggersEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IEventTriggerRepository> _eventTriggerRepository = null!;
    private Mock<IJobManager> _jobManager = null!;

    [SetUp]
    public void SetUp()
    {
        _eventTriggerRepository = new Mock<IEventTriggerRepository>();
        _jobManager = new Mock<IJobManager>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddTestLogger();
        var authServiceMock = new Mock<IAuthenticationService>();
        authServiceMock
            .Setup(m => m.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity("Test")), "Test")));
        builder.Services.AddSingleton(authServiceMock.Object);
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(null)
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true)
                .Build())
            .AddPolicy(nameof(Role.Viewer), policy => policy.RequireAssertion(_ => true))
            .AddPolicy(nameof(Role.Manager), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton(_eventTriggerRepository.Object);
        builder.Services.AddSingleton(_jobManager.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, EventTriggersEndpointGroup>();

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
    public async Task GetEventTriggers_ReturnsEmptyArray_WhenNoTriggers()
    {
        // Arrange
        _eventTriggerRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/event-triggers").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<EventTrigger[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetEventTriggers_ReturnsTriggers()
    {
        // Arrange
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            Name = "Test Trigger",
            JobType = "test-job",
            Scope = JobScope.Content,
            EventFilter = new EventFilter()
        };

        _eventTriggerRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([trigger]);

        // Act
        var response = await _client.GetAsync("/api/event-triggers").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<EventTrigger[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(EventTrigger.Name)).EqualTo("Test Trigger"));
        }
    }

    [Test]
    public async Task GetEventTriggers_ReturnsFilteredTriggers_WhenExtensionIdProvided()
    {
        // Arrange
        var extensionId = Guid.NewGuid();
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            Name = "Extension Trigger",
            JobType = "test-job",
            Scope = JobScope.Content,
            ExtensionId = extensionId,
            EventFilter = new EventFilter()
        };

        _eventTriggerRepository
            .Setup(m => m.ListByExtensionIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([trigger]);

        // Act
        var response = await _client.GetAsync($"/api/event-triggers?extensionId={extensionId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<EventTrigger[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(EventTrigger.ExtensionId)).EqualTo(extensionId));
        }
    }

    [Test]
    public async Task GetEventTriggers_ReturnsEmptyArray_WhenExtensionIdHasNoMatches()
    {
        // Arrange
        var extensionId = Guid.NewGuid();

        _eventTriggerRepository
            .Setup(m => m.ListByExtensionIdAsync(extensionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync($"/api/event-triggers?extensionId={extensionId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<EventTrigger[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetEventTriggers_ReturnsEnabledTriggers_WhenEnabledIsTrue()
    {
        // Arrange
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            Name = "Enabled Trigger",
            JobType = "test-job",
            Scope = JobScope.Content,
            Enabled = true,
            EventFilter = new EventFilter()
        };

        _eventTriggerRepository
            .Setup(m => m.ListEnabledAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([trigger]);

        // Act
        var response = await _client.GetAsync("/api/event-triggers?enabled=true").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<EventTrigger[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(EventTrigger.Enabled)).True);
        }
    }

    [Test]
    public async Task GetEventTriggers_ReturnsAllTriggers_WhenEnabledIsFalse()
    {
        // Arrange
        var trigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            Name = "Test Trigger",
            JobType = "test-job",
            Scope = JobScope.Content,
            EventFilter = new EventFilter()
        };

        _eventTriggerRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([trigger]);

        // Act
        var response = await _client.GetAsync("/api/event-triggers?enabled=false").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<EventTrigger[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task GetEventTriggerById_ReturnsTrigger()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        var trigger = new EventTrigger
        {
            Id = triggerId,
            Name = "Test Trigger",
            JobType = "test-job",
            Scope = JobScope.Content,
            EventFilter = new EventFilter()
        };

        _eventTriggerRepository
            .Setup(m => m.GetByIdAsync(triggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trigger);

        // Act
        var response = await _client.GetAsync($"/api/event-triggers/{triggerId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<EventTrigger>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(triggerId));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("Test Trigger"));
        }
    }

    [Test]
    public async Task GetEventTriggerById_ReturnsNotFound_WhenTriggerDoesNotExist()
    {
        // Arrange
        var triggerId = Guid.NewGuid();

        _eventTriggerRepository
            .Setup(m => m.GetByIdAsync(triggerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(EventTrigger?));

        // Act
        var response = await _client.GetAsync($"/api/event-triggers/{triggerId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task CreateEventTrigger_CreatesAndReturnsTrigger()
    {
        // Arrange
        var request = new EventTriggerCreate
        {
            Name = "New Trigger",
            JobType = "test-job",
            Scope = JobScope.Content,
            EventFilter = new EventFilter()
        };

        var createdTrigger = new EventTrigger
        {
            Id = Guid.NewGuid(),
            Name = "New Trigger",
            JobType = "test-job",
            Scope = JobScope.Content,
            EventFilter = new EventFilter()
        };

        _jobManager
            .Setup(m => m.CreateEventTriggerAsync(It.IsAny<EventTriggerCreate>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdTrigger);

        // Act
        var response = await _client.PostAsJsonAsync("/api/event-triggers", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<EventTrigger>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Name)).EqualTo("New Trigger"));
        }
    }

    [Test]
    public async Task PatchEventTrigger_PatchesTrigger()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        var request = new EventTriggerPatch { Id = triggerId, Name = new() { Value = "Updated Name" } };

        _jobManager
            .Setup(m => m.UpdateEventTriggerAsync(
                It.Is<EventTriggerPatch>(p => p.Id == triggerId),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/event-triggers/{triggerId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchEventTrigger_ReturnsNotFound_WhenTriggerDoesNotExist()
    {
        // Arrange
        var triggerId = Guid.NewGuid();
        var request = new EventTriggerPatch { Id = triggerId, Name = new() { Value = "Updated Name" } };

        _jobManager
            .Setup(m => m.UpdateEventTriggerAsync(It.IsAny<EventTriggerPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/event-triggers/{triggerId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteEventTrigger_DeletesTrigger()
    {
        // Arrange
        var triggerId = Guid.NewGuid();

        _eventTriggerRepository
            .Setup(m => m.DeleteAsync(triggerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/event-triggers/{triggerId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteEventTrigger_ReturnsNotFound_WhenTriggerDoesNotExist()
    {
        // Arrange
        var triggerId = Guid.NewGuid();

        _eventTriggerRepository
            .Setup(m => m.DeleteAsync(triggerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/event-triggers/{triggerId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
