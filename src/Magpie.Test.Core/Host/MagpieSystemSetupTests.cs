using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Database;
using Magpie.Core;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public class MagpieSystemSetupTests
{
    private Mock<ISettingsService> _settingsService = null!;
    private Mock<IUserService> _userService = null!;
    private Mock<IDatabaseManagementProvider> _databaseManagement = null!;
    private Mock<IFileSystem> _fileSystem = null!;
    private ISetupService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _settingsService = new Mock<ISettingsService>();
        _userService = new Mock<IUserService>();
        _databaseManagement = new Mock<IDatabaseManagementProvider>();
        _databaseManagement.SetupGet(provider => provider.Provider).Returns("sqlite");
        _fileSystem = new Mock<IFileSystem>();

        _settingsService
            .Setup(service => service.GetSystemGroupAsync("setup", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingsGroupDetail?)null);
        _userService
            .Setup(service => service.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User> { Items = [], TotalItems = 0 });

        _subject = new SetupService(
            new MagpieHostEnvironment(
                NodeId: "node-1",
                IsClustered: false,
                IsSupervised: false,
                SystemRoot: "/tmp/magpie",
                ExtensionsRoot: "/tmp/magpie/extensions",
                ConfigRoot: "/tmp/magpie/config",
                DataRoot: "/tmp/magpie/data",
                StartedAt: DateTime.UtcNow),
            _settingsService.Object,
            _userService.Object,
            _databaseManagement.Object,
            _fileSystem.Object,
            NullLogger<SetupService>.Instance);
    }

    [Test]
    public async Task ConfigureDatabaseAsync_Result_WhenConfigurationIsStored()
    {
        // Arrange
        var configurationPath = Path.Combine("/tmp/magpie/config", "appsettings.json");
        var configurationDirectory = Path.GetDirectoryName(configurationPath)!;
        var temporaryPath = configurationPath + ".tmp";
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(It.IsAny<string>())).Returns(false);
        _fileSystem.Setup(fileSystem => fileSystem.CreateFile(It.IsAny<string>())).Returns(new MemoryStream());

        // Act
        var result = await _subject.ConfigureDatabaseAsync("sqlite", null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Provider, Is.EqualTo("sqlite"));
            Assert.That(result.RestartRequired, Is.True);
            _fileSystem.Verify(fileSystem => fileSystem.CreateDirectory(configurationDirectory));
            _fileSystem.Verify(fileSystem => fileSystem.CreateFile(temporaryPath));
            _fileSystem.Verify(fileSystem => fileSystem.Move(
                temporaryPath,
                configurationPath,
                true));
        }
    }

    [TestCase("postgresql")]
    [TestCase("sqlserver")]
    public void ConfigureDatabaseAsync_Throws_WhenConnectionStringIsRequired(string provider)
    {
        // Act / Assert
        Assert.That(
            async () => await _subject.ConfigureDatabaseAsync(provider, null).ConfigureAwait(false),
            Throws.TypeOf<MagpieException>().With.Message.EqualTo("A connection string is required."));
    }

    [Test]
    public void ConfigureDatabaseAsync_Throws_WhenProviderIsUnknown()
    {
        // Act / Assert
        Assert.That(
            async () => await _subject.ConfigureDatabaseAsync("unknown", null).ConfigureAwait(false),
            Throws.TypeOf<MagpieException>().With.Message.EqualTo("The database provider is not supported."));
    }

    [Test]
    public async Task IsSetupCompleteAsync_ReturnsFalse_WhenSetupUnset()
    {
        // Act
        var isComplete = await _subject.IsCompleteAsync().ConfigureAwait(false);

        // Assert
        Assert.That(isComplete, Is.False);
    }

    [Test]
    public async Task IsSetupCompleteAsync_ReturnsTrue_WhenSentinelTrue()
    {
        // Arrange
        var group = new SettingsGroup { Name = "setup" };
        group.Settings.Add(new Setting { Key = "complete", Value = "true" });
        _settingsService
            .Setup(service => service.GetSystemGroupAsync("setup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SettingsGroupDetail { Group = group });
        _userService
            .Setup(service => service.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items =
                [
                    new User
                    {
                        Id = Guid.NewGuid(),
                        Username = "admin",
                        ProviderType = "local",
                        Enabled = true,
                        Role = Role.Admin
                    }
                ],
                TotalItems = 1
            });

        // Act
        var isComplete = await _subject.IsCompleteAsync().ConfigureAwait(false);

        // Assert
        Assert.That(isComplete, Is.True);
    }

    [Test]
    public async Task CreateSetupAdminAsync_CreatesLocalAdmin_WhenClaimRequired()
    {
        // Arrange
        var created = new User
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Username = "admin",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin
        };
        _userService
            .Setup(service => service.GetAuthenticationUserAsync("admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _userService
            .Setup(service => service.CreateAsync(
                It.IsAny<LocalUserCreate>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        // Act
        var result = await _subject
            .CreateAdministratorAsync("admin", "password123")
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Id, Is.EqualTo(created.Id));
            Assert.That(result.Username, Is.EqualTo("admin"));
            _userService.Verify(
                service => service.CreateAsync(
                    It.Is<LocalUserCreate>(create =>
                        create.Username == "admin" &&
                        create.Password == "password123" &&
                        create.Role == Role.Admin),
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public void CreateSetupAdminAsync_Throws_WhenAdminAlreadyExists()
    {
        // Arrange
        _userService
            .Setup(service => service.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items =
                [
                    new User
                    {
                        Id = Guid.NewGuid(),
                        Username = "admin",
                        ProviderType = "local",
                        Enabled = true,
                        Role = Role.Admin
                    }
                ],
                TotalItems = 1
            });

        // Act / Assert
        Assert.That(
            async () => await _subject.CreateAdministratorAsync("admin", "password123").ConfigureAwait(false),
            Throws.TypeOf<MagpieException>()
                .With.Message.EqualTo("The administrator account is already claimed."));
    }

    [Test]
    public async Task CompleteSetupAsync_MarksComplete_WhenAdminExists()
    {
        // Arrange
        _userService
            .Setup(service => service.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items =
                [
                    new User
                    {
                        Id = Guid.NewGuid(),
                        Username = "admin",
                        ProviderType = "local",
                        Enabled = true,
                        Role = Role.Admin
                    }
                ],
                TotalItems = 1
            });
        _settingsService
            .Setup(service => service.PatchSystemGroupAsync(
                "setup",
                It.IsAny<SettingsGroupPatch>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _subject.CompleteAsync().ConfigureAwait(false);

        // Assert
        _settingsService.Verify(
            service => service.PatchSystemGroupAsync(
                "setup",
                It.Is<SettingsGroupPatch>(patch =>
                    patch.Settings.Add != null &&
                    patch.Settings.Add.ContainsKey("complete") &&
                    patch.Settings.Add["complete"] == "true"),
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void CompleteSetupAsync_Throws_WhenAdminMissing()
    {
        // Act / Assert
        Assert.That(
            async () => await _subject.CompleteAsync().ConfigureAwait(false),
            Throws.TypeOf<MagpieException>()
                .With.Message.EqualTo(
                    "An administrator account must be created before setup can be marked complete."));
    }

    [Test]
    public async Task GetAppliedMigrationsAsync_ReturnsAppliedMigrations_WhenProviderSucceeds()
    {
        // Arrange
        _databaseManagement
            .Setup(provider => provider.CanConnectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _databaseManagement
            .Setup(provider => provider.GetAppliedMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["20260509010443_Initial"]);

        // Act
        var canConnect = await _subject.CanConnectAsync().ConfigureAwait(false);
        var migrations = await _subject.GetAppliedMigrationsAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(canConnect, Is.True);
            Assert.That(migrations, Is.EqualTo((IReadOnlyCollection<string>)["20260509010443_Initial"]));
            Assert.That(_subject.DatabaseProvider, Is.EqualTo("sqlite"));
        }
    }

    [Test]
    public async Task CanConnectAsync_ReturnsFalse_WhenProviderThrows()
    {
        // Arrange
        _databaseManagement
            .Setup(provider => provider.CanConnectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("offline"));

        // Act
        var canConnect = await _subject.CanConnectAsync().ConfigureAwait(false);

        // Assert
        Assert.That(canConnect, Is.False);
    }
}
