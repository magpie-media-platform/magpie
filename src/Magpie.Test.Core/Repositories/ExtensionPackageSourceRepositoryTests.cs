using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Abstractions.Security;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Patch;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ExtensionPackageSourceRepositoryTests
{
    private Mock<IEventBus> _updateEvents = null!;

    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IExtensionPackageSourceRepository _subject = null!;
    private IDataProtectionService _dataProtectionService = null!;
    private ILoggerFactory _loggerFactory = null!;
    private Guid _userId;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        var options = new DbContextOptionsBuilder<MagpieDbContext>().UseSqlite(_template);
        options.UseMagpieSeeding([], []);
        using var context = new MagpieDbContext(options.Options);
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
        var options = new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true);
        options.UseMagpieSeeding([], []);
        _context = new MagpieDbContext(options.Options);

        var user = new User
        {
            Username = "TestUser",
            ProviderType = "local",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        _context.SaveChanges();

        _userId = user.Id;

        _loggerFactory = NullLoggerFactory.Instance;
        var dataProtectionService = new Mock<IDataProtectionService>();
        dataProtectionService
            .Setup(x => x.Protect(It.IsAny<string>()))
            .Returns((string value) => $"protected:{value}");
        dataProtectionService
            .Setup(x => x.Unprotect(It.IsAny<string>()))
            .Returns((string value) => value["protected:".Length..]);
        _dataProtectionService = dataProtectionService.Object;

        var auditRecordRepository = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            _loggerFactory.CreateLogger<AuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IJobExecutionScopeFactory>(),
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var transactionManager = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);

        _updateEvents = new Mock<IEventBus>();
        _subject = new ExtensionPackageSourceRepository(
            _context,
            auditRecordRepository,
            transactionManager,
            _updateEvents.Object,
            _loggerFactory.CreateLogger<ExtensionPackageSourceRepository>(),
            _dataProtectionService,
            timeProvider: new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task ListAllAsync_ReturnsSeededNugetSource_WhenTheDatabaseIsNew()
    {
        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0], Has.Property(nameof(ExtensionPackageSource.Name)).EqualTo("nuget.org"));
            Assert.That(
                result[0],
                Has.Property(nameof(ExtensionPackageSource.SourceUrl))
                    .EqualTo("https://api.nuget.org/v3/index.json"));
            Assert.That(result[0], Has.Property(nameof(ExtensionPackageSource.Enabled)).True);
            Assert.That(
                result[0],
                Has.Property(nameof(ExtensionPackageSource.SearchFilterKind))
                    .EqualTo(ExtensionPackageSourceSearchFilterKind.Tag));
            Assert.That(result[0], Has.Property(nameof(ExtensionPackageSource.SearchFilterValue)).EqualTo("magpie"));
            Assert.That(
                result[0],
                Has.Property(nameof(ExtensionPackageSource.CredentialKind))
                    .EqualTo(ExtensionPackageSourceCredentialKind.None));
        }
    }

    [Test]
    public async Task CreateAsync_StoresCiphertextAndReturnsPlaintext_WhenSecretCredentialsAreProvided()
    {
        // Arrange
        var model = new ExtensionPackageSource
        {
            Name = "internal",
            SourceUrl = "https://packages.example/v3/index.json",
            Enabled = true,
            SearchFilterKind = ExtensionPackageSourceSearchFilterKind.Prefix,
            SearchFilterValue = "Magpie.Extensions.",
            CredentialKind = ExtensionPackageSourceCredentialKind.Basic,
            Username = "feed-user",
            EncryptedSecret = "plain-secret"
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.ExtensionPackageSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == result.Id).ConfigureAwait(false);
        var storedPlaintext = _dataProtectionService.Unprotect(entity!.EncryptedSecret!);
        var auditRecords = await _context.AuditRecords.ToListAsync().ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.EncryptedSecret, Is.EqualTo("plain-secret"));
            Assert.That(entity, Has.Property(nameof(ExtensionPackageSource.Name)).EqualTo("internal"));
            Assert.That(
                entity,
                Has.Property(nameof(ExtensionPackageSource.SearchFilterKind))
                    .EqualTo(ExtensionPackageSourceSearchFilterKind.Prefix));
            Assert.That(
                entity,
                Has.Property(nameof(ExtensionPackageSource.EncryptedSecret))
                    .Not.EqualTo("plain-secret"));
            Assert.That(storedPlaintext, Is.EqualTo("plain-secret"));
            Assert.That(entity, Has.Property(nameof(ExtensionPackageSource.CreatedByUserId)).EqualTo(_userId));
            Assert.That(
                auditRecords,
                Has.One
                    .With.Property(nameof(AuditRecord.Operation)).EqualTo("Create")
                    .And.Property(nameof(AuditRecord.EntityType)).EqualTo(nameof(ExtensionPackageSource)));
        }
    }

    [Test]
    public async Task UpdateAsync_StoresCiphertextAndPreservesPlaintextRead_WhenModelHasPlaintextSecret()
    {
        // Arrange
        var source = new ExtensionPackageSource
        {
            Name = "update-secret",
            SourceUrl = "https://update-secret.example/v3/index.json",
            Enabled = true,
            SearchFilterKind = ExtensionPackageSourceSearchFilterKind.Tag,
            SearchFilterValue = "magpie",
            CredentialKind = ExtensionPackageSourceCredentialKind.Basic,
            Username = "feed-user",
            EncryptedSecret = "plain-secret"
        };
        var created = await _subject.CreateAsync(source, _userId).ConfigureAwait(false);
        var plaintextSource = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);
        var update = plaintextSource! with { Enabled = false };

        // Act
        var result = await _subject.UpdateAsync(update, _userId).ConfigureAwait(false);

        // Assert
        var storedSecret = await _context.ExtensionPackageSources
            .AsNoTracking()
            .Where(s => s.Id == created.Id)
            .Select(s => s.EncryptedSecret)
            .FirstAsync()
            .ConfigureAwait(false);
        var roundTripped = await _subject.GetByIdAsync(created.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(storedSecret, Is.Not.Null);
            Assert.That(storedSecret, Is.Not.EqualTo("plain-secret"));
            Assert.That(roundTripped!.EncryptedSecret, Is.EqualTo("plain-secret"));
            Assert.That(roundTripped.Enabled, Is.False);
        }

        _updateEvents.Verify(bus => bus.PublishAsync(
            It.Is<MagpieEvent>(e =>
                e.Name == "extensionPackageSource.updated" &&
                e.Context["extensionPackageSource.changedFields"] == "enabled"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task PatchAsync_StoresCiphertext_WhenSecretIsPatched()
    {
        // Arrange
        var source = new ExtensionPackageSource
        {
            Name = "patch-secret",
            SourceUrl = "https://patch-secret.example/v3/index.json",
            Enabled = true,
            SearchFilterKind = ExtensionPackageSourceSearchFilterKind.Tag,
            SearchFilterValue = "magpie",
            CredentialKind = ExtensionPackageSourceCredentialKind.Basic,
            Username = "feed-user",
            EncryptedSecret = "old-secret",
            CreatedAt = DateTime.UtcNow
        };
        await _subject.CreateAsync(source, _userId).ConfigureAwait(false);

        var patch = new ExtensionPackageSourcePatch
        {
            Id = source.Id,
            EncryptedSecret = new ScalarPatch<string?> { Value = "new-secret" }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var storedSecret = await _context.ExtensionPackageSources
            .AsNoTracking()
            .Where(s => s.Id == source.Id)
            .Select(s => s.EncryptedSecret)
            .FirstAsync()
            .ConfigureAwait(false);
        var roundTripped = await _subject.GetByIdAsync(source.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(storedSecret, Is.Not.Null);
            Assert.That(storedSecret, Is.Not.EqualTo("new-secret"));
            Assert.That(roundTripped!.EncryptedSecret, Is.EqualTo("new-secret"));
        }
    }

    [Test]
    public async Task CreateAsync_RetainsUsernameForSecretReferenceCredentials_WhenSecretReferenceIsProvided()
    {
        // Arrange
        var model = new ExtensionPackageSource
        {
            Name = "internal",
            SourceUrl = "https://packages.example/v3/index.json",
            Enabled = true,
            SearchFilterKind = ExtensionPackageSourceSearchFilterKind.Tag,
            SearchFilterValue = "magpie",
            CredentialKind = ExtensionPackageSourceCredentialKind.SecretReference,
            Username = "feed-user",
            EncryptedSecret = "ciphertext",
            SecretReference = "MAGPIE_TEST_NUGET_SECRET"
        };

        // Act
        var result = await _subject.CreateAsync(model, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.ExtensionPackageSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == result.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity, Has.Property(nameof(ExtensionPackageSource.Username)).EqualTo("feed-user"));
            Assert.That(
                entity,
                Has.Property(nameof(ExtensionPackageSource.SecretReference))
                    .EqualTo("MAGPIE_TEST_NUGET_SECRET"));
            Assert.That(entity, Has.Property(nameof(ExtensionPackageSource.EncryptedSecret)).Null);
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyEnabledSources_WhenEnabledFilterIsSet()
    {
        // Arrange
        _context.ExtensionPackageSources.Add(new()
        {
            Name = "disabled",
            SourceUrl = "https://disabled.example/v3/index.json",
            Enabled = false,
            SearchFilterKind = ExtensionPackageSourceSearchFilterKind.None,
            CredentialKind = ExtensionPackageSourceCredentialKind.None,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new ExtensionPackageSourceQuery
        {
            Enabled = true
        }, new() { PageNumber = 1, PageSize = 100 }, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Has.Exactly(1).Items);
            Assert.That(result.Items.Single().Name, Is.EqualTo("nuget.org"));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesFieldsAndClearsSecrets_WhenCredentialTypeBecomesNone()
    {
        // Arrange
        var source = new ExtensionPackageSource
        {
            Name = "patch-source",
            SourceUrl = "https://patch.example/v3/index.json",
            Enabled = true,
            SearchFilterKind = ExtensionPackageSourceSearchFilterKind.Tag,
            SearchFilterValue = "magpie",
            CredentialKind = ExtensionPackageSourceCredentialKind.Basic,
            Username = "feed-user",
            EncryptedSecret = "ciphertext",
            CreatedAt = DateTime.UtcNow
        };
        _context.ExtensionPackageSources.Add(source);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var patch = new ExtensionPackageSourcePatch
        {
            Id = source.Id,
            Enabled = new ScalarPatch<bool> { Value = false },
            SearchFilterKind = new ScalarPatch<ExtensionPackageSourceSearchFilterKind>
            {
                Value = ExtensionPackageSourceSearchFilterKind.None
            },
            CredentialKind = new ScalarPatch<ExtensionPackageSourceCredentialKind>
            {
                Value = ExtensionPackageSourceCredentialKind.None
            }
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId).ConfigureAwait(false);

        // Assert
        var entity = await _context.ExtensionPackageSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == source.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(entity, Has.Property(nameof(ExtensionPackageSource.Enabled)).False);
            Assert.That(
                entity,
                Has.Property(nameof(ExtensionPackageSource.SearchFilterKind))
                    .EqualTo(ExtensionPackageSourceSearchFilterKind.None));
            Assert.That(entity, Has.Property(nameof(ExtensionPackageSource.SearchFilterValue)).Null);
            Assert.That(entity, Has.Property(nameof(ExtensionPackageSource.Username)).Null);
            Assert.That(entity, Has.Property(nameof(ExtensionPackageSource.EncryptedSecret)).Null);
            Assert.That(entity, Has.Property(nameof(ExtensionPackageSource.ModifiedByUserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesPackageSource_WhenSourceExists()
    {
        // Arrange
        var source = new ExtensionPackageSource
        {
            Name = "delete-source",
            SourceUrl = "https://delete.example/v3/index.json",
            Enabled = true,
            SearchFilterKind = ExtensionPackageSourceSearchFilterKind.Tag,
            SearchFilterValue = "magpie",
            CredentialKind = ExtensionPackageSourceCredentialKind.None,
            CreatedAt = DateTime.UtcNow
        };
        _context.ExtensionPackageSources.Add(source);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.DeleteAsync(source.Id, _userId).ConfigureAwait(false);

        // Assert
        var exists = await _context.ExtensionPackageSources.AnyAsync(s => s.Id == source.Id).ConfigureAwait(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(exists, Is.False);
        }
    }
}
