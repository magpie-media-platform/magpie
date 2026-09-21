using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Database;
using Magpie.Abstractions.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Host;

[TestFixture]
public sealed class DatabaseBootstrapTests
{
    private MagpieDbContext _context = null!;
    private WebApplicationBuilder _builder = null!;
    private Mock<IRecoverableDatabaseManagementProvider> _provider = null!;
    private Mock<IStartupValidationDatabaseManagementProvider> _validation = null!;

    [SetUp]
    public async Task SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite($"Data Source=bootstrap-{Guid.NewGuid():N};Mode=Memory;Pooling=false").Options);
        await _context.Database.OpenConnectionAsync().ConfigureAwait(false);
        _builder = WebApplication.CreateSlimBuilder();
        _builder.Logging.ClearProviders();
        _builder.Services.AddSingleton(_context);
        _builder.Services.AddSingleton(new MagpieHostEnvironment(
            "test", false, false, Path.GetTempPath(), Path.GetTempPath(), Path.GetTempPath(),
            Path.GetTempPath(), DateTime.UtcNow));
        _builder.Services.AddSingleton(Mock.Of<ISettingsRepository>());
        _builder.Services.AddSingleton(TimeProvider.System);
        _builder.Services.AddSingleton<ILogger<SQLiteDatabaseManagementProvider>>(
            NullLogger<SQLiteDatabaseManagementProvider>.Instance);
        _builder.Services.AddScoped<IDatabaseManagementProvider, SQLiteDatabaseManagementProvider>();
        _provider = new Mock<IRecoverableDatabaseManagementProvider>(MockBehavior.Strict);
        _validation = _provider.As<IStartupValidationDatabaseManagementProvider>();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task PrepareMagpieDatabaseAsync_AcceptsAnEmptyDatabase_WhenNoRestoreOrLockExists()
    {
        // Arrange
        var builder = _builder;

        // Act
        await builder.PrepareMagpieDatabaseAsync().ConfigureAwait(false);

        // Assert
        Assert.That(await _context.Database.CanConnectAsync().ConfigureAwait(false), Is.True);
    }

    [Test]
    public async Task PrepareMagpieDatabaseAsync_RejectsStartup_WhenAMigrationLockExists()
    {
        // Arrange
        await _context.Database.ExecuteSqlRawAsync(
            "CREATE TABLE __EFMigrationsLock (Id INTEGER, Timestamp TEXT); " +
            "INSERT INTO __EFMigrationsLock VALUES (1, '2026-09-08');").ConfigureAwait(false);

        // Act
        var action = () => _builder.PrepareMagpieDatabaseAsync();

        // Assert
        Assert.ThrowsAsync<MagpieException>(async () => await action().ConfigureAwait(false));
    }

    [TestCase("postgresql")]
    [TestCase("sqlserver")]
    public async Task PrepareMagpieDatabaseAsync_RestoresBeforeValidation_WhenARegisteredProviderHasBothCapabilities(
        string provider)
    {
        // Arrange
        _builder.Configuration["Magpie:Database:Provider"] = provider;
        _builder.Services.AddSingleton<IDatabaseManagementProvider>(_provider.Object);
        using var cancellation = new CancellationTokenSource();
        var sequence = new MockSequence();
        _provider.InSequence(sequence).Setup(value => value.ApplyPendingRestoreAsync(cancellation.Token))
            .ReturnsAsync(true);
        _validation.InSequence(sequence).Setup(value => value.ValidateStartupAsync(cancellation.Token))
            .ReturnsAsync(new DatabaseStartupValidationResult(true, null));

        // Act
        await _builder.PrepareMagpieDatabaseAsync(cancellation.Token).ConfigureAwait(false);

        // Assert
        _provider.Verify(value => value.ApplyPendingRestoreAsync(cancellation.Token), Times.Once);
        _validation.Verify(value => value.ValidateStartupAsync(cancellation.Token), Times.Once);
    }

    [Test]
    public void PrepareMagpieDatabaseAsync_RejectsStartup_WhenANonSqliteProviderFailsValidation()
    {
        // Arrange
        _builder.Configuration["Magpie:Database:Provider"] = "postgresql";
        _builder.Services.AddSingleton<IDatabaseManagementProvider>(_provider.Object);
        _provider.Setup(value => value.ApplyPendingRestoreAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _validation.Setup(value => value.ValidateStartupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DatabaseStartupValidationResult(false, "The database is locked."));

        // Act
        var action = () => _builder.PrepareMagpieDatabaseAsync();

        // Assert
        Assert.That(Assert.ThrowsAsync<MagpieException>(async () => await action().ConfigureAwait(false))!.Message,
            Is.EqualTo("The database is locked."));
    }
}
