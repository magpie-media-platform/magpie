using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Auth;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Sdk.Auth;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class AuthRefreshTransactionTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private ServiceProvider _services = null!;
    private IAuthService _auth = null!;
    private Mock<ITokenService> _tokens = null!;
    private User _user = null!;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        using var context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(_template)
            .Options);
        context.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void DisposeTemplate()
    {
        _template.Dispose();
    }

    [SetUp]
    public async Task SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync().ConfigureAwait(false);
        _template.BackupDatabase(connection);
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true).Options);
        _user = new User
        {
            Id = new Guid("31ed83cd-724d-4cbb-8ca6-d4c9066d07c4"),
            Username = "test",
            ProviderType = "local",
            Enabled = true,
        };
        _context.Users.Add(_user);
        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = _user.Id,
            TokenHash = "old-refresh",
            AccessTokenJti = "old-access-id",
            ExpiresAt = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var repository = new RefreshTokenRepository(_context, Mock.Of<IAuditRecordRepository>(), transactions,
            NullLogger<RefreshTokenRepository>.Instance, TimeProvider.System);
        _tokens = new Mock<ITokenService>();
        _tokens.Setup(tokens => tokens.TryConsumeRefreshTokenAsync("old-refresh", It.IsAny<CancellationToken>()))
            .Returns(async (string hash, CancellationToken token) =>
                (await repository.ConsumeAsync(hash, DateTime.UtcNow, token).ConfigureAwait(false))?.UserId);
        var users = new Mock<IUserService>();
        users.Setup(service => service.GetAuthenticationUserAsync(_user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_user);
        var services = new ServiceCollection();
        services.AddMagpieAuth(new ConfigurationBuilder().Build());
        services.RemoveAll<ICredentialAuthenticationProvider>();
        services.AddSingleton(_tokens.Object);
        services.AddSingleton(users.Object);
        services.AddSingleton(Mock.Of<IEventBus>());
        services.AddSingleton<ITransactionManager>(transactions);
        services.AddSingleton<IRefreshTokenRepository>(repository);
        services.AddSingleton(Mock.Of<IDataProtectionService>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _services = services.BuildServiceProvider();
        _auth = _services.GetRequiredService<IAuthService>();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync().ConfigureAwait(false);
        await _context.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task RefreshAsync_PreservesTheRefreshToken_WhenIssuingTheReplacementFails()
    {
        // Arrange
        _tokens.Setup(tokens => tokens.GenerateAccessTokenAsync(_user, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Temporary signing failure"));

        // Act
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _auth.RefreshAsync("old-refresh").ConfigureAwait(false));

        // Assert
        Assert.That(await _context.RefreshTokens.AsNoTracking()
            .AnyAsync(token => token.TokenHash == "old-refresh").ConfigureAwait(false), Is.True);
    }

    [TestCase(false, false)]
    [TestCase(true, true)]
    public async Task RefreshAsync_RejectsAndConsumesTheRefreshToken_WhenTheAccountIsRestricted(
        bool enabled, bool locked)
    {
        // Arrange
        _user.Enabled = enabled;
        _user.LockedUntil = locked ? new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null;

        // Act
        var session = await _auth.RefreshAsync("old-refresh").ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(session, Is.Null);
            Assert.That(await _context.RefreshTokens.AsNoTracking().AnyAsync().ConfigureAwait(false), Is.False);
        }

        _tokens.Verify(tokens => tokens.GenerateAccessTokenAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
