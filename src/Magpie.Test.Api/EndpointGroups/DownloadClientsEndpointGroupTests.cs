using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Api;
using Magpie.Data.Database;
using Magpie.Models.Downloads;
using Magpie.Test.Shared;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

using DownloadClientEntity = Magpie.Data.Database.Entities.DownloadClient;

namespace Magpie.Test.Api.EndpointGroups;

[TestFixture]
public class DownloadClientsEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private MagpieDbContext _context = null!;
    private SqliteConnection _connection = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

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

        builder.Services.AddMagpieApi();
        builder.Services.AddLogging(b => b.AddTestLogger());
        builder.Services.AddSingleton(new Mock<ITokenService>().Object);
        builder.Services.AddSingleton(new Mock<IAuthenticator>().Object);

        builder.Services.AddDbContext<MagpieDbContext>(options =>
            options.UseSqlite(_connection));
        builder.Services.AddMagpieServices();

        var app = builder.Build();
        app.UseMagpieApi();

        app.UseAuthorization();

        _host = app;

        var scope = app.Services.CreateScope();
        _context = scope.ServiceProvider.GetRequiredService<MagpieDbContext>();
        _context.Database.EnsureCreated();

        _host.Start();
        _client = _host.GetTestClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
        _context?.Dispose();
        _host?.Dispose();
        _connection?.Close();
        _connection?.Dispose();
    }

    [Test]
    public async Task GetDownloadClientByName_ReturnsNotFound_WhenDownloadClientDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync("/api/download-clients/by-name/NonExistent").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteDownloadClient_DeletesDownloadClient()
    {
        // Arrange
        var entity = new DownloadClientEntity
        {
            Name = "ToDelete",
            Provider = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.DownloadClients.Add(entity);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var response = await _client.DeleteAsync($"/api/download-clients/{entity.Id}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        _context.ChangeTracker.Clear();
        var deleted = await _context.DownloadClients.FirstOrDefaultAsync(d => d.Id == entity.Id).ConfigureAwait(false);
        Assert.That(deleted, Is.Null);
    }

    [Test]
    public async Task DeleteDownloadClient_ReturnsNotFound_WhenDownloadClientDoesNotExist()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/download-clients/{Guid.NewGuid()}").ConfigureAwait(false);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
