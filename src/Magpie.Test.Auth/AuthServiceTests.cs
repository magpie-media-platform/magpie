using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Auth;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Auth;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Auth;

[TestFixture]
public class AuthServiceTests
{
    private static readonly object[] PermissionRoles =
    [
        new object[] { "content.view", Role.Viewer, true, true, true },
        new object[] { "content.edit", Role.Manager, false, false, true },
        new object[] { "content.add", Role.Requester, false, true, true },
        new object[] { "content.delete", Role.Manager, false, false, true },
        new object[] { "content.request", Role.Requester, false, true, true },
        new object[] { "subjects.view", Role.Viewer, true, true, true },
        new object[] { "subjects.edit", Role.Requester, false, true, true },
        new object[] { "downloads.view", Role.Viewer, true, true, true },
        new object[] { "downloads.control", Role.Requester, false, true, true },
        new object[] { "downloads.manage", Role.Manager, false, false, true },
        new object[] { "indexers.manage", Role.Admin, false, false, false },
        new object[] { "clients.manage", Role.Admin, false, false, false },
        new object[] { "servers.manage", Role.Admin, false, false, false },
        new object[] { "folders.manage", Role.Admin, false, false, false },
        new object[] { "jobs.view", Role.Viewer, true, true, true },
        new object[] { "jobs.run", Role.Requester, false, true, true },
        new object[] { "jobs.schedule", Role.Requester, false, true, true },
        new object[] { "jobs.manage", Role.Manager, false, false, true },
        new object[] { "wishlists.view", Role.Viewer, true, true, true },
        new object[] { "wishlists.own", Role.Requester, false, true, true },
        new object[] { "wishlists.manage", Role.Requester, false, true, true },
        new object[] { "quality.view", Role.Viewer, true, true, true },
        new object[] { "quality.manage", Role.Admin, false, false, false },
        new object[] { "tags.view", Role.Viewer, true, true, true },
        new object[] { "tags.manage", Role.Manager, false, false, true },
        new object[] { "filters.view", Role.Viewer, true, true, true },
        new object[] { "filters.own", Role.Viewer, true, true, true },
        new object[] { "filters.manage", Role.Viewer, true, true, true },
        new object[] { "activity.view", Role.Viewer, true, true, true },
        new object[] { "calendar.view", Role.Viewer, true, true, true },
        new object[] { "system.view", Role.Viewer, true, true, true },
        new object[] { "settings.view", Role.Manager, false, false, true },
        new object[] { "settings.edit", Role.Admin, false, false, false },
        new object[] { "users.manage", Role.Admin, false, false, false },
        new object[] { "extensions.manage", Role.Admin, false, false, false },
        new object[] { "backups.manage", Role.Admin, false, false, false },
        new object[] { "files.browse", Role.Manager, false, false, true }
    ];

    private Mock<ITokenService> _tokenService = null!;
    private Mock<IUserService> _userService = null!;
    private AuthService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _tokenService = new Mock<ITokenService>();
        _userService = new Mock<IUserService>();
        _subject = new AuthService(
            [],
            _tokenService.Object,
            _userService.Object,
            TimeProvider.System,
            NullLogger<AuthService>.Instance,
            Mock.Of<ITransactionManager>(),
            Mock.Of<IRefreshTokenRepository>(),
            Mock.Of<IDataProtectionService>());
    }

    [TestCaseSource(nameof(PermissionRoles))]
    public void GetEffectivePermissions_ReturnsExpectedGrants_WhenTheCallerHasEachRole(
        string permission,
        Role requiredRole,
        bool viewerGranted,
        bool requesterGranted,
        bool managerGranted)
    {
        // Arrange
        // Act
        var viewer = _subject.GetEffectivePermissions(Role.Viewer).Permissions[permission];
        var requester = _subject.GetEffectivePermissions(Role.Requester).Permissions[permission];
        var manager = _subject.GetEffectivePermissions(Role.Manager).Permissions[permission];
        var admin = _subject.GetEffectivePermissions(Role.Admin).Permissions[permission];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewer.RequiredRole, Is.EqualTo(requiredRole));
            Assert.That(requester.RequiredRole, Is.EqualTo(requiredRole));
            Assert.That(manager.RequiredRole, Is.EqualTo(requiredRole));
            Assert.That(admin.RequiredRole, Is.EqualTo(requiredRole));
            Assert.That(viewer.Granted, Is.EqualTo(viewerGranted));
            Assert.That(requester.Granted, Is.EqualTo(requesterGranted));
            Assert.That(manager.Granted, Is.EqualTo(managerGranted));
            Assert.That(admin.Granted, Is.True);
        }
    }

    [Test]
    public void GetEffectivePermissions_PublishesExpectedPermissions_WhenTheCatalogIsServed()
    {
        // Arrange
        // Act
        var permissions = _subject.GetEffectivePermissions(Role.Admin).Permissions.Keys;

        // Assert
        Assert.That(
            permissions,
            Is.EquivalentTo(
            [
                "content.view",
                "content.edit",
                "content.add",
                "content.delete",
                "content.request",
                "subjects.view",
                "subjects.edit",
                "downloads.view",
                "downloads.control",
                "downloads.manage",
                "indexers.manage",
                "clients.manage",
                "servers.manage",
                "folders.manage",
                "jobs.view",
                "jobs.run",
                "jobs.schedule",
                "jobs.manage",
                "wishlists.view",
                "wishlists.own",
                "wishlists.manage",
                "quality.view",
                "quality.manage",
                "tags.view",
                "tags.manage",
                "filters.view",
                "filters.own",
                "filters.manage",
                "activity.view",
                "calendar.view",
                "system.view",
                "settings.view",
                "settings.edit",
                "users.manage",
                "extensions.manage",
                "backups.manage",
                "files.browse"
            ]));
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
            TimeProvider.System,
            NullLogger<AuthService>.Instance,
            Mock.Of<ITransactionManager>(),
            Mock.Of<IRefreshTokenRepository>(),
            Mock.Of<IDataProtectionService>());

        // Act
        var result = await subject.VerifyCredentialsAsync("jdoe", "secret").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.ProviderType, Is.EqualTo("ldap"));
        }

        local.Verify(p => p.VerifyCredentialsAsync("jdoe", "secret", It.IsAny<CancellationToken>()), Times.Once);
        ldap.Verify(p => p.VerifyCredentialsAsync("jdoe", "secret", It.IsAny<CancellationToken>()), Times.Once);
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
            TimeProvider.System,
            NullLogger<AuthService>.Instance,
            Mock.Of<ITransactionManager>(),
            Mock.Of<IRefreshTokenRepository>(),
            Mock.Of<IDataProtectionService>());

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
            TimeProvider.System,
            NullLogger<AuthService>.Instance,
            Mock.Of<ITransactionManager>(),
            Mock.Of<IRefreshTokenRepository>(),
            Mock.Of<IDataProtectionService>());

        // Act
        var result = await subject.VerifyCredentialsAsync("nobody", "secret").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
    }
}
