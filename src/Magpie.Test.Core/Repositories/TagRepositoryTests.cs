using System;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class TagRepositoryTests
{
    private MagpieDbContext _context = null!;
    private ITagRepository _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

        _subject = new TagRepository(
            _context,
            loggerFactory.CreateLogger<TagRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllTags()
    {
        // Arrange
        var tag1 = new Tag { Name = "tag1" };
        var tag2 = new Tag { Name = "tag2" };
        var tag3 = new Tag { Name = "tag3" };
        _context.Tags.AddRange(tag1, tag2, tag3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Has.Exactly(3).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoTags()
    {
        // Act
        var result = await _subject.ListAllAsync(default);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetByPrefixAsync_ReturnsMatchingTags()
    {
        // Arrange
        var tag1 = new Tag { Name = "media:movie" };
        var tag2 = new Tag { Name = "media:tv" };
        var tag3 = new Tag { Name = "genre:action" };
        _context.Tags.AddRange(tag1, tag2, tag3);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByPrefixAsync("media:");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Exactly(2).Items);
            Assert.That(result, Has.All.Matches<Tag>(t => t.Name.StartsWith("media:")));
        }
    }

    [Test]
    public async Task GetByPrefixAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Arrange
        var tag = new Tag { Name = "genre:action" };
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetByPrefixAsync("media:");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetTagsByEntityAsync_ReturnsTagsForDownloadClient()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "TestClient",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        var tag1 = new Tag { Name = "client:primary" };
        var tag2 = new Tag { Name = "client:backup" };
        var unrelatedTag = new Tag { Name = "unrelated" };

        tag1.DownloadClients.Add(client);
        tag2.DownloadClients.Add(client);

        _context.DownloadClients.Add(client);
        _context.Tags.AddRange(tag1, tag2, unrelatedTag);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetTagsByEntityAsync(client.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Exactly(2).Items);
            Assert.That(result, Has.One.Property(nameof(Tag.Name)).EqualTo("client:primary"));
            Assert.That(result, Has.One.Property(nameof(Tag.Name)).EqualTo("client:backup"));
        }
    }

    [Test]
    public async Task GetTagsByEntityAsync_ReturnsEmptyList_WhenEntityHasNoTags()
    {
        // Arrange
        var client = new DownloadClient
        {
            Name = "TestClient",
            ProviderType = "qbittorrent",
            Enabled = true,
            Status = DownloadClientStatus.Unknown,
            CreatedAt = DateTime.UtcNow
        };
        _context.DownloadClients.Add(client);
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.GetTagsByEntityAsync(client.Id);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetTagsByEntityAsync_ReturnsEmptyList_WhenEntityNotFound()
    {
        // Act
        var result = await _subject.GetTagsByEntityAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.Empty);
    }
}
