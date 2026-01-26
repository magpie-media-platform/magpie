using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Abstractions.Security;
using Magpie.Auth;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public class LocalAuthenticatorTests
{
    private Mock<IUserRepository> _userRepository = null!;
    private Mock<IPasswordHasher> _passwordHasher = null!;
    private LocalAuthenticator _subject = null!;
    private AuthProvider _authProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _userRepository = new Mock<IUserRepository>();
        _passwordHasher = new Mock<IPasswordHasher>();
        _subject = new LocalAuthenticator(_userRepository.Object, _passwordHasher.Object);
        _authProvider = CreateTestAuthProvider();
    }

    [Test]
    public void ProviderType_ReturnsLocal()
    {
        Assert.That(_subject.ProviderType, Is.EqualTo("local"));
    }

    [Test]
    public void SupportsJitProvisioning_ReturnsFalse()
    {
        Assert.That(_subject.SupportsJitProvisioning, Is.False);
    }

    [Test]
    public async Task AuthenticateAsync_UserNotFound_ReturnsFailure()
    {
        // Arrange
        _userRepository.Setup(r => r.GetUserCredentialsAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserCredentials?>(null));

        // Act
        var result = await _subject.AuthenticateAsync("testuser", "password", _authProvider).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<User>>()
            .With.Property(nameof(FailureResult<>.Reason)).EqualTo("invalid credentials"));
    }

    [Test]
    public async Task AuthenticateAsync_UserLocked_ReturnsLockedOut()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var lockedUntil = DateTimeOffset.UtcNow.AddHours(1);
        var credentials = new UserCredentials(userId, "hash", "salt", true, lockedUntil);

        _userRepository.Setup(r => r.GetUserCredentialsAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserCredentials?>(credentials));

        // Act
        var result = await _subject.AuthenticateAsync("testuser", "password", _authProvider).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<User>>()
            .With.Property(nameof(FailureResult<>.Reason)).Contains("locked"));
    }

    [Test]
    public async Task AuthenticateAsync_UserDisabled_ReturnsFailure()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var credentials = new UserCredentials(userId, "hash", "salt", false, null);

        _userRepository.Setup(r => r.GetUserCredentialsAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserCredentials?>(credentials));

        // Act
        var result = await _subject.AuthenticateAsync("testuser", "password", _authProvider).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<User>>()
            .With.Property(nameof(FailureResult<>.Reason)).EqualTo("invalid credentials"));
    }

    [Test]
    public async Task AuthenticateAsync_InvalidPassword_ReturnsFailure()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var credentials = new UserCredentials(userId, "storedhash", "storedsalt", true, null);

        _userRepository.Setup(r => r.GetUserCredentialsAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserCredentials?>(credentials));

        _passwordHasher.Setup(h => h.VerifyPasswordAsync(
            "wrongpassword",
            "storedhash",
            "storedsalt",
            It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(false));

        _userRepository.Setup(r => r.RecordFailedLoginAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _subject.AuthenticateAsync("testuser", "wrongpassword", _authProvider).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<User>>());
    }

    [Test]
    public async Task AuthenticateAsync_InvalidPassword_RecordsFailedLogin()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var credentials = new UserCredentials(userId, "storedhash", "storedsalt", true, null);

        _userRepository.Setup(r => r.GetUserCredentialsAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserCredentials?>(credentials));

        _passwordHasher.Setup(h => h.VerifyPasswordAsync(
            "wrongpassword",
            "storedhash",
            "storedsalt",
            It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(false));

        _userRepository.Setup(r => r.RecordFailedLoginAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // Act
        await _subject.AuthenticateAsync("testuser", "wrongpassword", _authProvider).ConfigureAwait(false);

        // Assert
        _userRepository.Verify(r => r.RecordFailedLoginAsync(userId, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task AuthenticateAsync_ValidPassword_ReturnsSuccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var credentials = new UserCredentials(userId, "storedhash", "storedsalt", true, null);
        var user = CreateTestUser(userId);

        _userRepository.Setup(r => r.GetUserCredentialsAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserCredentials?>(credentials));

        _passwordHasher.Setup(h => h.VerifyPasswordAsync(
            "correctpassword",
            "storedhash",
            "storedsalt",
            It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(true));

        _userRepository.Setup(r => r.ResetFailedLoginAttemptsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _userRepository.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));

        // Act
        var result = await _subject.AuthenticateAsync("testuser", "correctpassword", _authProvider)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User>>()
            .With.Property(nameof(SuccessResult<>.Value)).EqualTo(user));
    }

    [Test]
    public async Task AuthenticateAsync_ValidPassword_ResetsFailedLoginAttempts()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var credentials = new UserCredentials(userId, "storedhash", "storedsalt", true, null);
        var user = CreateTestUser(userId);

        _userRepository.Setup(r => r.GetUserCredentialsAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserCredentials?>(credentials));

        _passwordHasher.Setup(h => h.VerifyPasswordAsync(
            "correctpassword",
            "storedhash",
            "storedsalt",
            It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success(true));

        _userRepository.Setup(r => r.ResetFailedLoginAttemptsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _userRepository.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(user));

        // Act
        await _subject.AuthenticateAsync("testuser", "correctpassword", _authProvider).ConfigureAwait(false);

        // Assert
        _userRepository.Verify(r => r.ResetFailedLoginAttemptsAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static User CreateTestUser(Guid? id = null, bool enabled = true)
    {
        return new(
            Id: id ?? Guid.NewGuid(),
            Username: "testuser",
            Email: "test@example.com",
            AuthProviderId: null,
            ExternalId: null,
            Enabled: enabled,
            Role: Role.Viewer,
            ProviderData: ImmutableDictionary<string, string?>.Empty,
            LastLoginAt: null,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);
    }

    private static AuthProvider CreateTestAuthProvider()
    {
        return new(
            Id: Guid.NewGuid(),
            Name: "Local",
            Provider: "local",
            Enabled: true,
            IsDefault: true,
            Settings: new Dictionary<string, string?>(),
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUserId: null,
            ModifiedAt: null,
            ModifiedByUserId: null);
    }
}
