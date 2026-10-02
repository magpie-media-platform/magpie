using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ExtensionActivationGenerationRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IExtensionActivationGenerationRepository _subject = null!;

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
    public void SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options);

        _context.ExtensionActivationStates.Add(new ExtensionActivationState { Id = 1, Generation = 0 });
        _context.SaveChanges();

        _subject = new ExtensionActivationGenerationRepository(
            _context,
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            NullLogger<ExtensionActivationGenerationRepository>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task GetCurrentAsync_ReturnsZero_WhenTheDatabaseIsNew()
    {
        // Act
        var generation = await _subject.GetCurrentAsync().ConfigureAwait(false);

        // Assert
        Assert.That(generation, Is.EqualTo(0));
    }

    [Test]
    public async Task BumpAsync_IncrementsGeneration_WhenExtensionExists()
    {
        // Act
        var first = await _subject.BumpAsync().ConfigureAwait(false);
        var second = await _subject.BumpAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(2));
            Assert.That(await _subject.GetCurrentAsync().ConfigureAwait(false), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetCurrentAsync_DoesNotInsert_WhenDatabaseHasNoSeed()
    {
        // Arrange
        await _context.ExtensionActivationStates.ExecuteDeleteAsync().ConfigureAwait(false);

        // Act
        var generation = await _subject.GetCurrentAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(generation, Is.Zero);
            Assert.That(await _context.ExtensionActivationStates.CountAsync().ConfigureAwait(false), Is.Zero);
        }
    }
}
