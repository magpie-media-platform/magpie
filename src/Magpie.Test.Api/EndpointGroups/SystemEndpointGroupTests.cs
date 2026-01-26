using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Api.EndpointGroups;
using Magpie.Models.System;
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
public class SystemEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
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

        builder.Services.AddSingleton<IApiEndpointGroup, SystemEndpointGroup>();

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
    public async Task GetHealth_ReturnsHealthyStatus()
    {
        // Act
        var response = await _client.GetAsync("/api/system/health").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<HealthStatus>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(HealthStatus.Status)).EqualTo("healthy"));
        }
    }

    [Test]
    public async Task GetInfo_ReturnsSystemInfo()
    {
        // Act
        var response = await _client.GetAsync("/api/system/info").ConfigureAwait(false);
        var content = await response.Content.ReadFromJsonAsync<SystemInfo>().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content!.Version, Is.Not.Null.And.Not.Empty);
            Assert.That(content, Has.Property(nameof(SystemInfo.Runtime))
                .EqualTo(RuntimeInformation.FrameworkDescription));
            Assert.That(content, Has.Property(nameof(SystemInfo.OperatingSystem))
                .EqualTo(RuntimeInformation.OSDescription));
            Assert.That(content, Has.Property(nameof(SystemInfo.Architecture))
                .EqualTo(RuntimeInformation.ProcessArchitecture.ToString()));
        }
    }
}
