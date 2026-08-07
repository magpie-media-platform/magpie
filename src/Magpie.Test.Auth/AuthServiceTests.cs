using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Auth;
using Magpie.Auth;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Auth;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public class AuthServiceTests
{
    private Mock<ITokenService> _tokenService = null!;
    private Mock<IUserService> _userService = null!;
    private Mock<IEventBus> _eventBus = null!;
    private ILoggerFactory _loggerFactory = null!;

    [SetUp]
    public void SetUp()
    {
        _tokenService = new Mock<ITokenService>();
        _userService = new Mock<IUserService>();
        _eventBus = new Mock<IEventBus>();
        _loggerFactory = NullLoggerFactory.Instance;
    }

    [TearDown]
    public void TearDown()
    {
        _loggerFactory?.Dispose();
    }

    [Test]
    public async Task VerifyCredentials_ReachesExtensionProvider_WhenLocalDeclines()
    {
        // Arrange
        var local = new Mock<ICredentialAuthenticationProvider>(MockBehavior.Strict);
        local.SetupGet(p => p.ProviderType).Returns("local");

        var ldapUser = new User
        {
            Id = Guid.NewGuid(),
            Username = "jdoe",
            ProviderType = "ldap",
            ExternalId = "jdoe",
            Enabled = true,
            Role = Role.Viewer,
            CreatedAt = DateTime.UtcNow
        };
        var ldap = new Mock<ICredentialAuthenticationProvider>(MockBehavior.Strict);
        ldap.SetupGet(p => p.ProviderType).Returns("ldap");

        var sequence = new MockSequence();
        local.InSequence(sequence)
            .Setup(p => p.VerifyCredentialsAsync("jdoe", "secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        ldap.InSequence(sequence)
            .Setup(p => p.VerifyCredentialsAsync("jdoe", "secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ldapUser);

        var subject = new AuthService(
            [ldap.Object, local.Object],
            _tokenService.Object,
            _userService.Object,
            _eventBus.Object,
            new AccountLockoutOptions(),
            TimeProvider.System,
            _loggerFactory.CreateLogger<AuthService>());

        // Act
        var result = await subject.VerifyCredentialsAsync("jdoe", "secret").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.ProviderType, Is.EqualTo("ldap"));
        }

        local.Verify(
            p => p.VerifyCredentialsAsync("jdoe", "secret", It.IsAny<CancellationToken>()),
            Times.Once);
        ldap.Verify(
            p => p.VerifyCredentialsAsync("jdoe", "secret", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task VerifyCredentials_PrefersLocal_WhenLocalAuthenticates()
    {
        // Arrange
        var localUser = new User
        {
            Id = Guid.NewGuid(),
            Username = "admin",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };
        var local = new Mock<ICredentialAuthenticationProvider>(MockBehavior.Strict);
        local.SetupGet(p => p.ProviderType).Returns("local");
        local
            .Setup(p => p.VerifyCredentialsAsync("admin", "secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(localUser);

        var ldap = new Mock<ICredentialAuthenticationProvider>(MockBehavior.Strict);
        ldap.SetupGet(p => p.ProviderType).Returns("ldap");

        var subject = new AuthService(
            [ldap.Object, local.Object],
            _tokenService.Object,
            _userService.Object,
            _eventBus.Object,
            new AccountLockoutOptions(),
            TimeProvider.System,
            _loggerFactory.CreateLogger<AuthService>());

        // Act
        var result = await subject.VerifyCredentialsAsync("admin", "secret").ConfigureAwait(false);

        // Assert
        Assert.That(result!.ProviderType, Is.EqualTo("local"));
        ldap.Verify(
            p => p.VerifyCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task VerifyCredentials_ReturnsNull_WhenNoProviderAuthenticates()
    {
        // Arrange
        var local = new Mock<ICredentialAuthenticationProvider>(MockBehavior.Strict);
        local.SetupGet(p => p.ProviderType).Returns("local");

        var ldap = new Mock<ICredentialAuthenticationProvider>(MockBehavior.Strict);
        ldap.SetupGet(p => p.ProviderType).Returns("ldap");

        var sequence = new MockSequence();
        local.InSequence(sequence)
            .Setup(p => p.VerifyCredentialsAsync("nobody", "secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        ldap.InSequence(sequence)
            .Setup(p => p.VerifyCredentialsAsync("nobody", "secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var subject = new AuthService(
            [ldap.Object, local.Object],
            _tokenService.Object,
            _userService.Object,
            _eventBus.Object,
            new AccountLockoutOptions(),
            TimeProvider.System,
            _loggerFactory.CreateLogger<AuthService>());

        // Act
        var result = await subject.VerifyCredentialsAsync("nobody", "secret").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }
}
