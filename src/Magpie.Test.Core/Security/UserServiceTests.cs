using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core.Security;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Models.Security;
using Magpie.Sdk.Services;

using Microsoft.EntityFrameworkCore;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Security;

[TestFixture]
public sealed class UserServiceTests
{
    private static readonly Guid CallerUserId = new("11111111-1111-1111-1111-111111111111");

    private Mock<IPasswordHasher> _passwordHasher = null!;
    private Mock<IUserRepository> _users = null!;
    private Mock<IRefreshTokenRepository> _refreshTokens = null!;
    private IUserService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _passwordHasher = new Mock<IPasswordHasher>();
        _passwordHasher
            .Setup(hasher => hasher.HashPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashedPassword("hash", "salt"));

        _users = new Mock<IUserRepository>();
        _users
            .Setup(repository => repository.QueryAsync(
                It.IsAny<UserQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User> { Items = [], TotalItems = 0 });
        _users
            .Setup(repository => repository.CreateAsync(
                It.IsAny<User>(),
                CallerUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Username = "newuser",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            });

        _refreshTokens = new Mock<IRefreshTokenRepository>();

        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken token) => operation(token));

        _subject = new UserService(
            _users.Object,
            _refreshTokens.Object,
            _passwordHasher.Object,
            new DefaultPasswordPolicy(),
            transactionManager.Object,
            TimeProvider.System);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CreateAsync_CreatesUser_WhenEnabledStateSpecified(bool enabled)
    {
        // Arrange
        var create = new LocalUserCreate
        {
            Username = "newuser",
            Password = "password123",
            Role = Role.Viewer,
            Enabled = enabled
        };

        // Act
        await _subject.CreateAsync(create, CallerUserId).ConfigureAwait(false);

        // Assert
        _users.Verify(
            repository => repository.CreateAsync(
                It.Is<User>(user =>
                    user.Username == "newuser" &&
                    user.ProviderType == "local" &&
                    user.Role == Role.Viewer &&
                    user.Enabled == enabled),
                CallerUserId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void CreateAsync_ThrowsMagpieException_WhenUsernameIsBlank(string username)
    {
        // Arrange
        var create = new LocalUserCreate
        {
            Username = username,
            Password = "password123",
            Role = Role.Viewer,
            Enabled = true
        };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(
            () => _subject.CreateAsync(create, CallerUserId));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Username is required."));
        _users.Verify(
            repository => repository.CreateAsync(
                It.IsAny<User>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task CreateAsync_StoresTrimmedUsername_WhenUsernameHasOuterWhitespace()
    {
        // Arrange
        var create = new LocalUserCreate
        {
            Username = "  newuser  ",
            Password = "password123",
            Role = Role.Viewer,
            Enabled = true
        };

        // Act
        await _subject.CreateAsync(create, CallerUserId).ConfigureAwait(false);

        // Assert
        _users.Verify(
            repository => repository.CreateAsync(
                It.Is<User>(user => user.Username == "newuser"),
                CallerUserId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task CreateAsync_ReturnsNull_WhenUsernameAlreadyExists()
    {
        // Arrange
        _users
            .Setup(repository => repository.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "newuser"),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [new User
            {
                Username = "newuser",
                ProviderType = "local"
            }],
                TotalItems = 1
            });
        var create = new LocalUserCreate
        {
            Username = "newuser",
            Password = "password123",
            Role = Role.Viewer,
            Enabled = true
        };

        // Act
        var result = await _subject.CreateAsync(create, CallerUserId).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
        _users.Verify(
            repository => repository.CreateAsync(
                It.IsAny<User>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void CreateAsync_ThrowsDbUpdateException_WhenInsertFails()
    {
        // Arrange
        _users
            .Setup(repository => repository.CreateAsync(
                It.IsAny<User>(),
                CallerUserId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Database unavailable"));
        var create = new LocalUserCreate
        {
            Username = "newuser",
            Password = "password123",
            Role = Role.Viewer,
            Enabled = true
        };

        // Act
        var exception = Assert.ThrowsAsync<DbUpdateException>(
            () => _subject.CreateAsync(create, CallerUserId));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Database unavailable"));
        _users.Verify(
            repository => repository.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "newuser"),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void CreateAsync_ThrowsMagpieException_WhenPasswordHasSevenCharacters()
    {
        // Arrange
        var create = new LocalUserCreate
        {
            Username = "newuser",
            Password = "1234567",
            Role = Role.Viewer,
            Enabled = true
        };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(
            () => _subject.CreateAsync(create, CallerUserId));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Password must be at least 8 characters long"));
        _passwordHasher.Verify(
            hasher => hasher.HashPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task CreateAsync_CreatesUser_WhenPasswordHasEightCharacters()
    {
        // Arrange
        var create = new LocalUserCreate
        {
            Username = "newuser",
            Password = "12345678",
            Role = Role.Viewer,
            Enabled = true
        };

        // Act
        await _subject.CreateAsync(create, CallerUserId).ConfigureAwait(false);

        // Assert
        _passwordHasher.Verify(
            hasher => hasher.HashPasswordAsync("12345678", It.IsAny<CancellationToken>()),
            Times.Once);
        _users.Verify(
            repository => repository.CreateAsync(
                It.IsAny<User>(),
                CallerUserId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCase("", "Password cannot be empty Password must be at least 8 characters long")]
    [TestCase("        ", "Password cannot be empty Password cannot contain leading or trailing whitespace")]
    public void CreateAsync_ThrowsMagpieException_WhenPasswordIsEmptyOrWhitespaceOnly(
        string password,
        string expectedMessage)
    {
        // Arrange
        var create = new LocalUserCreate
        {
            Username = "newuser",
            Password = password,
            Role = Role.Viewer,
            Enabled = true
        };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(
            () => _subject.CreateAsync(create, CallerUserId));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo(expectedMessage));
        _passwordHasher.Verify(
            hasher => hasher.HashPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCase(" secret1")]
    [TestCase("secret1 ")]
    public void CreateAsync_ThrowsMagpieException_WhenPasswordHasLeadingOrTrailingWhitespace(string password)
    {
        // Arrange
        var create = new LocalUserCreate
        {
            Username = "newuser",
            Password = password,
            Role = Role.Viewer,
            Enabled = true
        };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(
            () => _subject.CreateAsync(create, CallerUserId));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Password cannot contain leading or trailing whitespace"));
        _passwordHasher.Verify(
            hasher => hasher.HashPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void ChangePasswordAsync_ThrowsMagpieException_WhenAdminPasswordHasSevenCharacters()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _users
            .Setup(repository => repository.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = userId,
                Username = "target",
                ProviderType = "local"
            });
        var change = new PasswordChange { NewPassword = "1234567" };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(
            () => _subject.ChangePasswordAsync(userId, change, CallerUserId, true));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Password must be at least 8 characters long"));
        _users.Verify(
            repository => repository.SetPasswordAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void ChangePasswordAsync_ThrowsMagpieException_WhenSelfServicePasswordHasSevenCharacters()
    {
        // Arrange
        var user = new User
        {
            Id = CallerUserId,
            Username = "caller",
            ProviderType = "local",
            PasswordHash = "old-hash",
            PasswordSalt = "old-salt"
        };
        _users
            .Setup(repository => repository.GetByIdAsync(CallerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasher
            .Setup(hasher => hasher.VerifyPasswordAsync(
                "current-password",
                "old-hash",
                "old-salt",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var change = new PasswordChange
        {
            CurrentPassword = "current-password",
            NewPassword = "1234567"
        };

        // Act
        var exception = Assert.ThrowsAsync<MagpieException>(
            () => _subject.ChangePasswordAsync(CallerUserId, change, CallerUserId, false));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Password must be at least 8 characters long"));
        _users.Verify(
            repository => repository.SetPasswordAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task VerifyPasswordAsync_ReturnsTrue_WhenLegacyPasswordIsShort()
    {
        // Arrange
        var user = new User
        {
            Username = "legacy",
            ProviderType = "local",
            PasswordHash = "old-hash",
            PasswordSalt = "old-salt"
        };
        _passwordHasher
            .Setup(hasher => hasher.VerifyPasswordAsync(
                "short",
                "old-hash",
                "old-salt",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject.VerifyPasswordAsync(user, "short").ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task PatchAsync_RevokesTheSessionsOfTheUser_WhenTheEnabledStateChanges(bool enabled)
    {
        // Arrange
        var userId = new Guid("22222222-2222-2222-2222-222222222222");
        _users
            .Setup(repository => repository.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = userId,
                Username = "target",
                ProviderType = "local",
                Enabled = !enabled,
                Role = Role.Viewer
            });
        _users
            .Setup(repository => repository.PatchAsync(
                It.IsAny<UserPatch>(),
                CallerUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject
            .PatchAsync(userId, new UserPatch { Enabled = new() { Value = enabled } }, CallerUserId)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _refreshTokens.Verify(
                repository => repository.DeleteByUserAsync(userId, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task PatchAsync_RevokesTheSessionsOfTheUser_WhenTheRoleChanges()
    {
        // Arrange
        var userId = new Guid("22222222-2222-2222-2222-222222222222");
        _users
            .Setup(repository => repository.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = userId,
                Username = "target",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Requester
            });
        _users
            .Setup(repository => repository.PatchAsync(
                It.IsAny<UserPatch>(),
                CallerUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject
            .PatchAsync(userId, new UserPatch { Role = new() { Value = Role.Viewer } }, CallerUserId)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _refreshTokens.Verify(
                repository => repository.DeleteByUserAsync(userId, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task PatchAsync_KeepsTheSessionsOfTheUser_WhenTheRoleIsUnchanged()
    {
        // Arrange
        var userId = new Guid("22222222-2222-2222-2222-222222222222");
        _users
            .Setup(repository => repository.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = userId,
                Username = "target",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Requester
            });
        _users
            .Setup(repository => repository.PatchAsync(
                It.IsAny<UserPatch>(),
                CallerUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject
            .PatchAsync(userId, new UserPatch { Role = new() { Value = Role.Requester } }, CallerUserId)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _refreshTokens.Verify(
                repository => repository.DeleteByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task PatchAsync_KeepsTheSessionsOfTheUser_WhenTheEnabledStateIsUnchanged()
    {
        // Arrange
        var userId = new Guid("22222222-2222-2222-2222-222222222222");
        _users
            .Setup(repository => repository.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = userId,
                Username = "target",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            });
        _users
            .Setup(repository => repository.PatchAsync(
                It.IsAny<UserPatch>(),
                CallerUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject
            .PatchAsync(userId, new UserPatch { Enabled = new() { Value = true } }, CallerUserId)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _refreshTokens.Verify(
                repository => repository.DeleteByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task PatchAsync_KeepsTheSessionsOfTheUser_WhenRoleAndEnabledStateAreUnchanged()
    {
        // Arrange
        var userId = new Guid("22222222-2222-2222-2222-222222222222");
        _users
            .Setup(repository => repository.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = userId,
                Username = "target",
                ProviderType = "local",
                Enabled = false,
                Role = Role.Manager
            });
        _users
            .Setup(repository => repository.PatchAsync(
                It.IsAny<UserPatch>(),
                CallerUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject.PatchAsync(
            userId,
            new UserPatch
            {
                Role = new() { Value = Role.Manager },
                Enabled = new() { Value = false }
            },
            CallerUserId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _refreshTokens.Verify(
                repository => repository.DeleteByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task PatchAsync_KeepsTheSessionsOfTheUser_WhenThePatchCarriesNoSecurityField()
    {
        // Arrange
        var userId = new Guid("22222222-2222-2222-2222-222222222222");
        _users
            .Setup(repository => repository.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = userId,
                Username = "target",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            });
        _users
            .Setup(repository => repository.PatchAsync(
                It.IsAny<UserPatch>(),
                CallerUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _subject.PatchAsync(userId, new UserPatch(), CallerUserId).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _refreshTokens.Verify(
                repository => repository.DeleteByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task PatchAsync_KeepsTheSessionsOfTheUser_WhenTheUserRepositoryPatchFails()
    {
        // Arrange
        var userId = new Guid("22222222-2222-2222-2222-222222222222");
        _users
            .Setup(repository => repository.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = userId,
                Username = "target",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            });
        _users
            .Setup(repository => repository.PatchAsync(
                It.IsAny<UserPatch>(),
                CallerUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _subject
            .PatchAsync(userId, new UserPatch { Role = new() { Value = Role.Admin } }, CallerUserId)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            _refreshTokens.Verify(
                repository => repository.DeleteByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
