using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Repositories;
using Magpie.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class ExtensionActivationGenerationRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IExtensionActivationGenerationRepository _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var loggerFactory = NullLoggerFactory.Instance;
        _subject = new ExtensionActivationGenerationRepository(
            _context,
            loggerFactory.CreateLogger<ExtensionActivationGenerationRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task GetCurrentAsync_ReturnsZero_ForFreshDatabase()
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
}
