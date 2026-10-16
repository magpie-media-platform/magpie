using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Auth;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Repositories;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public class CredentialTimingEqualizationTests
{
    private Mock<IUserService> _userService = null!;
    private Mock<IUserRepository> _userRepository = null!;
    private LocalCredentialAuthenticationProvider _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _userService = new Mock<IUserService>();
        _userRepository = new Mock<IUserRepository>();
        _userService
            .Setup(s => s.VerifyPasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var options = new AccountLockoutOptions
        {
            Enabled = true,
            MaxFailedAttempts = 3,
            LockoutDuration = TimeSpan.FromMinutes(5)
        };

        _subject = new LocalCredentialAuthenticationProvider(
            _userService.Object,
            _userRepository.Object,
            options,
            TimeProvider.System,
            NullLogger<LocalCredentialAuthenticationProvider>.Instance);
    }

    [Test]
    public async Task VerifyCredentials_InvokesVerifier_WhenUsernameNotFound()
    {
        // Arrange
        _userService.Setup(s => s.GetAuthenticationUserAsync("ghost", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _subject.VerifyCredentialsAsync("ghost", "any-password").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
        _userService.Verify(
            s => s.VerifyPasswordAsync(It.IsAny<User>(), "any-password", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task VerifyCredentials_InvokesVerifier_WhenUserDisabled()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "alice",
            ProviderType = "local",
            Enabled = false,
            Role = Role.Viewer,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            CreatedAt = DateTime.UtcNow
        };
        _userService.Setup(s => s.GetAuthenticationUserAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _subject.VerifyCredentialsAsync("alice", "any-password").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
        _userService.Verify(
            s => s.VerifyPasswordAsync(It.IsAny<User>(), "any-password", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task VerifyCredentials_InvokesVerifier_WhenProviderMismatch()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "alice",
            ProviderType = "ldap",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            CreatedAt = DateTime.UtcNow
        };
        _userService.Setup(s => s.GetAuthenticationUserAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _subject.VerifyCredentialsAsync("alice", "any-password").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
        _userService.Verify(
            s => s.VerifyPasswordAsync(It.IsAny<User>(), "any-password", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task VerifyCredentials_InvokesVerifierExactlyOnce_WhenMissingUserAndWrongPassword()
    {
        // Arrange
        _userService.Setup(s => s.GetAuthenticationUserAsync("ghost", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        var realUser = new User
        {
            Id = Guid.NewGuid(),
            Username = "alice",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Viewer,
            PasswordHash = "hash",
            PasswordSalt = "salt",
            CreatedAt = DateTime.UtcNow
        };
        _userService.Setup(s => s.GetAuthenticationUserAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realUser);
        _userRepository
            .Setup(r => r.RecordFailedLoginAsync(
                realUser.Id,
                It.IsAny<int>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FailedLoginResult(1, null));

        // Act
        var missing = await _subject.VerifyCredentialsAsync("ghost", "any-password").ConfigureAwait(false);
        var wrong = await _subject.VerifyCredentialsAsync("alice", "wrong-password").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing, Is.Null);
            Assert.That(wrong, Is.Null);
        }

        _userService.Verify(
            s => s.VerifyPasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
