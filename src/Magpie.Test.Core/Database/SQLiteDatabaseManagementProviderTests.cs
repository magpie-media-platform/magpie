using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Configuration;
using Magpie.Abstractions.Database;
using Magpie.Abstractions.System;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Database;

[TestFixture]
public class SQLiteDatabaseManagementProviderTests
{
    private MagpieDbContext _context = null!;
    private IDatabaseManagementProvider _subject = null!;
    private string _testBackupDirectory = null!;
    private string _testDatabasePath = null!;
    private Mock<IMagpieSystem> _magpieSystem = null!;
    private Mock<ISettingsProvider> _settingsProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _testBackupDirectory = Path.Combine(Path.GetTempPath(), "magpie-test-backups-" + Guid.NewGuid());
        _testDatabasePath = Path.Combine(Path.GetTempPath(), "magpie-test-" + Guid.NewGuid() + ".db");

        Directory.CreateDirectory(_testBackupDirectory);

        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite($"Data Source={_testDatabasePath}")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _magpieSystem = new Mock<IMagpieSystem>();
        _magpieSystem.Setup(m => m.SystemRoot).Returns(Path.GetTempPath());

        _settingsProvider = new Mock<ISettingsProvider>();
        var settingsGroup = new Mock<ISettingsGroup>();
        settingsGroup.Setup(m => m.Settings).Returns(new Dictionary<string, string>
        {
            ["BackupDirectory"] = _testBackupDirectory
        });
        _settingsProvider
            .Setup(m => m.GetSettingsGroupAsync(It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(settingsGroup.Object);

        _subject = new SQLiteDatabaseManagementProvider(
            _context,
            _magpieSystem.Object,
            _settingsProvider.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();

        // SQLite needs a moment to release file locks
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();

        if (File.Exists(_testDatabasePath))
        {
            try
            {
                File.Delete(_testDatabasePath);
            }
            catch
            {
                // If we can't delete, it will be cleaned up by the OS eventually
            }
        }

        if (Directory.Exists(_testBackupDirectory))
        {
            try
            {
                Directory.Delete(_testBackupDirectory, recursive: true);
            }
            catch
            {
                // If we can't delete, it will be cleaned up by the OS eventually
            }
        }
    }

    [Test]
    public async Task BackupAsync_CreatesBackupFile()
    {
        // Act
        var backupId = await _subject.BackupAsync();

        // Assert
        var backupFiles = Directory.GetFiles(_testBackupDirectory, $"{backupId}*.db");
        Assert.That(backupFiles, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task BackupAsync_WithName_EncodesNameInFileName()
    {
        // Arrange
        var backupName = "my-backup";

        // Act
        var backupId = await _subject.BackupAsync(backupName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(backupId, Is.Not.EqualTo(backupName), "Id should be timestamp-based, not user name");
            var expectedPath = Path.Combine(_testBackupDirectory, $"{backupId}__{backupName}.db");
            Assert.That(File.Exists(expectedPath), Is.True);
        }
    }

    [Test]
    public async Task BackupAsync_WithoutName_UsesScheduledDefaultName()
    {
        // Act
        var backupId = await _subject.BackupAsync();

        // Assert
        var backups = (await _subject.ListBackupsAsync()).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(backups, Has.Count.EqualTo(1));
            Assert.That(backups[0].Name, Does.StartWith("Scheduled "));
            Assert.That(backups[0].Id, Is.EqualTo(backupId));
        }
    }

    [Test]
    public async Task BackupAsync_SanitizesNameForFileSystem()
    {
        // Arrange
        var unsafeName = "my/backup\\with:bad*chars?";

        // Act
        var backupId = await _subject.BackupAsync(unsafeName);

        // Assert
        var backups = (await _subject.ListBackupsAsync()).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(backups, Has.Count.EqualTo(1));
            Assert.That(backups[0].Id, Is.EqualTo(backupId));
            Assert.That(backups[0].Name, Is.EqualTo("mybackupwithbadchars"));
        }
    }

    [Test]
    public async Task ListBackupsAsync_ReturnsEmptyList_WhenNoBackups()
    {
        // Act
        var backups = await _subject.ListBackupsAsync();

        // Assert
        Assert.That(backups, Is.Empty);
    }

    [Test]
    public async Task ListBackupsAsync_ReturnsBackupsWithSizeAndName()
    {
        // Arrange
        var backupId = await _subject.BackupAsync("test backup");

        // Act
        var backups = await _subject.ListBackupsAsync();

        // Assert
        var backupList = backups.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(backupList, Has.Count.EqualTo(1));
            var backup = backupList.First();
            Assert.That(backup.Id, Is.EqualTo(backupId));
            Assert.That(backup.Name, Is.EqualTo("test backup"));
            Assert.That(backup.CreatedAt, Is.Not.EqualTo(default(DateTime)));
            Assert.That(backup.SizeBytes, Is.GreaterThan(0));
        }
    }

    [Test]
    public async Task ListBackupsAsync_ParsesLegacyFileNamesWithoutNameSuffix()
    {
        // Arrange: file written by an older version (no name suffix).
        var legacyPath = Path.Combine(_testBackupDirectory, "legacy-id.db");
        await File.WriteAllTextAsync(legacyPath, "legacy backup data");

        // Act
        var backups = (await _subject.ListBackupsAsync()).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(backups, Has.Count.EqualTo(1));
            Assert.That(backups[0].Id, Is.EqualTo("legacy-id"));
            Assert.That(backups[0].Name, Is.EqualTo("legacy-id"));
            Assert.That(backups[0].SizeBytes, Is.EqualTo("legacy backup data".Length));
        }
    }

