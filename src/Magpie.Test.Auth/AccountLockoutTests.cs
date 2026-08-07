using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Auth;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Repositories;
using Magpie.Sdk.Events;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public class AccountLockoutTests
{
    private Mock<IUserService> _userService = null!;
    private Mock<IUserRepository> _userRepository = null!;
    private Mock<IEventBus> _eventBus = null!;
    private LocalCredentialAuthenticationProvider _subject = null!;
    private AccountLockoutOptions _options = null!;
    private ILoggerFactory _loggerFactory = null!;

    [SetUp]
    public void SetUp()
    {
        _userService = new Mock<IUserService>();
        _userRepository = new Mock<IUserRepository>();
        _eventBus = new Mock<IEventBus>();
        _loggerFactory = NullLoggerFactory.Instance;
        _options = new AccountLockoutOptions
        {
            Enabled = true,
            MaxFailedAttempts = 3,
            LockoutDuration = TimeSpan.FromMinutes(5)
        };

        _subject = new LocalCredentialAuthenticationProvider(
            _userService.Object,
            _userRepository.Object,
            _options,
            _eventBus.Object,
            TimeProvider.System,
            _loggerFactory.CreateLogger<LocalCredentialAuthenticationProvider>());
    }

    [TearDown]
    public void TearDown()
    {
        _loggerFactory?.Dispose();
    }

    [Test]
    public async Task VerifyCredentials_DeniesLockedAccount_BeforeCheckingPassword()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "alice",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            LockedUntil = DateTime.UtcNow.AddMinutes(2),
            CreatedAt = DateTime.UtcNow
        };
        _userService.Setup(s => s.GetAuthenticationUserAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _subject.VerifyCredentialsAsync("alice", "any-password").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
        _userService.Verify(
            s => s.VerifyPasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Locked-out account must not even reach the password verifier.");
    }

    [Test]
    public async Task VerifyCredentials_ResetsLockoutBeforePasswordCheck_WhenLockExpired()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "alice",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            FailedLoginAttempts = 3,
            LockedUntil = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow
        };
        _userService.Setup(s => s.GetAuthenticationUserAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userService
            .Setup(s => s.VerifyPasswordAsync(user, "good-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject.VerifyCredentialsAsync("alice", "good-password").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.SameAs(user));
            Assert.That(user.FailedLoginAttempts, Is.Zero);
            Assert.That(user.LockedUntil, Is.Null);
            _userRepository.Verify(
                r => r.ResetLockoutAsync(user.Id, null, It.IsAny<CancellationToken>()),
                Times.Once,
                "Expired lockout must be reset before the password check.");
            _userRepository.Verify(
                r => r.ClearFailedLoginsAsync(user.Id, It.IsAny<CancellationToken>()),
                Times.Once,
                "Successful login after expiry must leave the failure counter reset.");
            _eventBus.Verify(
                b => b.PublishAsync(
                    It.Is<MagpieEvent>(e =>
                        e.Name == MagpieEvents.Names.UserUnlocked &&
                        e.Context[MagpieEvents.Keys.UserUnlockReason] == "expiry"),
                    It.IsAny<CancellationToken>()),
                Times.Once,
                "An expired lockout being reset must raise user.unlocked with reason 'expiry'.");
        }
    }

    [Test]
    public async Task VerifyCredentials_DoesNotRelock_WhenFirstFailureFollowsExpiredLockout()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "alice",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            FailedLoginAttempts = 3,
            LockedUntil = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow
        };
        _userService.Setup(s => s.GetAuthenticationUserAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userService
            .Setup(s => s.VerifyPasswordAsync(user, "bad-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _userRepository
            .Setup(r => r.RecordFailedLoginAsync(user.Id, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FailedLoginResult(1, null));

        // Act
        var result = await _subject.VerifyCredentialsAsync("alice", "bad-password").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            Assert.That(user.FailedLoginAttempts, Is.Zero);
            Assert.That(user.LockedUntil, Is.Null);
            _userRepository.Verify(
                r => r.ResetLockoutAsync(user.Id, null, It.IsAny<CancellationToken>()),
                Times.Once);
            _userRepository.Verify(
                r => r.RecordFailedLoginAsync(user.Id, null, It.IsAny<CancellationToken>()),
                Times.Once,
                "The first failure after expiry must not lock the account.");
        }
    }

    [Test]
    public async Task VerifyCredentials_RecordsFailure_WhenPasswordWrong()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "alice",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            FailedLoginAttempts = 1,
            CreatedAt = DateTime.UtcNow
        };
        _userService.Setup(s => s.GetAuthenticationUserAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userService
            .Setup(s => s.VerifyPasswordAsync(user, "bad-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _userRepository
            .Setup(r => r.RecordFailedLoginAsync(user.Id, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FailedLoginResult(2, null));

        // Act
        var result = await _subject.VerifyCredentialsAsync("alice", "bad-password").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
        _userRepository.Verify(
            r => r.RecordFailedLoginAsync(user.Id, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task VerifyCredentials_PassesLockUntilToRepo_WhenAtThreshold()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "alice",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            FailedLoginAttempts = 2,
            CreatedAt = DateTime.UtcNow
        };
        _userService.Setup(s => s.GetAuthenticationUserAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userService
            .Setup(s => s.VerifyPasswordAsync(user, "bad-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        DateTime? capturedLockUntil = null;
        _userRepository
            .Setup(r => r.RecordFailedLoginAsync(user.Id, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime?, CancellationToken>((_, lockUntil, _) => capturedLockUntil = lockUntil)
            .ReturnsAsync(new FailedLoginResult(3, DateTime.UtcNow.Add(_options.LockoutDuration)));

        // Act
        await _subject.VerifyCredentialsAsync("alice", "bad-password").ConfigureAwait(false);

        // Assert
        Assert.That(capturedLockUntil, Is.Not.Null);
        Assert.That(
            capturedLockUntil!.Value,
            Is.EqualTo(DateTime.UtcNow.Add(_options.LockoutDuration))
                .Within(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public async Task VerifyCredentials_ClearsFailureCounter_OnSuccess()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "alice",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            FailedLoginAttempts = 2,
            CreatedAt = DateTime.UtcNow
        };
        _userService.Setup(s => s.GetAuthenticationUserAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userService
            .Setup(s => s.VerifyPasswordAsync(user, "good-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject.VerifyCredentialsAsync("alice", "good-password").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        _userRepository.Verify(
            r => r.ClearFailedLoginsAsync(user.Id, It.IsAny<CancellationToken>()),
            Times.Once,
            "Successful login must reset failure counter so accumulated attempts don't persist.");
    }

    [Test]
    public async Task VerifyCredentials_SkipsLockoutLogic_WhenLockoutDisabled()
    {
        // Arrange
        _options.Enabled = false;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "alice",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            FailedLoginAttempts = 99,
            LockedUntil = DateTime.UtcNow.AddYears(1),
            CreatedAt = DateTime.UtcNow
        };
        _userService.Setup(s => s.GetAuthenticationUserAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userService
            .Setup(s => s.VerifyPasswordAsync(user, "good-password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject.VerifyCredentialsAsync("alice", "good-password").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
    }
}
