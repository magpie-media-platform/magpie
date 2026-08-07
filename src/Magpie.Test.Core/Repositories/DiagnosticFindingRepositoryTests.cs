using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class DiagnosticFindingRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IDiagnosticFindingRepository _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _subject = new DiagnosticFindingRepository(
            _context,
            NullLogger<DiagnosticFindingRepository>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task QueryAsync_ReturnsFindings_WhenDiagnosticCheckIdMatches()
    {
        // Arrange
        await _subject.CreateAsync(
            new DiagnosticFinding
            {
                Id = Guid.NewGuid(),
                DiagnosticCheckId = "test.a",
                Severity = DiagnosticFindingSeverity.Info,
                Title = "first finding",
                Detail = "first detail",
                DetectedAt = DateTime.UtcNow
            },
            null).ConfigureAwait(false);
        await _subject.CreateAsync(
            new DiagnosticFinding
            {
                Id = Guid.NewGuid(),
                DiagnosticCheckId = "test.b",
                Severity = DiagnosticFindingSeverity.Info,
                Title = "second finding",
                Detail = "second detail",
                DetectedAt = DateTime.UtcNow
            },
            null).ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new DiagnosticFindingQuery { DiagnosticCheckId = "test.a" },
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Single().DiagnosticCheckId, Is.EqualTo("test.a"));
    }

    [Test]
    public async Task DeleteAsync_RemovesFinding_WhenFindingExists()
    {
        // Arrange
        var finding = await _subject.CreateAsync(
            new DiagnosticFinding
            {
                Id = Guid.NewGuid(),
                DiagnosticCheckId = "test.a",
                Severity = DiagnosticFindingSeverity.Info,
                Title = "test finding",
                Detail = "test detail",
                DetectedAt = DateTime.UtcNow
            },
            null).ConfigureAwait(false);

        // Act
        var deleted = await _subject.DeleteAsync(finding.Id, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted, Is.True);
            Assert.That(await _subject.GetByIdAsync(finding.Id).ConfigureAwait(false), Is.Null);
        }
    }
}