    [Test]
    public async Task DeleteBackupAsync_DeletesBackup()
    {
        // Arrange
        var backupId = await _subject.BackupAsync("backup-to-delete");
        var matchingFiles = Directory.GetFiles(_testBackupDirectory, $"{backupId}*.db");
        Assert.That(matchingFiles, Has.Length.EqualTo(1));

        // SQLite needs a moment to release file locks after backup
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await Task.Delay(50);

        // Act
        var result = await _subject.DeleteBackupAsync(backupId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(Directory.GetFiles(_testBackupDirectory, $"{backupId}*.db"), Is.Empty);
        }
    }

    [Test]
    public async Task DeleteBackupAsync_ReturnsFalse_WhenBackupDoesNotExist()
    {
        // Arrange: well-formed timestamp id that doesn't exist on disk.
        var backupId = "99990101_000000";

        // Act
        var result = await _subject.DeleteBackupAsync(backupId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task RestoreAsync_RestoresBackup()
    {
        // Arrange
        var backupId = await _subject.BackupAsync("backup-to-restore");

        // Act
        var result = await _subject.RestoreAsync(backupId);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task RestoreAsync_ReturnsFalse_WhenBackupDoesNotExist()
    {
        // Arrange: well-formed timestamp id that doesn't exist on disk.
        var backupId = "99990101_000000";

        // Act
        var result = await _subject.RestoreAsync(backupId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task GetBackupFilePathAsync_ReturnsFilePath()
    {
        // Arrange
        var backupId = await _subject.BackupAsync("backup-to-download");
        var expectedPath = Path.Combine(_testBackupDirectory, $"{backupId}__backup-to-download.db");

        // Act
        var filePath = await _subject.GetBackupFilePathAsync(backupId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filePath, Is.EqualTo(expectedPath));
            Assert.That(File.Exists(filePath), Is.True);
        }
    }

    [Test]
    public async Task GetBackupFilePathAsync_ReturnsNull_WhenBackupDoesNotExist()
    {
        // Arrange: well-formed timestamp id that doesn't exist on disk.
        var backupId = "99990101_000000";

        // Act
        var result = await _subject.GetBackupFilePathAsync(backupId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task OptimizeAsync_CompletesWithoutError()
    {
        // Act & Assert - Should complete without throwing
        await _subject.OptimizeAsync();
        Assert.Pass();
    }

    // --- Path-traversal hardening for backup id input ---------------------------------

    private static readonly string[] MaliciousBackupIds =
    [
        "../../etc/passwd",
        "foo/../bar",
        "..\\bad",
        "..\\..\\Windows\\System32\\config",
        "/absolute/path",
        "C:\\absolute\\path",
        "20240115_103000/../escape",
        "20240115_103000__name", // name suffix is not part of the id
        "nonexistent-backup",
        "",
        "   ",
        ".",
        ".."
    ];

    [TestCaseSource(nameof(MaliciousBackupIds))]
    public void RestoreAsync_RejectsMaliciousBackupId(string maliciousId)
    {
        Assert.ThrowsAsync<ArgumentException>(
            () => _subject.RestoreAsync(maliciousId),
            $"Expected ArgumentException for backup id '{maliciousId}'");
    }

    [TestCaseSource(nameof(MaliciousBackupIds))]
    public void DeleteBackupAsync_RejectsMaliciousBackupId(string maliciousId)
    {
        Assert.ThrowsAsync<ArgumentException>(
            () => _subject.DeleteBackupAsync(maliciousId),
            $"Expected ArgumentException for backup id '{maliciousId}'");
    }

    [TestCaseSource(nameof(MaliciousBackupIds))]
    public void GetBackupFilePathAsync_RejectsMaliciousBackupId(string maliciousId)
    {
        Assert.ThrowsAsync<ArgumentException>(
            () => _subject.GetBackupFilePathAsync(maliciousId),
            $"Expected ArgumentException for backup id '{maliciousId}'");
    }

    [Test]
    public async Task GetBackupFilePathAsync_AcceptsTimestampIdProducedByBackupAsync()
    {
        // Arrange: an id produced by BackupAsync conforms to the timestamp format and must
        // continue to round-trip through every entry point.
        var backupId = await _subject.BackupAsync("legit");

        // Act
        var resolved = await _subject.GetBackupFilePathAsync(backupId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved, Is.Not.Null);
            Assert.That(File.Exists(resolved!), Is.True);
        }
    }

    [Test]
    public void GetBackupFilePathAsync_AcceptsTimestampIdWithMillisecondSuffix()
    {
        // The disambiguator path in BackupAsync produces yyyyMMdd_HHmmss_NNN; ensure the
        // validator accepts that exact shape even when no file exists.
        Assert.DoesNotThrowAsync(async () =>
        {
            var resolved = await _subject.GetBackupFilePathAsync("20240115_103000_123");
            Assert.That(resolved, Is.Null);
        });
    }
}
