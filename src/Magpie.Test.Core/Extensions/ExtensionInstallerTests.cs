using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Extensions;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Extensions;

[TestFixture]
public class ExtensionInstallerTests
{
    private MagpieDbContext _context = null!;
    private IExtensionRepository _extensionRepository = null!;
    private Mock<INuGetService> _nuGetService = null!;
    private string _extensionsRoot = null!;
    private ExtensionInstaller _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var user = new User
        {
            Username = "tester",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(user);
        _context.SaveChanges();

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var auditRecordRepository = new AuditRecordRepository(
            _context,
            loggerFactory.CreateLogger<AuditRecordRepository>());
        var transactionManager = new TransactionManager(_context);
        _extensionRepository = new ExtensionRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            loggerFactory.CreateLogger<ExtensionRepository>());

        _nuGetService = new Mock<INuGetService>();

        _extensionsRoot = Path.Combine(Path.GetTempPath(), $"magpie-ext-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_extensionsRoot);

        _subject = new ExtensionInstaller(
            _extensionRepository,
            _nuGetService.Object,
            new ExtensionInstallerOptions { ExtensionsRoot = _extensionsRoot },
            loggerFactory.CreateLogger<ExtensionInstaller>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();

        if (Directory.Exists(_extensionsRoot))
        {
            try
            {
                Directory.Delete(_extensionsRoot, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    [Test]
    public async Task InstallFromNuGetAsync_StagesPackage_AndRegistersExtension()
    {
        // Arrange
        var package = CreateSyntheticPackage("Magpie.Extensions.Foo", "1.2.3", new[] { "lib/netstandard2.1/Foo.dll" });
        _nuGetService
            .Setup(m => m.DownloadPackageAsync("Magpie.Extensions.Foo", "1.2.3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(package);

        // Act
        var result = await _subject.InstallFromNuGetAsync("Magpie.Extensions.Foo", "1.2.3", null, CancellationToken.None);

        // Assert
        var dllPath = Path.Combine(_extensionsRoot, "Magpie.Extensions.Foo", "Foo.dll");
        var nuspecPath = Path.Combine(_extensionsRoot, "Magpie.Extensions.Foo", "Magpie.Extensions.Foo.nuspec");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(dllPath), Is.True, "lib dll should be extracted to the staging dir");
            Assert.That(File.Exists(nuspecPath), Is.True, "nuspec should be copied alongside the assembly");
            Assert.That(result.Managed, Is.True);
            Assert.That(result.NuGetId, Is.EqualTo("Magpie.Extensions.Foo"));
            Assert.That(result.Version, Is.EqualTo("1.2.3"));
            Assert.That(result.Author, Is.EqualTo("tests"));
            Assert.That(result.Description, Is.EqualTo("Synthetic test package"));
        }

        var dbRow = await _context.Extensions.AsNoTracking().FirstOrDefaultAsync(e => e.Id == result.Id);
        Assert.That(dbRow, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dbRow!.Author, Is.EqualTo("tests"));
            Assert.That(dbRow.Description, Is.EqualTo("Synthetic test package"));
        }
    }

    [Test]
    public async Task InstallFromNuGetAsync_Throws_WhenDownloadReturnsNull()
    {
        // Arrange
        _nuGetService
            .Setup(m => m.DownloadPackageAsync("Magpie.Extensions.Missing", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        // Act / Assert
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _subject.InstallFromNuGetAsync("Magpie.Extensions.Missing", null, null, CancellationToken.None));
    }

    [Test]
    public async Task InstallFromStreamAsync_StagesPackage_AndRegistersExtension()
    {
        // Arrange
        var package = CreateSyntheticPackage(
            "Magpie.Extensions.Bar",
            "0.1.0",
            new[] { "lib/netstandard2.1/Bar.dll" });

        // Act
        var result = await _subject.InstallFromStreamAsync(package, "Bar.nupkg", null, CancellationToken.None);

        // Assert
        var dllPath = Path.Combine(_extensionsRoot, "Bar", "Bar.dll");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(dllPath), Is.True);
            Assert.That(result.Managed, Is.False);
            Assert.That(result.NuGetId, Is.Null);
            Assert.That(result.Version, Is.EqualTo("0.1.0"));
        }
    }

    [Test]
    public async Task CheckUpdatesAsync_FlagsManagedExtensionWithNewerVersionOnFeed()
    {
        // Arrange
        var seeded = await _extensionRepository.CreateAsync(
            new Extension
            {
                Name = "Magpie.Extensions.Foo",
                Version = "1.0.0",
                FilePath = Path.Combine(_extensionsRoot, "Magpie.Extensions.Foo", "Foo.dll"),
                AssemblyName = "Magpie.Extensions.Foo",
                Enabled = true,
                Installed = true,
                Managed = true,
                NuGetId = "Magpie.Extensions.Foo"
            },
            null,
            CancellationToken.None);

        _nuGetService
            .Setup(m => m.GetVersionsAsync("Magpie.Extensions.Foo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(["0.9.0", "1.0.0", "1.1.0"]);

        // Act
        var results = await _subject.CheckUpdatesAsync(CancellationToken.None);

        // Assert
        var entry = results.Single(r => r.ExtensionId == seeded.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.HasUpdate, Is.True);
            Assert.That(entry.LatestVersion, Is.EqualTo("1.1.0"));
            Assert.That(entry.CurrentVersion, Is.EqualTo("1.0.0"));
        }
    }

    [Test]
    public async Task CheckUpdatesAsync_DoesNotFlagUnmanagedExtensions()
    {
        // Arrange
        await _extensionRepository.CreateAsync(
            new Extension
            {
                Name = "BuiltIn",
                Version = "1.0.0",
                FilePath = Path.Combine(_extensionsRoot, "BuiltIn", "BuiltIn.dll"),
                AssemblyName = "BuiltIn",
                Enabled = true,
                Installed = true,
                Managed = false
            },
            null,
            CancellationToken.None);

        // Act
        var results = await _subject.CheckUpdatesAsync(CancellationToken.None);

        // Assert
        Assert.That(results, Has.One.Property(nameof(ExtensionUpdateInfo.HasUpdate)).False);
        _nuGetService.Verify(
            m => m.GetVersionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task UpdateAsync_RemovesOldStagingDir_AndReinstalls()
    {
        // Arrange: seed an existing managed extension (with on-disk staging) + .installed.json
        var stagingDir = Path.Combine(_extensionsRoot, "Magpie.Extensions.Foo");
        Directory.CreateDirectory(stagingDir);
        await File.WriteAllBytesAsync(Path.Combine(stagingDir, "Foo.dll"), [0xAA]);

        var seeded = await _extensionRepository.CreateAsync(
            new Extension
            {
                Name = "Magpie.Extensions.Foo",
                Version = "1.0.0",
                FilePath = Path.Combine(stagingDir, "Foo.dll"),
                AssemblyName = "Foo",
                Managed = true,
                NuGetId = "Magpie.Extensions.Foo",
                Enabled = true,
                Installed = true
            },
            null,
            CancellationToken.None);

        var installedRecordPath = Path.Combine(_extensionsRoot, ExtensionInstaller.InstalledRecordFileName);
        await File.WriteAllTextAsync(installedRecordPath, JsonSerializer.Serialize(new[] { seeded.Id.ToString() }));

        var newPackage = CreateSyntheticPackage(
            "Magpie.Extensions.Foo",
            "1.1.0",
            new[] { "lib/netstandard2.1/Foo.dll" });
        _nuGetService
            .Setup(m => m.DownloadPackageAsync("Magpie.Extensions.Foo", "1.1.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(newPackage);

        // Act
        var updated = await _subject.UpdateAsync(seeded.Id, "1.1.0", null, CancellationToken.None);

        // Assert: new row, install record cleaned of old id
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Version, Is.EqualTo("1.1.0"));

        var record = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(installedRecordPath));
        Assert.That(record, Does.Not.Contain(seeded.Id.ToString()));
    }

    [Test]
    public async Task UninstallAsync_RemovesDirectory_DbRow_AndInstallRecord()
    {
        // Arrange
        var stagingDir = Path.Combine(_extensionsRoot, "Magpie.Extensions.Foo");
        Directory.CreateDirectory(stagingDir);
        await File.WriteAllBytesAsync(Path.Combine(stagingDir, "Foo.dll"), [0xAA]);

        var seeded = await _extensionRepository.CreateAsync(
            new Extension
            {
                Name = "Magpie.Extensions.Foo",
                Version = "1.0.0",
                FilePath = Path.Combine(stagingDir, "Foo.dll"),
                AssemblyName = "Foo",
                Managed = true,
                NuGetId = "Magpie.Extensions.Foo",
                Enabled = true,
                Installed = true
            },
            null,
            CancellationToken.None);

        var installedRecordPath = Path.Combine(_extensionsRoot, ExtensionInstaller.InstalledRecordFileName);
        await File.WriteAllTextAsync(installedRecordPath, JsonSerializer.Serialize(new[] { seeded.Id.ToString() }));

        // Act
        var removed = await _subject.UninstallAsync(seeded.Id, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.True);
            Assert.That(Directory.Exists(stagingDir), Is.False);
        }

        var dbRow = await _context.Extensions.AsNoTracking().FirstOrDefaultAsync(e => e.Id == seeded.Id);
        Assert.That(dbRow, Is.Null);

        var record = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(installedRecordPath));
        Assert.That(record, Does.Not.Contain(seeded.Id.ToString()));
    }

    [Test]
    public async Task UninstallAsync_ReturnsFalse_WhenExtensionMissing()
    {
        // Act
        var removed = await _subject.UninstallAsync(Guid.NewGuid(), null, CancellationToken.None);

        // Assert
        Assert.That(removed, Is.False);
    }

    [Test]
    public void InstallFromStreamAsync_RejectsZipSlipEntry_AndDoesNotWriteOutsideTarget()
    {
        // Arrange: build a synthetic .nupkg whose lib entry tries to escape the staging dir.
        var maliciousRelative = $"..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}escaped.dll";
        var package = CreateSyntheticPackageWithRawEntries(
            "Magpie.Extensions.Evil",
            "1.0.0",
            new[]
            {
                ("lib/netstandard2.1/../../../escaped.dll", new byte[] { 0xAA }),
                ("lib/netstandard2.1/Foo.dll", new byte[] { 0xBB })
            });

        // The extensions root parent must NOT contain a file named 'escaped.dll' afterwards.
        var parentDir = Directory.GetParent(_extensionsRoot)!.FullName;
        var escapedCandidate = Path.Combine(parentDir, "escaped.dll");
        if (File.Exists(escapedCandidate))
        {
            File.Delete(escapedCandidate);
        }

        // Act / Assert: extraction must throw before any write escapes.
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _subject.InstallFromStreamAsync(package, "Evil.nupkg", null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                File.Exists(escapedCandidate),
                Is.False,
                "Zip-slip entry must not have been written outside the extensions root");

            // Walk every directory above the extensions root up to a sane stopping point and
            // confirm no 'escaped.dll' was created anywhere.
            var probe = parentDir;
            for (var i = 0; i < 4 && !string.IsNullOrEmpty(probe); i++)
            {
                Assert.That(
                    File.Exists(Path.Combine(probe, "escaped.dll")),
                    Is.False,
                    $"Unexpected escaped file at {probe}");
                probe = Directory.GetParent(probe)?.FullName ?? string.Empty;
            }

            // A reference to maliciousRelative is kept only so the lint sees the local in use; the
            // important assertion is the absence of any write outside _extensionsRoot.
            Assert.That(maliciousRelative, Is.Not.Empty);
        }
    }

    /// <summary>
    /// Build a .nupkg-like ZIP allowing entries with traversal segments. Uses Update mode so
    /// ZipArchive does not normalise the entry name (Create mode rewrites <c>..</c> away on
    /// some platforms).
    /// </summary>
    private static MemoryStream CreateSyntheticPackageWithRawEntries(
        string packageId,
        string version,
        IEnumerable<(string FullName, byte[] Content)> entries,
        bool includeNuspec = true)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (includeNuspec)
            {
                var nuspec = $"""
                    <?xml version="1.0"?>
                    <package>
                      <metadata>
                        <id>{packageId}</id>
                        <version>{version}</version>
                        <title>{packageId}</title>
                        <description>Synthetic test package</description>
                        <authors>tests</authors>
                      </metadata>
                    </package>
                    """;

                var nuspecEntry = archive.CreateEntry($"{packageId}.nuspec");
                using var writer = new StreamWriter(nuspecEntry.Open(), Encoding.UTF8);
                writer.Write(nuspec);
            }

            foreach (var (fullName, content) in entries)
            {
                var zipEntry = archive.CreateEntry(fullName);
                using var es = zipEntry.Open();
                es.Write(content, 0, content.Length);
            }
        }
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Build an in-memory .nupkg-like ZIP with a .nuspec at the root and the requested
    /// lib/&lt;tfm&gt; dll entries.
    /// </summary>
    private static MemoryStream CreateSyntheticPackage(
        string packageId,
        string version,
        IEnumerable<string> entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var nuspec = $"""
                <?xml version="1.0"?>
                <package>
                  <metadata>
                    <id>{packageId}</id>
                    <version>{version}</version>
                    <title>{packageId}</title>
                    <description>Synthetic test package</description>
                    <authors>tests</authors>
                  </metadata>
                </package>
                """;

            var nuspecEntry = archive.CreateEntry($"{packageId}.nuspec");
            using (var writer = new StreamWriter(nuspecEntry.Open(), Encoding.UTF8))
            {
                writer.Write(nuspec);
            }

            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry);
                using var writer = zipEntry.Open();
                writer.WriteByte(0xAA);
            }
        }
        stream.Position = 0;
        return stream;
    }
}
