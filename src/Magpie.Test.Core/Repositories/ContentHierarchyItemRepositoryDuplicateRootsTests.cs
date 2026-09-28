using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class ContentHierarchyItemRepositoryDuplicateRootsTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private IContentHierarchyItemRepository _subject = null!;

    [OneTimeSetUp]
    public void CreateTemplate()
    {
        _template = new SqliteConnection("Data Source=:memory:");
        _template.Open();
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        using var context = new MagpieDbContext(options.UseSqlite(_template).Options);
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
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        _context = new MagpieDbContext(options.UseSqlite(connection, contextOwnsConnection: true).Options);
        _subject = new ContentHierarchyItemRepository(
            _context,
            Mock.Of<IAuditRecordRepository>(),
            new TransactionManager(_context, NullLogger<TransactionManager>.Instance),
            Mock.Of<IMetadataContainerRepository>(),
            Mock.Of<IContentTypeRegistry>(),
            Mock.Of<IEventBus>(),
            NullLogger<ContentHierarchyItemRepository>.Instance,
            TimeProvider.System,
            Mock.Of<IContentRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task FindDuplicateRootsAsync_ReportsRoots_WhenTheyShareAnIdentityAtTheSameTypeAndLevel()
    {
        // Arrange
        var tvmaze = AddItem("tv", "show", null, Contribution("tvmaze", 0, ("tvmaze", "1"), ("imdb", "tt1")));
        var wikidata = AddItem("tv", "show", null, Contribution("wikidata", 0, ("wikidata", "Q1"), ("imdb", "tt1")));
        AddItem("tv", "show", null, Contribution("first", 0, ("imdb", "tt2")));
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var shared = await _subject.FindDuplicateRootsAsync().ConfigureAwait(false);

        // Assert
        var identity = shared.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((identity.Source, identity.Value), Is.EqualTo(("imdb", "tt1")));
            Assert.That(identity.Items.Keys, Is.EquivalentTo([tvmaze.Id, wikidata.Id]));
        }
    }

    [Test]
    public async Task FindDuplicateRootsAsync_IgnoresCase_WhenProvidersCaseTheIdentityDifferently()
    {
        // Arrange
        var upper = AddItem("tv", "show", null, Contribution("first", 0, ("IMDB", "TT1")));
        var lower = AddItem("tv", "show", null, Contribution("second", 0, ("imdb", "tt1")));
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var shared = await _subject.FindDuplicateRootsAsync().ConfigureAwait(false);

        // Assert
        var identity = shared.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((identity.Source, identity.Value), Is.EqualTo(("imdb", "tt1")));
            Assert.That(identity.Items.Keys, Is.EquivalentTo([upper.Id, lower.Id]));
        }
    }

    [Test]
    public async Task FindDuplicateRootsAsync_IgnoresStaleValue_WhenAHigherContributionOverridesIt()
    {
        // Arrange
        AddItem(
            "tv",
            "show",
            null,
            Contribution("current", 0, ("imdb", "tt5")),
            Contribution("stale", 1, ("imdb", "tt1")));
        AddItem("tv", "show", null, Contribution("first", 0, ("imdb", "tt1")));
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var shared = await _subject.FindDuplicateRootsAsync().ConfigureAwait(false);

        // Assert
        Assert.That(shared, Is.Empty);
    }

    [Test]
    public async Task FindDuplicateRootsAsync_ReportsNothing_WhenOnlyTypeLevelChildOrSourceDiffers()
    {
        // Arrange
        var show = AddItem("tv", "show", null, Contribution("first", 0, ("imdb", "tt1")));
        AddItem("tv", "special", null, Contribution("first", 0, ("imdb", "tt1")));
        AddItem("ebook", "show", null, Contribution("first", 0, ("imdb", "tt1")));
        AddItem("tv", "show", show.Id, Contribution("first", 0, ("imdb", "tt1")));
        AddItem("tv", "show", null, Contribution("first", 0, ("tvdb", "tt1")));
        AddItem(
            "tv",
            "show",
            null,
            Contribution("first", 0, ("imdb", "tt9")),
            Contribution("second", 1, ("imdb", "tt9")));
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var shared = await _subject.FindDuplicateRootsAsync().ConfigureAwait(false);

        // Assert
        Assert.That(shared, Is.Empty);
    }

    private static MetadataContribution Contribution(
        string providerType,
        int ordinal,
        params (string Source, string Value)[] ids)
    {
        var contribution = new MetadataContribution { ProviderType = providerType, Ordinal = ordinal };
        foreach (var (source, value) in ids)
        {
            contribution.ExternalIds.Add(new ExternalId { Source = source, Value = value });
        }

        return contribution;
    }

    private ContentHierarchyItem AddItem(
        string contentType,
        string level,
        Guid? parentId,
        params MetadataContribution[] contributions)
    {
        var container = new MetadataContainer();
        foreach (var contribution in contributions)
        {
            container.Contributions.Add(contribution);
        }

        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            ContentType = contentType,
            Level = level,
            Slug = $"item-{Guid.NewGuid():N}",
            Name = "Item",
            CreatedAt = new DateTime(2026, 1, 1),
            MetadataContainer = container
        };
        _context.ContentHierarchyItems.Add(item);
        return item;
    }
}
