using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
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
public class FileBrowserEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private string _testDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "magpie-file-browser-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_testDirectory);

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
            .AddPolicy(nameof(Role.Admin), policy => policy.RequireAssertion(_ => true));

        builder.Services.AddSingleton<IApiEndpointGroup, FileBrowserEndpointGroup>();

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

        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Test]
    public async Task BrowseDirectory_ReturnsEmptyArray_WhenDirectoryIsEmpty()
    {
        // Arrange
        var request = new FileBrowserRequest { Path = _testDirectory };

        // Act
        var response = await _client.PostAsJsonAsync("/api/file-browser/contents", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<FileSystemEntry[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task BrowseDirectory_ReturnsFolders()
    {
        // Arrange
        var subDir = Path.Combine(_testDirectory, "SubFolder");
        Directory.CreateDirectory(subDir);

        var request = new FileBrowserRequest { Path = _testDirectory };

        // Act
        var response = await _client.PostAsJsonAsync("/api/file-browser/contents", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<FileSystemEntry[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(FileSystemEntry.Name)).EqualTo("SubFolder"));
            Assert.That(content, Has.One.With.Property(nameof(FileSystemEntry.Type)).EqualTo("folder"));
        }
    }

    [Test]
    public async Task BrowseDirectory_ReturnsFiles()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test.txt");
        await File.WriteAllTextAsync(filePath, "test content").ConfigureAwait(false);

        var request = new FileBrowserRequest { Path = _testDirectory };

        // Act
        var response = await _client.PostAsJsonAsync("/api/file-browser/contents", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<FileSystemEntry[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content, Has.One.With.Property(nameof(FileSystemEntry.Name)).EqualTo("test.txt"));
            Assert.That(content, Has.One.With.Property(nameof(FileSystemEntry.Type)).EqualTo("file"));
            Assert.That(content, Has.One.With.Property(nameof(FileSystemEntry.Size)).GreaterThan(0));
        }
    }

    [Test]
    public async Task BrowseDirectory_ReturnsFoldersBeforeFiles()
    {
        // Arrange
        var subDir = Path.Combine(_testDirectory, "Folder");
        Directory.CreateDirectory(subDir);
        var filePath = Path.Combine(_testDirectory, "file.txt");
        await File.WriteAllTextAsync(filePath, "content").ConfigureAwait(false);

        var request = new FileBrowserRequest { Path = _testDirectory };

        // Act
        var response = await _client.PostAsJsonAsync("/api/file-browser/contents", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<FileSystemEntry[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(2));
            Assert.That(content![0].Type, Is.EqualTo("folder"));
            Assert.That(content[1].Type, Is.EqualTo("file"));
        }
    }

    [Test]
    public async Task BrowseDirectory_ReturnsBadRequest_WhenDirectoryDoesNotExist()
    {
        // Arrange
        var request = new FileBrowserRequest { Path = Path.Combine(_testDirectory, "nonexistent") };

        // Act
        var response = await _client.PostAsJsonAsync("/api/file-browser/contents", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task BrowseDirectory_ReturnsSystemRoot_WhenPathIsSlash()
    {
        // Arrange
        var request = new FileBrowserRequest { Path = "/" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/file-browser/contents", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<FileSystemEntry[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Not.Empty);
        }
    }

    [Test]
    public async Task BrowseDirectory_SortsEntriesAlphabetically()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_testDirectory, "Zebra"));
        Directory.CreateDirectory(Path.Combine(_testDirectory, "Apple"));
        Directory.CreateDirectory(Path.Combine(_testDirectory, "Mango"));

        var request = new FileBrowserRequest { Path = _testDirectory };

        // Act
        var response = await _client.PostAsJsonAsync("/api/file-browser/contents", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<FileSystemEntry[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Has.Length.EqualTo(3));
            Assert.That(content![0].Name, Is.EqualTo("Apple"));
            Assert.That(content[1].Name, Is.EqualTo("Mango"));
            Assert.That(content[2].Name, Is.EqualTo("Zebra"));
        }
    }
}
