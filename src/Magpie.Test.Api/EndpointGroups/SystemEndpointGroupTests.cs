using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Api;
using Magpie.Abstractions.Database;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Abstractions.System;
using Magpie.Api.EndpointGroups;
using Magpie.Api.Logging;
using Magpie.Data;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

using static Magpie.Abstractions.Security.IPasswordHasher;

namespace Magpie.Test.Api.EndpointGroups;

[TestFixture]
public class SystemEndpointGroupTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Mock<IMagpieSystem> _magpieSystem = null!;
    private Mock<IRootFolderRepository> _rootFolderRepository = null!;
    private Mock<IScheduleTriggerRepository> _scheduleTriggerRepository = null!;
    private Mock<IUserRepository> _userRepository = null!;
    private Mock<IPasswordHasher> _passwordHasher = null!;
    private Mock<IDatabaseManagementProvider> _databaseManagementProvider = null!;
    private MagpieDbContext _dbContext = null!;
    private LogBuffer _logBuffer = null!;

    [SetUp]
    public void SetUp()
    {
        _magpieSystem = new Mock<IMagpieSystem>();
        _magpieSystem.Setup(m => m.SystemRoot).Returns("/opt/magpie");
        _magpieSystem.Setup(m => m.NodeId).Returns("test-node-001");
        _magpieSystem.Setup(m => m.StartedAt).Returns(new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc));

        _rootFolderRepository = new Mock<IRootFolderRepository>();
        _rootFolderRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RootFolder>());

        _scheduleTriggerRepository = new Mock<IScheduleTriggerRepository>();
        _scheduleTriggerRepository
            .Setup(m => m.ListDueTriggersAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScheduleTrigger>());

        _userRepository = new Mock<IUserRepository>();
        _userRepository
            .Setup(m => m.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter?>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [],
                PageNumber = null,
                PageSize = null,
                TotalItems = 0
            });

        _passwordHasher = new Mock<IPasswordHasher>();
        _passwordHasher
            .Setup(m => m.HashPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashedPassword("test-hash", "test-salt"));

        _databaseManagementProvider = new Mock<IDatabaseManagementProvider>();
        _databaseManagementProvider.Setup(m => m.Provider).Returns("SQLite");

        _logBuffer = new LogBuffer();

        var dbOptions = new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new MagpieDbContext(dbOptions);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

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
        builder.Services.AddSingleton(_rootFolderRepository.Object);
        builder.Services.AddSingleton(_scheduleTriggerRepository.Object);
        builder.Services.AddSingleton(_userRepository.Object);
        builder.Services.AddSingleton(_passwordHasher.Object);
        builder.Services.AddSingleton(_dbContext);
        builder.Services.AddSingleton(_databaseManagementProvider.Object);
        builder.Services.AddSingleton(_logBuffer);
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
        _dbContext?.Dispose();
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
            Assert.That(content, Has.Property(nameof(SystemInfo.StartedAt)).EqualTo(new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc)));
        }
    }

    [Test]
    public async Task GetDiskSpace_ReturnsEmptyArray_WhenNoRootFolders()
    {
        // Act
        var response = await _client.GetAsync("/api/system/disk-space").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<DiskSpaceInfo[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetDiskSpace_ReturnsDiskSpaceInfo_ForRootFolders()
    {
        // Arrange
        var rootFolders = new List<RootFolder>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Movies",
                Path = Environment.CurrentDirectory,
                ContentTypeId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow
            }
        };
        _rootFolderRepository
            .Setup(m => m.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootFolders);

        // Act
        var response = await _client.GetAsync("/api/system/disk-space").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<DiskSpaceInfo[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content![0], Has.Property(nameof(DiskSpaceInfo.Name)).EqualTo("Movies"));
            Assert.That(content[0], Has.Property(nameof(DiskSpaceInfo.Path)).EqualTo(Environment.CurrentDirectory));
            Assert.That(content[0], Has.Property(nameof(DiskSpaceInfo.TotalBytes)).GreaterThanOrEqualTo(0));
            Assert.That(content[0], Has.Property(nameof(DiskSpaceInfo.FreeBytes)).GreaterThanOrEqualTo(0));
            Assert.That(content[0], Has.Property(nameof(DiskSpaceInfo.UsedBytes)).GreaterThanOrEqualTo(0));
        }
    }

    [Test]
    public async Task GetDatabaseHealth_ReturnsHealthy_WhenDatabaseIsAccessible()
    {
        // Act
        var response = await _client.GetAsync("/api/system/health/database").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ServiceHealthResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(ServiceHealthResponse.Name)).EqualTo("SQLite"));
            Assert.That(content, Has.Property(nameof(ServiceHealthResponse.Status)).EqualTo("healthy"));
            Assert.That(content!.Message, Is.Not.Null.And.Not.Empty);
            Assert.That(content, Has.Property(nameof(ServiceHealthResponse.ResponseTimeMs)).GreaterThanOrEqualTo(0));
        }
    }

    [Test]
    public async Task GetSchedulerHealth_ReturnsHealthy_WhenSchedulerIsAccessible()
    {
        // Act
        var response = await _client.GetAsync("/api/system/health/scheduler").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ServiceHealthResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(ServiceHealthResponse.Name)).EqualTo("Scheduler"));
            Assert.That(content, Has.Property(nameof(ServiceHealthResponse.Status)).EqualTo("healthy"));
            Assert.That(content!.Message, Is.Not.Null.And.Not.Empty);
            Assert.That(content, Has.Property(nameof(ServiceHealthResponse.ResponseTimeMs)).GreaterThanOrEqualTo(0));
        }
    }

    [Test]
    public async Task GetSchedulerHealth_ReturnsUnhealthy_WhenSchedulerThrows()
    {
        // Arrange
        _scheduleTriggerRepository
            .Setup(m => m.ListDueTriggersAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Scheduler unavailable"));

        // Act
        var response = await _client.GetAsync("/api/system/health/scheduler").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ServiceHealthResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(ServiceHealthResponse.Name)).EqualTo("Scheduler"));
            Assert.That(content, Has.Property(nameof(ServiceHealthResponse.Status)).EqualTo("unhealthy"));
            Assert.That(content, Has.Property(nameof(ServiceHealthResponse.Message)).EqualTo("Scheduler unavailable"));
        }
    }

    [Test]
    public async Task GetSchedulerHealth_ReturnsHealthy_WithDueTriggerCount()
    {
        // Arrange
        _scheduleTriggerRepository
            .Setup(m => m.ListDueTriggersAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScheduleTrigger>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    JobType = "TestJob1",
                    Scope = JobScope.System,
                    Name = "Test Trigger 1",
                    Schedule = "0 * * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = DateTime.UtcNow,
                    NextRunAt = DateTime.UtcNow
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    JobType = "TestJob2",
                    Scope = JobScope.System,
                    Name = "Test Trigger 2",
                    Schedule = "0 0 * * *",
                    ExecutionMode = JobExecutionMode.Queue,
                    Enabled = true,
                    CreatedAt = DateTime.UtcNow,
                    NextRunAt = DateTime.UtcNow
                }
            });

        // Act
        var response = await _client.GetAsync("/api/system/health/scheduler").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<ServiceHealthResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(ServiceHealthResponse.Status)).EqualTo("healthy"));
            Assert.That(content, Has.Property(nameof(ServiceHealthResponse.Message)).Contains("2 due trigger(s)"));
        }
    }

    [Test]
    public async Task GetLogs_ReturnsEmptyArray_WhenBufferIsEmpty()
    {
        // Act
        var response = await _client.GetAsync("/api/system/logs").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<LogEntry[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Is.Empty);
        }
    }

    [Test]
    public async Task GetLogs_ReturnsEntries_WhenBufferHasEntries()
    {
        // Arrange
        var timestamp = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        _logBuffer.Add(new LogEntry
        {
            Id = "test-id-1",
            Timestamp = timestamp,
            Level = "info",
            Source = "TestSource",
            Message = "Hello from test",
            Exception = null
        });

        // Act
        var response = await _client.GetAsync("/api/system/logs").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<LogEntry[]>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Length.EqualTo(1));
            Assert.That(content![0], Has.Property(nameof(LogEntry.Id)).EqualTo("test-id-1"));
            Assert.That(content[0], Has.Property(nameof(LogEntry.Level)).EqualTo("info"));
            Assert.That(content[0], Has.Property(nameof(LogEntry.Source)).EqualTo("TestSource"));
            Assert.That(content[0], Has.Property(nameof(LogEntry.Message)).EqualTo("Hello from test"));
        }
    }

    [Test]
    public async Task GetSetupStatus_ReturnsSetupRequired_WhenNoAdminExists()
    {
        // Arrange -- default mock returns empty list (0 admins)

        // Act
        var response = await _client.GetAsync("/api/system/setup/status").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<SetupStatusResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(SetupStatusResponse.IsRequired)).EqualTo(true));
            Assert.That(content, Has.Property(nameof(SetupStatusResponse.IsComplete)).EqualTo(false));
        }
    }

    [Test]
    public async Task GetSetupStatus_ReturnsSetupComplete_WhenAdminExists()
    {
        // Arrange
        _userRepository
            .Setup(m => m.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter?>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [new() { Username = "admin", Role = Role.Admin, ProviderType = "local" }],
                PageNumber = null,
                PageSize = null,
                TotalItems = 1
            });

        // Act
        var response = await _client.GetAsync("/api/system/setup/status").ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<SetupStatusResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.OK));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(SetupStatusResponse.IsRequired)).EqualTo(false));
            Assert.That(content, Has.Property(nameof(SetupStatusResponse.IsComplete)).EqualTo(true));
        }
    }

    [Test]
    public async Task CreateSetupAdmin_ReturnsCreated_WhenSetupRequired()
    {
        // Arrange
        var adminId = Guid.NewGuid();
        var createdUser = new User
        {
            Id = adminId,
            Username = "admin",
            Role = Role.Admin,
            ProviderType = "local",
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };

        _userRepository
            .Setup(m => m.CreateAsync(It.IsAny<User>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdUser);

        var request = new { username = "admin", password = "securepassword123" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/system/setup/admin", request).ConfigureAwait(false);

        // Assert
        var content = await response.Content.ReadFromJsonAsync<UserResponse>().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Created));
            Assert.That(content, Is.Not.Null);
            Assert.That(content, Has.Property(nameof(UserResponse.Id)).EqualTo(adminId));
            Assert.That(content, Has.Property(nameof(UserResponse.Username)).EqualTo("admin"));
            Assert.That(content, Has.Property(nameof(UserResponse.Role)).EqualTo(Role.Admin));
        }
    }

    [Test]
    public async Task CreateSetupAdmin_ReturnsConflict_WhenAdminAlreadyExists()
    {
        // Arrange
        _userRepository
            .Setup(m => m.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter?>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [new() { Username = "admin", Role = Role.Admin, ProviderType = "local" }],
                PageNumber = null,
                PageSize = null,
                TotalItems = 1
            });

        var request = new { username = "admin2", password = "securepassword123" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/system/setup/admin", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task CreateSetupAdmin_ReturnsBadRequest_WhenUsernameTooShort()
    {
        // Arrange
        var request = new { username = "ab", password = "securepassword123" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/system/setup/admin", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateSetupAdmin_ReturnsBadRequest_WhenPasswordTooShort()
    {
        // Arrange
        var request = new { username = "admin", password = "short" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/system/setup/admin", request).ConfigureAwait(false);

        // Assert
        Assert.That(response, Has.Property(nameof(response.StatusCode)).EqualTo(HttpStatusCode.BadRequest));
    }
}
