using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class TagRepositoryTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private ITagRepository _subject = null!;

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

        var loggerFactory = NullLoggerFactory.Instance;

        _subject = new TagRepository(_context, loggerFactory.CreateLogger<TagRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllTags_WhenTagsExist()
    {
        // Arrange
        var tag1 = new Tag { Name = "tag1" };
        var tag2 = new Tag { Name = "tag2" };
        var tag3 = new Tag { Name = "tag3" };
        _context.Tags.AddRange(tag1, tag2, tag3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(3).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoTags()
    {
        // Act
        var result = await _subject.ListAllAsync(default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingTags_WhenPrefixMatches()
    {
        // Arrange
        var tag1 = new Tag { Name = "media:type-a" };
        var tag2 = new Tag { Name = "media:type-c" };
        var tag3 = new Tag { Name = "genre:action" };
        _context.Tags.AddRange(tag1, tag2, tag3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new TagQuery { Prefix = "media:" },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Has.Exactly(2).Items);
            Assert.That(result.Items, Has.All.Matches<Tag>(t => t.Name.StartsWith("media:", StringComparison.Ordinal)));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmptyList_WhenPrefixDoesNotMatch()
    {
        // Arrange
        var tag = new Tag { Name = "genre:action" };
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new TagQuery { Prefix = "media:" },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task QueryAsync_ReturnsTags_WhenDownloadClientMatches()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "TestClient",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        var tag1 = new Tag { Name = "client:primary" };
        var tag2 = new Tag { Name = "client:backup" };
        var unrelatedTag = new Tag { Name = "unrelated" };

        tag1.DownloadClients.Add(client);
        tag2.DownloadClients.Add(client);

        _context.DownloadClients.Add(client);
        _context.Tags.AddRange(tag1, tag2, unrelatedTag);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new TagQuery { EntityId = client.Id },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Has.Exactly(2).Items);
            Assert.That(result.Items, Has.One.Property(nameof(Tag.Name)).EqualTo("client:primary"));
            Assert.That(result.Items, Has.One.Property(nameof(Tag.Name)).EqualTo("client:backup"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsTags_WhenContentHierarchyItemMatches()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var item = new ContentHierarchyItem
        {
            MetadataContainerId = container.Id,
            ContentType = "type-a",
            Level = "type-a",
            Slug = "tagged-type-a",
            Name = "Tagged Type A",
            Monitoring = MonitoringMode.Monitored,
            CreatedAt = DateTime.UtcNow
        };
        _context.ContentHierarchyItems.Add(item);

        var tag1 = new Tag { Name = "genre:action" };
        var tag2 = new Tag { Name = "quality:hd" };
        var unrelatedTag = new Tag { Name = "unrelated" };

        tag1.MetadataContainers.Add(container);
        tag2.MetadataContainers.Add(container);

        _context.Tags.AddRange(tag1, tag2, unrelatedTag);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new TagQuery { EntityId = item.Id },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Has.Exactly(2).Items);
            Assert.That(result.Items, Has.One.Property(nameof(Tag.Name)).EqualTo("genre:action"));
            Assert.That(result.Items, Has.One.Property(nameof(Tag.Name)).EqualTo("quality:hd"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmptyList_WhenEntityHasNoTags()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "TestClient",
            ProviderType = "downloadclient-a",
            Enabled = true,
            Status = IntegrationHealthStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new TagQuery { EntityId = client.Id },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task QueryAsync_ReturnsEmptyList_WhenEntityDoesNotExist()
    {
        // Act
        var result = await _subject.QueryAsync(new TagQuery { EntityId = Guid.NewGuid() },
            new() { PageNumber = 1, PageSize = 100 },
            null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items, Is.Empty);
    }

    [TestCase(SortDirection.Ascending, new[] { "tag-a", "tag-b", "tag-c" })]
    [TestCase(SortDirection.Descending, new[] { "tag-c", "tag-b", "tag-a" })]
    public async Task QueryAsync_OrdersByTheRequestedField_WhenSortIsGiven(
        SortDirection direction,
        string[] expectedNames)
    {
        // Arrange
        _context.Tags.AddRange(new Tag { Name = "tag-b" }, new Tag { Name = "tag-c" }, new Tag { Name = "tag-a" });
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new TagQuery(),
            new() { PageNumber = 1, PageSize = 100 },
            new() { PropertyName = nameof(Tag.Name), Direction = direction })
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Select(tag => tag.Name), Is.EqualTo(expectedNames));
    }
}
