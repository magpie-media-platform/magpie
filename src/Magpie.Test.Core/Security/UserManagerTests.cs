using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Abstractions.Security;
using Magpie.Core.Security;
using Magpie.Models.Result;
using Magpie.Models.Users;

using Moq;

using NUnit.Framework;

using static Magpie.Abstractions.Security.IPasswordHasher;

using static Magpie.Core.Security.IPasswordPolicy;

namespace Magpie.Test.Core.Security;

[TestFixture]
public class UserManagerTests
{
    private Mock<IUserRepository> _userRepository = null!;
    private Mock<IPasswordHasher> _passwordHasher = null!;
    private Mock<IPasswordPolicy> _passwordPolicy = null!;
    private UserManager _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _userRepository = new Mock<IUserRepository>();
        _passwordHasher = new Mock<IPasswordHasher>();
        _passwordPolicy = new Mock<IPasswordPolicy>();
        _subject = new UserManager(_userRepository.Object, _passwordHasher.Object, _passwordPolicy.Object);
    }

    [Test]
    public async Task VerifyCredentialsAsync_ReturnsNull_WhenUserNotFound()
    {
        // Arrange
        _userRepository.Setup(r => r.GetUserCredentialsAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserCredentials?>(null));

        // Act
        var result = await _subject.VerifyCredentialsAsync("unknown", "password", default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task VerifyCredentialsAsync_ReturnsNull_WhenUserIsDisabled()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var credentials = new UserCredentials(userId, "hash", "salt", false, null);

        _userRepository.Setup(r => r.GetUserCredentialsAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserCredentials?>(credentials));

        // Act
        var result = await _subject.VerifyCredentialsAsync("testuser", "password", default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task VerifyCredentialsAsync_ReturnsNull_WhenUserIsLocked()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var credentials = new UserCredentials(userId, "hash", "salt", true, DateTimeOffset.UtcNow.AddHours(1));

        _userRepository.Setup(r => r.GetUserCredentialsAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<UserCredentials?>(credentials));

        // Act
        var result = await _subject.VerifyCredentialsAsync("testuser", "password", default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task VerifyCredentialsAsync_ReturnsNull_AndRecordsFailedLogin_WhenPasswordIsInvalid()
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
        var result = await _subject.VerifyCredentialsAsync("testuser", "wrongpassword", default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User?>>().With.Property(nameof(SuccessResult<>.Value)).Null);
        _userRepository.Verify(
            r => r.RecordFailedLoginAsync(userId, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task VerifyCredentialsAsync_ReturnsUser_AndResetsFailedAttempts_WhenCredentialsAreValid()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var credentials = new UserCredentials(userId, "storedhash", "storedsalt", true, null);
        var user = new User(
            userId,
            "testuser",
            "test@example.com",
            null,
            null,
            true,
            Role.Viewer,
            ImmutableDictionary<string, string?>.Empty,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

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
        var result = await _subject.VerifyCredentialsAsync(
            "testuser",
            "correctpassword",
            default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.Id)).EqualTo(userId)
            .And.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.Username)).EqualTo("testuser"));
        _userRepository.Verify(r => r.ResetFailedLoginAttemptsAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task VerifyCredentialsAsync_PropagatesFailure_WhenGetUserCredentialsFails()
    {
        // Arrange
        _userRepository.Setup(r => r.GetUserCredentialsAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<UserCredentials?>("Database error", "DB_ERROR"));

        // Act
        var result = await _subject.VerifyCredentialsAsync("testuser", "password", default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<User?>>()
            .With.Property(nameof(FailureResult<>.Reason)).EqualTo("Database error")
            .And.Property(nameof(FailureResult<>.ErrorCode)).EqualTo("DB_ERROR"));
    }

    [Test]
    public async Task ResetPasswordAsync_HashesAndUpdatesPassword()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var modifiedByUserId = Guid.NewGuid();

        _passwordPolicy.Setup(p => p.ValidatePasswordAsync("newpassword", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PasswordPolicyResult(true, [])));
        _passwordHasher.Setup(h => h.HashPasswordAsync("newpassword", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new HashedPassword("newhash", "newsalt")));
        _userRepository
            .Setup(r => r.UpdatePasswordAsync(
                userId,
                "newhash",
                "newsalt",
                true,
                true,
                modifiedByUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _subject.ResetPasswordAsync(userId, "newpassword", modifiedByUserId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Unit>>());
        _userRepository.Verify(
            r => r.UpdatePasswordAsync(
                userId,
                "newhash",
                "newsalt",
                true,
                true,
                modifiedByUserId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ResetPasswordAsync_Fails_WhenPasswordPolicyValidationFails()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var modifiedByUserId = Guid.NewGuid();
        _passwordPolicy.Setup(p => p.ValidatePasswordAsync("weak", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PasswordPolicyResult(false, [])));

        // Act
        var result = await _subject.ResetPasswordAsync(userId, "weak", modifiedByUserId);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<Unit>>());
        _userRepository.Verify(
            r => r.UpdatePasswordAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool?>(),
                It.IsAny<bool?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task CreateLocalUserAsync_ValidatesPasswordAndCreatesUser()
    {
        // Arrange
        var username = "newuser";
        var email = "new@example.com";
        var password = "securepassword";
        var createdByUserId = Guid.NewGuid();
        var createdUser = new User(
            Guid.NewGuid(),
            username,
            email,
            null,
            null,
            true,
            Role.Viewer,
            ImmutableDictionary<string, string?>.Empty,
            null,
            DateTimeOffset.UtcNow,
            createdByUserId,
            null,
            null);

        _passwordPolicy.Setup(p => p.ValidatePasswordAsync(password, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PasswordPolicyResult(true, [])));
        _userRepository.Setup(r => r.GetByUsernameAsync(username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(null));
        _passwordHasher.Setup(h => h.HashPasswordAsync(password, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new HashedPassword("hash", "salt")));
        _userRepository
            .Setup(r => r.CreateAsync(
                username,
                email,
                Guid.Empty,
                null,
                true,
                Role.Viewer,
                "hash",
                "salt",
                It.IsAny<ImmutableDictionary<string, string>>(),
                It.IsAny<DateTimeOffset>(),
                createdByUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(createdUser));

        // Act
        var result = await _subject.CreateLocalUserAsync(username,
            email,
            password,
            Role.Viewer,
            true,
            createdByUserId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<User>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(User.Username)).EqualTo(username));
    }

    [Test]
    public async Task CreateLocalUserAsync_Fails_WhenUsernameAlreadyExists()
    {
        // Arrange
        var existingUser = new User(
            Guid.NewGuid(),
            "existinguser",
            null,
            null,
            null,
            true,
            Role.Viewer,
            ImmutableDictionary<string, string?>.Empty,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        _passwordPolicy.Setup(p => p.ValidatePasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PasswordPolicyResult(true, [])));
        _userRepository.Setup(r => r.GetByUsernameAsync("existinguser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<User?>(existingUser));

        // Act
        var result = await _subject.CreateLocalUserAsync(
            "existinguser",
            null,
            "password",
            Role.Viewer,
            true,
            null);

        // Assert
        Assert.That(result, Is.InstanceOf<FailureResult<User>>());
    }
}
