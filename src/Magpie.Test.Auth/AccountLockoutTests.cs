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

    [SetUp]
    public void SetUp()
    {
        _userService = new Mock<IUserService>();
        _userRepository = new Mock<IUserRepository>();
        _eventBus = new Mock<IEventBus>();
        var time = new Mock<TimeProvider>();
        time.Setup(value => value.GetUtcNow())
            .Returns(new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));
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
            time.Object,
            NullLogger<LocalCredentialAuthenticationProvider>.Instance);
    }

    [Test]
    public async Task VerifyCredentials_DeniesWithoutCheckingThePassword_WhenTheAccountIsLocked()
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
            LockedUntil = new DateTime(2026, 9, 9, 12, 2, 0, DateTimeKind.Utc),
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
            Times.Never);
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
            LockedUntil = new DateTime(2026, 9, 9, 11, 59, 0, DateTimeKind.Utc),
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
        }

        _userRepository.Verify(r => r.ResetLockoutAsync(user.Id, null, It.IsAny<CancellationToken>()), Times.Once);
        _userRepository.Verify(r => r.ClearFailedLoginsAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _eventBus.Verify(
            b => b.PublishAsync(
                It.Is<MagpieEvent>(e =>
                    e.Name == MagpieEvents.Names.UserUnlocked &&
                    e.Context[MagpieEvents.Keys.UserUnlockReason] == "expiry"),
                It.IsAny<CancellationToken>()),
            Times.Once);
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
            LockedUntil = new DateTime(2026, 9, 9, 11, 59, 0, DateTimeKind.Utc),
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
        }

        _userRepository.Verify(r => r.ResetLockoutAsync(user.Id, null, It.IsAny<CancellationToken>()), Times.Once);
        _userRepository.Verify(r => r.RecordFailedLoginAsync(user.Id, null, It.IsAny<CancellationToken>()), Times.Once);
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
        _userRepository.Verify(r => r.RecordFailedLoginAsync(user.Id, null, It.IsAny<CancellationToken>()), Times.Once);
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
            .ReturnsAsync(new FailedLoginResult(3, new DateTime(2026, 9, 9, 12, 5, 0, DateTimeKind.Utc)));

        // Act
        await _subject.VerifyCredentialsAsync("alice", "bad-password").ConfigureAwait(false);

        // Assert
        Assert.That(capturedLockUntil, Is.EqualTo(new DateTime(2026, 9, 9, 12, 5, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task VerifyCredentials_ClearsFailureCounter_WhenSuccess()
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
        _userRepository.Verify(r => r.ClearFailedLoginsAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
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
