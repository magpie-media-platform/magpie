using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
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
public class SubjectsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<ISubjectRepository> _subjectRepository = null!;

    [SetUp]
    public void SetUp()
    {
        _subjectRepository = new Mock<ISubjectRepository>();

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
            .AddPolicy(nameof(Role.Requester), policy => policy.RequireAssertion(_ => true))
            .AddPolicy(nameof(Role.Manager), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton(_subjectRepository.Object);
        builder.Services.AddSingleton<IApiEndpointGroup, SubjectsEndpointGroup>();

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
    public async Task GetSubjects_ReturnsEmptyArray_WhenNoSubjects()
    {
        // Arrange
        _subjectRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/subjects").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Subject[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetSubjects_ReturnsSubjects()
    {
        // Arrange
        var subject = new Subject { Kind = "person", MetadataContainerId = Guid.NewGuid() };

        _subjectRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([subject]);

        // Act
        var response = await _client.GetAsync("/api/subjects").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Subject[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Subject.Kind)).EqualTo("person"));
        }
    }

    [Test]
    public async Task GetSubjectById_ReturnsSubject()
    {
        // Arrange
        var subjectId = Guid.NewGuid();
        var subject = new Subject { Id = subjectId, Kind = "person", MetadataContainerId = Guid.NewGuid() };

        _subjectRepository
            .Setup(m => m.GetByIdAsync(subjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subject);

        // Act
        var response = await _client.GetAsync($"/api/subjects/{subjectId}").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Subject>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(content.Id)).EqualTo(subjectId));
            Assert.That(content, Has.Property(nameof(content.Kind)).EqualTo("person"));
        }
    }

    [Test]
    public async Task GetSubjectById_ReturnsNotFound_WhenSubjectDoesNotExist()
    {
        // Arrange
        var subjectId = Guid.NewGuid();
        _subjectRepository
            .Setup(m => m.GetByIdAsync(subjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Subject?));

        // Act
        var response = await _client.GetAsync($"/api/subjects/{subjectId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetSubjects_ReturnsFilteredSubjects_WhenKindProvided()
    {
        // Arrange
        var subject1 = new Subject { Kind = "person", MetadataContainerId = Guid.NewGuid() };
        var subject2 = new Subject { Kind = "person", MetadataContainerId = Guid.NewGuid() };

        _subjectRepository
            .Setup(m => m.GetByKindAsync("person", It.IsAny<CancellationToken>()))
            .ReturnsAsync([subject1, subject2]);

        // Act
        var response = await _client.GetAsync("/api/subjects?kind=person").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Subject[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(2));
            Assert.That(content, Has.All.With.Property(nameof(Subject.Kind)).EqualTo("person"));
        }
    }

    [Test]
    public async Task GetSubjects_ReturnsEmptyArray_WhenKindHasNoMatches()
    {
        // Arrange
        _subjectRepository
            .Setup(m => m.GetByKindAsync("studio", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/subjects?kind=studio").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Subject[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetSubjects_ReturnsFilteredSubjects_WhenTagProvided()
    {
        // Arrange
        var subject = new Subject { Kind = "person", MetadataContainerId = Guid.NewGuid() };

        _subjectRepository
            .Setup(m => m.GetByTagAsync("actor", It.IsAny<CancellationToken>()))
            .ReturnsAsync([subject]);

        // Act
        var response = await _client.GetAsync("/api/subjects?tag=actor").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Subject[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Subject.Kind)).EqualTo("person"));
        }
    }

    [Test]
    public async Task GetSubjects_ReturnsEmptyArray_WhenTagHasNoMatches()
    {
        // Arrange
        _subjectRepository
            .Setup(m => m.GetByTagAsync("director", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var response = await _client.GetAsync("/api/subjects?tag=director").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Subject[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task CreateSubject_CreatesAndReturnsSubject()
    {
        // Arrange
        var request = new Subject { Kind = "person", MetadataContainerId = Guid.NewGuid() };

        _subjectRepository
            .Setup(m => m.CreateAsync(It.IsAny<Subject>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Subject s, Guid? _, CancellationToken _) => s);

        // Act
        var response = await _client.PostAsJsonAsync("/api/subjects", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<Subject>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(content.Kind)).EqualTo("person"));
        }
    }

    [Test]
    public async Task PatchSubject_PatchesTags()
    {
        // Arrange
        var subjectId = Guid.NewGuid();
        var request = new TagsPatch { Id = subjectId, Tags = new() { Add = ["new-tag"] } };

        _subjectRepository
            .Setup(m => m.PatchAsync(
                It.Is<TagsPatch>(p => p.Id == subjectId && p.Tags != null),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/subjects/{subjectId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchSubject_ReturnsNotFound_WhenSubjectDoesNotExist()
    {
        // Arrange
        var subjectId = Guid.NewGuid();
        var request = new TagsPatch { Id = subjectId, Tags = new() { Add = ["tag"] } };

        _subjectRepository
            .Setup(m => m.PatchAsync(It.IsAny<TagsPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/subjects/{subjectId}", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteSubject_DeletesSubject()
    {
        // Arrange
        var subjectId = Guid.NewGuid();

        _subjectRepository
            .Setup(m => m.DeleteAsync(subjectId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var response = await _client.DeleteAsync($"/api/subjects/{subjectId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteSubject_ReturnsNotFound_WhenSubjectDoesNotExist()
    {
        // Arrange
        var subjectId = Guid.NewGuid();

        _subjectRepository
            .Setup(m => m.DeleteAsync(subjectId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var response = await _client.DeleteAsync($"/api/subjects/{subjectId}").ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.NotFound));
    }
}
