using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core.Security;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Security;

[TestFixture]
public sealed class ApiKeyServiceTests
{
    private static readonly Guid OwnerUserId = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OtherUserId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AdminUserId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ApiKeyId = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SystemUserId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EnabledUserId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DisabledUserId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime Now = new(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    private Mock<IApiKeyRepository> _apiKeys = null!;
    private Mock<IPasswordHasher> _passwordHasher = null!;
    private Mock<IUserRepository> _users = null!;
    private IApiKeyService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _apiKeys = new Mock<IApiKeyRepository>();
        _passwordHasher = new Mock<IPasswordHasher>();
        _users = new Mock<IUserRepository>();
        _apiKeys
            .Setup(repository => repository.GetByIdAsync(ApiKeyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiKey
            {
                Id = ApiKeyId,
                UserId = OwnerUserId,
                Name = "OwnedKey",
                KeyPrefix = "mgp_own",
                KeyHash = "ownedhash",
                Enabled = true
            });
        _apiKeys
            .Setup(repository => repository.PatchAsync(
                It.IsAny<ApiKeyPatch>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _passwordHasher
            .Setup(hasher => hasher.HashPasswordUnsaltedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("hashed-key");
        _users
            .Setup(repository => repository.GetByIdAsync(SystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = SystemUserId,
                Username = "system",
                ProviderType = "system",
                Enabled = false,
                Role = Role.Admin
            });
        _users
            .Setup(repository => repository.GetByIdAsync(AdminUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = AdminUserId,
                Username = "admin",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Admin
            });
        _users
            .Setup(repository => repository.GetByIdAsync(OtherUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = OtherUserId,
                Username = "bob",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            });
        _users
            .Setup(repository => repository.GetByIdAsync(EnabledUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = EnabledUserId,
                Username = "alice",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            });

        _subject = new ApiKeyService(
            _apiKeys.Object,
            _passwordHasher.Object,
            _users.Object,
            new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)));
    }

    [Test]
    public async Task PatchAsync_PatchesTheKey_WhenCallerOwnsIt()
    {
        // Act
        var result = await _subject
            .PatchAsync(
                ApiKeyId,
                new ApiKeyPatch { Enabled = new() { Value = false } },
                OwnerUserId,
                callerIsAdmin: false)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _apiKeys.Verify(
                repository => repository.PatchAsync(
                    It.Is<ApiKeyPatch>(patch => patch.Id == ApiKeyId && patch.Enabled!.Value == false),
                    OwnerUserId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalseWithoutWriting_WhenCallerDoesNotOwnTheKey()
    {
        // Act
        var result = await _subject
            .PatchAsync(
                ApiKeyId,
                new ApiKeyPatch { Enabled = new() { Value = false } },
                OtherUserId,
                callerIsAdmin: false)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            _apiKeys.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<ApiKeyPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTheKey_WhenCallerIsAdmin()
    {
        // Act
        var result = await _subject
            .PatchAsync(
                ApiKeyId,
                new ApiKeyPatch { Enabled = new() { Value = false } },
                AdminUserId,
                callerIsAdmin: true)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _apiKeys.Verify(
                repository => repository.PatchAsync(
                    It.Is<ApiKeyPatch>(patch => patch.Id == ApiKeyId),
                    AdminUserId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalseWithoutWriting_WhenApiKeyNotFound()
    {
        // Act
        var result = await _subject
            .PatchAsync(
                new Guid("55555555-5555-5555-5555-555555555555"),
                new ApiKeyPatch { Enabled = new() { Value = false } },
                OwnerUserId,
                callerIsAdmin: true)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            _apiKeys.Verify(
                repository => repository.PatchAsync(
                    It.IsAny<ApiKeyPatch>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task CreateAsync_CreatesKeyForCaller_WhenUsernameOmittedAndCallerEnabled()
    {
        // Arrange
        _users
            .Setup(repository => repository.GetByIdAsync(EnabledUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = EnabledUserId,
                Username = "alice",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Admin
            });
        _apiKeys
            .Setup(repository => repository.CreateAsync(
                It.IsAny<ApiKey>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Returns((ApiKey key, Guid? _, CancellationToken _) => Task.FromResult(key));

        // Act
        var result = await _subject
            .CreateAsync(new ApiKeyCreate { Name = "ci-bootstrap", ExpiresAt = null }, EnabledUserId)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ApiKey.UserId, Is.EqualTo(EnabledUserId));
            Assert.That(result.ApiKey.Name, Is.EqualTo("ci-bootstrap"));
            Assert.That(result.PlaintextKey, Does.StartWith("mgp_"));
            _apiKeys.Verify(
                repository => repository.CreateAsync(
                    It.Is<ApiKey>(key => key.UserId == EnabledUserId && key.Name == "ci-bootstrap"),
                    EnabledUserId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public void CreateAsync_ThrowsArgumentException_WhenUsernameOmittedAndCallerIsSystemAccount()
    {
        // Arrange
        _users
            .Setup(repository => repository.GetByIdAsync(SystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = SystemUserId,
                Username = "system",
                ProviderType = "system",
                Enabled = false,
                Role = Role.Admin
            });

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateAsync(
            new ApiKeyCreate { Name = "qa-full-lifecycle", ExpiresAt = null },
            SystemUserId));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("user is required"));
            _apiKeys.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<ApiKey>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public void CreateAsync_ThrowsArgumentException_WhenUsernameOmittedAndCallerDisabled()
    {
        // Arrange
        _users
            .Setup(repository => repository.GetByIdAsync(DisabledUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = DisabledUserId,
                Username = "carol",
                ProviderType = "local",
                Enabled = false,
                Role = Role.Viewer
            });

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateAsync(
            new ApiKeyCreate { Name = "qa-full-lifecycle", ExpiresAt = null },
            DisabledUserId));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("user is required"));
            _apiKeys.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<ApiKey>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task CreateForUserAsync_CreatesKeyForNamedUser_WhenAdminSuppliesUsername()
    {
        // Arrange
        _users
            .Setup(repository => repository.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "alice"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [new User
            {
                Id = EnabledUserId,
                Username = "alice",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Admin
            }],
                TotalItems = 1
            });
        _apiKeys
            .Setup(repository => repository.CreateAsync(
                It.IsAny<ApiKey>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Returns((ApiKey key, Guid? _, CancellationToken _) => Task.FromResult(key));

        // Act
        var result = await _subject
            .CreateForUserAsync(new ApiKeyCreate { Name = "ci-bootstrap", ExpiresAt = null }, SystemUserId, "alice")
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ApiKey.UserId, Is.EqualTo(EnabledUserId));
            Assert.That(result.PlaintextKey, Does.StartWith("mgp_"));
            _apiKeys.Verify(
                repository => repository.CreateAsync(
                    It.Is<ApiKey>(key => key.UserId == EnabledUserId && key.Name == "ci-bootstrap"),
                    SystemUserId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public void CreateForUserAsync_ThrowsArgumentException_WhenUsernameUnknown()
    {
        // Arrange
        _users
            .Setup(repository => repository.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "ghost"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User> { Items = [], TotalItems = 0 });

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateForUserAsync(
            new ApiKeyCreate { Name = "ci-bootstrap", ExpiresAt = null },
            SystemUserId,
            "ghost"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("No user named 'ghost' was found."));
            _apiKeys.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<ApiKey>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public void CreateForUserAsync_ThrowsArgumentException_WhenNamedUserDisabled()
    {
        // Arrange
        _users
            .Setup(repository => repository.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "alice"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [new User
            {
                Id = EnabledUserId,
                Username = "alice",
                ProviderType = "local",
                Enabled = false,
                Role = Role.Admin
            }],
                TotalItems = 1
            });

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateForUserAsync(
            new ApiKeyCreate { Name = "ci-bootstrap", ExpiresAt = null },
            SystemUserId,
            "alice"));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("User 'alice' is disabled and cannot own an API key."));
    }

    [Test]
    public void CreateForUserAsync_ThrowsArgumentException_WhenNamedUserIsSystemAccount()
    {
        // Arrange
        _users
            .Setup(repository => repository.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "system"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [new User
            {
                Id = SystemUserId,
                Username = "system",
                ProviderType = "system",
                Enabled = false,
                Role = Role.Admin
            }],
                TotalItems = 1
            });

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateForUserAsync(
            new ApiKeyCreate { Name = "ci-bootstrap", ExpiresAt = null },
            AdminUserId,
            "system"));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("The system account cannot own an API key."));
    }

    [Test]
    public void CreateForUserAsync_ThrowsUnauthorizedAccessException_WhenNonAdminCreatesForOtherUser()
    {
        // Arrange
        _users
            .Setup(repository => repository.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "alice"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [new User
            {
                Id = EnabledUserId,
                Username = "alice",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            }],
                TotalItems = 1
            });

        // Act
        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(() => _subject.CreateForUserAsync(
            new ApiKeyCreate { Name = "ci-bootstrap", ExpiresAt = null },
            OtherUserId,
            "alice"));

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("Only administrators can create API keys for other users."));
    }

    [TestCase("ghost")]
    [TestCase("alice")]
    [TestCase("carol")]
    [TestCase("system")]
    public void CreateForUserAsync_ThrowsIdenticalUnauthorizedAccessException_WhenNonAdminProbesNonOwnedUsername(
        string username)
    {
        // Arrange
        _users
            .Setup(repository => repository.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "ghost"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User> { Items = [], TotalItems = 0 });
        _users
            .Setup(repository => repository.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "alice"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [new User
            {
                Id = EnabledUserId,
                Username = "alice",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            }],
                TotalItems = 1
            });
        _users
            .Setup(repository => repository.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "carol"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [new User
            {
                Id = DisabledUserId,
                Username = "carol",
                ProviderType = "local",
                Enabled = false,
                Role = Role.Viewer
            }],
                TotalItems = 1
            });
        _users
            .Setup(repository => repository.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "system"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [new User
            {
                Id = SystemUserId,
                Username = "system",
                ProviderType = "system",
                Enabled = false,
                Role = Role.Admin
            }],
                TotalItems = 1
            });

        // Act
        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(() => _subject.CreateForUserAsync(
            new ApiKeyCreate { Name = "ci-bootstrap", ExpiresAt = null },
            OtherUserId,
            username));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo("Only administrators can create API keys for other users."));
            _apiKeys.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<ApiKey>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task CreateForUserAsync_CreatesKeyForCaller_WhenNonAdminSuppliesOwnUsername()
    {
        // Arrange
        _users
            .Setup(repository => repository.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "alice"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [new User
            {
                Id = EnabledUserId,
                Username = "alice",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            }],
                TotalItems = 1
            });
        _apiKeys
            .Setup(repository => repository.CreateAsync(
                It.IsAny<ApiKey>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Returns((ApiKey key, Guid? _, CancellationToken _) => Task.FromResult(key));

        // Act
        var result = await _subject
            .CreateForUserAsync(new ApiKeyCreate { Name = "mine", ExpiresAt = null }, EnabledUserId, "alice")
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.ApiKey.UserId, Is.EqualTo(EnabledUserId));
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("\t")]
    public void CreateAsync_ThrowsArgumentExceptionWithoutWriting_WhenNameIsBlank(string name)
    {
        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateAsync(
            new ApiKeyCreate { Name = name, ExpiresAt = null },
            EnabledUserId));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("create.Name"));
            _apiKeys.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<ApiKey>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("\t")]
    public void CreateForUserAsync_ThrowsArgumentExceptionWithoutWriting_WhenNameIsBlank(string name)
    {
        // Arrange
        _users
            .Setup(repository => repository.QueryAsync(
                It.Is<UserQuery>(query => query.Username == "alice"),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1 && paging.PageSize == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<User>
            {
                Items = [new User
            {
                Id = EnabledUserId,
                Username = "alice",
                ProviderType = "local",
                Enabled = true,
                Role = Role.Viewer
            }],
                TotalItems = 1
            });

        // Act
        var exception = Assert.ThrowsAsync<ArgumentException>(() => _subject.CreateForUserAsync(
            new ApiKeyCreate { Name = name, ExpiresAt = null },
            EnabledUserId,
            "alice"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("create.Name"));
            _apiKeys.Verify(
                repository => repository.CreateAsync(
                    It.IsAny<ApiKey>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task TryValidateAsync_WritesTheLastUsedTime_WhenTheKeyIsValid()
    {
        // Arrange
        _apiKeys
            .Setup(repository => repository.QueryAsync(
                It.Is<ApiKeyQuery>(query => query.KeyHash == "hashed-key"),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ApiKey>
            {
                Items = [new ApiKey
            {
                Id = ApiKeyId,
                UserId = OwnerUserId,
                Name = "OwnedKey",
                KeyPrefix = "mgp_own",
                KeyHash = "hashed-key",
                Enabled = true,
                LastUsedAt = null
            }],
                TotalItems = 1
            });

        // Act
        var result = await _subject.TryValidateAsync("mgp_valid").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.LastUsedAt, Is.EqualTo(new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc)));
            _apiKeys.Verify(
                repository => repository.UpdateLastUsedAsync(
                    ApiKeyId,
                    new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task TryValidateAsync_KeepsTheLastUsedTime_WhenTheKeyWasUsedInsideTheWriteInterval()
    {
        // Arrange
        _apiKeys
            .Setup(repository => repository.QueryAsync(
                It.Is<ApiKeyQuery>(query => query.KeyHash == "hashed-key"),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ApiKey>
            {
                Items = [new ApiKey
            {
                Id = ApiKeyId,
                UserId = OwnerUserId,
                Name = "OwnedKey",
                KeyPrefix = "mgp_own",
                KeyHash = "hashed-key",
                Enabled = true,
                LastUsedAt = new DateTime(2026, 8, 19, 11, 59, 30, DateTimeKind.Utc)
            }],
                TotalItems = 1
            });

        // Act
        var result = await _subject.TryValidateAsync("mgp_valid").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.LastUsedAt, Is.EqualTo(new DateTime(2026, 8, 19, 11, 59, 30, DateTimeKind.Utc)));
            _apiKeys.Verify(
                repository => repository.UpdateLastUsedAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task TryValidateAsync_WritesNothing_WhenTheKeyIsDisabled()
    {
        // Arrange
        _apiKeys
            .Setup(repository => repository.QueryAsync(
                It.Is<ApiKeyQuery>(query => query.KeyHash == "hashed-key"),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ApiKey>
            {
                Items = [new ApiKey
            {
                Id = ApiKeyId,
                UserId = OwnerUserId,
                Name = "OwnedKey",
                KeyPrefix = "mgp_own",
                KeyHash = "hashed-key",
                Enabled = false,
            }],
                TotalItems = 1
            });

        // Act
        var result = await _subject.TryValidateAsync("mgp_rejected").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            _apiKeys.Verify(
                repository => repository.UpdateLastUsedAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task TryValidateAsync_WritesNothing_WhenTheKeyIsExpired()
    {
        // Arrange
        _apiKeys
            .Setup(repository => repository.QueryAsync(
                It.Is<ApiKeyQuery>(query => query.KeyHash == "hashed-key"),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ApiKey>
            {
                Items = [new ApiKey
            {
                Id = ApiKeyId,
                UserId = OwnerUserId,
                Name = "OwnedKey",
                KeyPrefix = "mgp_own",
                KeyHash = "hashed-key",
                Enabled = true,
                ExpiresAt = new DateTime(2026, 8, 19, 11, 0, 0)
            }],
                TotalItems = 1
            });

        // Act
        var result = await _subject.TryValidateAsync("mgp_rejected").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            _apiKeys.Verify(
                repository => repository.UpdateLastUsedAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
