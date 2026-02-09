using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.System;
using Magpie.Api.EndpointGroups;
using Magpie.Models.Api;
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
public class SystemEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IMagpieSystem> _magpieSystem = null!;

    [SetUp]
    public void SetUp()
    {
        _magpieSystem = new Mock<IMagpieSystem>();
        _magpieSystem.Setup(m => m.SystemRoot).Returns("/opt/magpie");
        _magpieSystem.Setup(m => m.NodeId).Returns("test-node-001");

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
            .AddPolicy(nameof(Role.Viewer), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton(_magpieSystem.Object);
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

        // Assert
        var content = await response.Content.ReadFromJsonAsync<HealthResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(content.Status)).EqualTo("Healthy"));
        }
    }

    [Test]
    public async Task GetInfo_ReturnsSystemInfo()
    {
        // Act
        var response = await _client.GetAsync("/api/system/info").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<SystemInfo>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content,
                Has.Property(nameof(SystemInfo.Version)).Not.Null
                    .And.Property(nameof(SystemInfo.Version)).Not.Empty);
            Assert.That(content,
                Has.Property(nameof(SystemInfo.Runtime)).Not.Null
                    .And.Property(nameof(SystemInfo.Runtime)).Not.Empty);
            Assert.That(content,
                Has.Property(nameof(SystemInfo.OperatingSystem)).Not.Null
                    .And.Property(nameof(SystemInfo.OperatingSystem)).Not.Empty);
            Assert.That(content,
                Has.Property(nameof(SystemInfo.Architecture)).Not.Null
                    .And.Property(nameof(SystemInfo.Architecture)).Not.Empty);
            Assert.That(content, Has.Property(nameof(SystemInfo.SystemRoot)).EqualTo("/opt/magpie"));
            Assert.That(content, Has.Property(nameof(SystemInfo.NodeId)).EqualTo("test-node-001"));
        }
    }
}
