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
using Magpie.Models;
using Magpie.Models.Content;
using Magpie.Models.Metadata;
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
public class SubjectsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<ISubjectRepository> _subjectRepositoryMock = null!;

    [SetUp]
    public void SetUp()
    {
        _subjectRepositoryMock = new Mock<ISubjectRepository>();

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

        builder.Services.AddSingleton(_subjectRepositoryMock.Object);
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
    public async Task GetAllSubjects_ReturnsEmptyArray_WhenNoSubjects()
    {
        _subjectRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Subject>>([]));

        var response = await _client.GetAsync("/api/subjects").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Subject[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllSubjects_ReturnsSubjects()
    {
        var subject = CreateSubject("Person");

        _subjectRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Subject>>([subject]));

        var response = await _client.GetAsync("/api/subjects").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Subject[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Subject.Kind)).EqualTo("Person"));
        }
    }

    [Test]
    public async Task GetSubjectById_ReturnsSubject()
    {
        var subjectId = Guid.NewGuid();
        var subject = CreateSubject("Person", subjectId);

        _subjectRepositoryMock
            .Setup(r => r.GetByIdAsync(subjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Subject?>(subject));

        var response = await _client.GetAsync($"/api/subjects/{subjectId}").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Subject>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Property(nameof(Subject.Id)).EqualTo(subjectId));
            Assert.That(content, Has.Property(nameof(Subject.Kind)).EqualTo("Person"));
        }
    }

    [Test]
    public async Task GetSubjectById_ReturnsNotFound_WhenSubjectDoesNotExist()
    {
        var subjectId = Guid.NewGuid();
        _subjectRepositoryMock
            .Setup(r => r.GetByIdAsync(subjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Subject?>(null));

        var response = await _client.GetAsync($"/api/subjects/{subjectId}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetSubjectsByKind_ReturnsFilteredSubjects()
    {
        var subject1 = CreateSubject("Person");
        var subject2 = CreateSubject("Person");

        _subjectRepositoryMock
            .Setup(r => r.GetByKindAsync("Person", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Subject>>([subject1, subject2]));

        var response = await _client.GetAsync("/api/subjects/by-kind/Person").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Subject[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(2));
            Assert.That(content, Has.All.With.Property(nameof(Subject.Kind)).EqualTo("Person"));
        }
    }

    [Test]
    public async Task GetSubjectsByKind_ReturnsEmptyArray_WhenNoMatchingSubjects()
    {
        _subjectRepositoryMock
            .Setup(r => r.GetByKindAsync("Studio", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Subject>>([]));

        var response = await _client.GetAsync("/api/subjects/by-kind/Studio").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Subject[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetSubjectsByTag_ReturnsFilteredSubjects()
    {
        var subject = CreateSubject("Person", tags: ["actor"]);

        _subjectRepositoryMock
            .Setup(r => r.GetByTagAsync("actor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Subject>>([subject]));

        var response = await _client.GetAsync("/api/subjects/by-tag/actor").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Subject[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(Subject.Kind)).EqualTo("Person"));
        }
    }

    [Test]
    public async Task GetSubjectsByTag_ReturnsEmptyArray_WhenNoMatchingSubjects()
    {
        _subjectRepositoryMock
            .Setup(r => r.GetByTagAsync("director", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Subject>>([]));

        var response = await _client.GetAsync("/api/subjects/by-tag/director").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Subject[]>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task CreateSubject_CreatesAndReturnsSubject()
    {
        var request = CreateSubject("Person");

        _subjectRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Subject>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Subject s, Guid? _, CancellationToken _) => Result.Success(s));

        var response = await _client.PostAsJsonAsync("/api/subjects", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Subject>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(Subject.Kind)).EqualTo("Person"));
            Assert.That(content, Has.Property(nameof(Subject.Tags)).Empty);
            Assert.That(response.Headers.Location?.ToString(), Does.Contain($"/api/subjects/{content!.Id}"));
        }
    }

    [Test]
    public async Task CreateSubject_WithTags_CreatesSubject()
    {
        var request = CreateSubject("Person", tags: ["actor", "director"]);

        _subjectRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<Subject>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Subject s, Guid? _, CancellationToken _) => Result.Success(s));

        var response = await _client.PostAsJsonAsync("/api/subjects", request).ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<Subject>().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(content, Has.Property(nameof(Subject.Kind)).EqualTo("Person"));
            Assert.That(content, Has.Property(nameof(Subject.Tags)).Count.EqualTo(2));
        }
    }

    [Test]
    public async Task PatchSubject_PatchesTags()
    {
        var subjectId = Guid.NewGuid();
        var request = new SubjectPatch { Tags = new SetPatch<string, string>(["new-tag"], null) };

        _subjectRepositoryMock
            .Setup(r => r.PatchAsync(
                It.Is<SubjectPatch>(p => p.Id == subjectId && p.Tags != null),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var response = await _client.PatchAsJsonAsync($"/api/subjects/{subjectId}", request).ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task PatchSubject_ReturnsNotFound_WhenSubjectDoesNotExist()
    {
        var subjectId = Guid.NewGuid();
        var request = new SubjectPatch { Tags = new SetPatch<string, string>(["tag"], null) };

        _subjectRepositoryMock
            .Setup(r => r.PatchAsync(It.IsAny<SubjectPatch>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        var response = await _client.PatchAsJsonAsync($"/api/subjects/{subjectId}", request).ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteSubject_DeletesSubject()
    {
        var subjectId = Guid.NewGuid();

        _subjectRepositoryMock
            .Setup(r => r.DeleteAsync(subjectId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var response = await _client.DeleteAsync($"/api/subjects/{subjectId}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task DeleteSubject_ReturnsNotFound_WhenSubjectDoesNotExist()
    {
        var subjectId = Guid.NewGuid();

        _subjectRepositoryMock
            .Setup(r => r.DeleteAsync(subjectId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        var response = await _client.DeleteAsync($"/api/subjects/{subjectId}").ConfigureAwait(false);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static Subject CreateSubject(
        string kind,
        Guid? id = null,
        IReadOnlyList<string>? tags = null) =>
        new(
            Id: id ?? Guid.NewGuid(),
            Kind: kind,
            DateLastRefreshed: null,
            Metadata: new MetadataContainer(Guid.NewGuid(), DateTimeOffset.UtcNow, []),
            Tags: tags ?? [],
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);
}
