using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using NUnit.Framework;

namespace Magpie.Test.Core;

[TestFixture]
public sealed class MagpieSeedingExtensionsTests
{
    private MagpieDbContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true);
        options.UseMagpieSeeding([], []);
        _context = new MagpieDbContext(options.Options);
        _context.Database.EnsureCreated();
        _context.ChangeTracker.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task UseMagpieSeeding_KeepsListDeletedAndAddsNoTiers_WhenBuiltInListWasDeleted()
    {
        // Arrange
        var videoHdTierListId = new Guid("6f73ee55-eaa7-4a35-a3bf-f9bf075f4659");
        await _context.QualityTierListRanks
            .Where(rank => rank.QualityTierListId == videoHdTierListId)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
        await _context.QualityTierLists
            .Where(list => list.Id == videoHdTierListId)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        // Act
        await _context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                await _context.QualityTierLists.AnyAsync(list => list.Id == videoHdTierListId).ConfigureAwait(false),
                Is.False);
            Assert.That(await _context.QualityTiers.CountAsync().ConfigureAwait(false), Is.EqualTo(36));
        }
    }

    [Test]
    public async Task UseMagpieSeeding_KeepsListDeleted_WhenBuiltInListAndItsTiersWereDeleted()
    {
        // Arrange
        var videoHdTierListId = new Guid("6f73ee55-eaa7-4a35-a3bf-f9bf075f4659");
        var videoHdTierIds = await _context.QualityTierListRanks
            .Where(rank => rank.QualityTierListId == videoHdTierListId)
            .Select(rank => rank.QualityTierId)
            .ToListAsync()
            .ConfigureAwait(false);
        await _context.QualityTierListRanks
            .Where(rank => rank.QualityTierListId == videoHdTierListId)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
        await _context.QualityTiers
            .Where(tier => videoHdTierIds.Contains(tier.Id))
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);
        await _context.QualityTierLists
            .Where(list => list.Id == videoHdTierListId)
            .ExecuteDeleteAsync()
            .ConfigureAwait(false);

        // Act
        await _context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                await _context.QualityTierLists.AnyAsync(list => list.Id == videoHdTierListId).ConfigureAwait(false),
                Is.False);
            Assert.That(await _context.QualityTiers.CountAsync().ConfigureAwait(false), Is.EqualTo(29));
        }
    }

    [TestCase("Orphaned subject pruning schedule", "Subject maintenance schedule")]
    [TestCase("Nightly subject cleanup", "Nightly subject cleanup")]
    public async Task UseMagpieSeeding_MovesTheScheduleToSubjectMaintenance_WhenAPruneOrphanedSubjectsScheduleExists(
        string name,
        string expectedName)
    {
        // Arrange
        _context.ScheduleTriggers.Add(new ScheduleTrigger
        {
            JobType = "PruneOrphanedSubjects",
            Scope = JobScope.Administrative,
            Name = name,
            Schedule = "45 3 * * *",
            Enabled = false,
            CreatedAt = DateTime.UnixEpoch
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        await _context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        // Assert
        var trigger = await _context.ScheduleTriggers.AsNoTracking().SingleAsync().ConfigureAwait(false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(trigger.JobType, Is.EqualTo("SubjectMaintenance"));
            Assert.That(trigger.Name, Is.EqualTo(expectedName));
            Assert.That(trigger.Schedule, Is.EqualTo("45 3 * * *"));
            Assert.That(trigger.Enabled, Is.False);
        }
    }

    [Test]
    public async Task UseMagpieSeeding_DeletesOnlyTheDuplicateSubjectFindings_WhenTheRemovedCheckRaisedThem()
    {
        // Arrange
        _context.DiagnosticFindings.AddRange(
            new DiagnosticFinding
            {
                Id = new Guid("dddddddd-0000-0000-0000-000000000001"),
                DiagnosticCheckId = "core.content.duplicate-subject",
                Severity = DiagnosticFindingSeverity.Warning,
                Title = "Duplicate subjects",
                Detail = "1 provider identity belongs to more than one subject.",
                RepairId = "core.content.merge-duplicate-subjects",
                DetectedAt = DateTime.UnixEpoch
            },
            new DiagnosticFinding
            {
                Id = new Guid("dddddddd-0000-0000-0000-000000000002"),
                DiagnosticCheckId = "core.config.empty-quality-profile",
                Severity = DiagnosticFindingSeverity.Warning,
                Title = "Tier list Any has no tiers",
                Detail = "The tier list has no tiers.",
                DetectedAt = DateTime.UnixEpoch
            });
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _context.ChangeTracker.Clear();

        // Act
        await _context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        // Assert
        Assert.That(
            await _context.DiagnosticFindings.Select(finding => finding.Id).ToListAsync().ConfigureAwait(false),
            Is.EqualTo([new Guid("dddddddd-0000-0000-0000-000000000002")]));
    }

    [Test]
    public async Task UseMagpieSeeding_KeepsEveryListDeleted_WhenEveryTierListWasDeleted()
    {
        // Arrange
        await _context.QualityTierListRanks.ExecuteDeleteAsync().ConfigureAwait(false);
        await _context.QualityTierLists.ExecuteDeleteAsync().ConfigureAwait(false);

        // Act
        await _context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.QualityTierLists.CountAsync().ConfigureAwait(false), Is.EqualTo(0));
            Assert.That(await _context.QualityTiers.CountAsync().ConfigureAwait(false), Is.EqualTo(36));
        }
    }
}
