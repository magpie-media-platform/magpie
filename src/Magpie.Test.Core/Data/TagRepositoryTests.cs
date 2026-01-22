using System.Collections.Generic;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using TagEntity = Magpie.Data.Database.Entities.Tag;

namespace Magpie.Test.Core.Data;

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

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

        _subject = new TagRepository(_context, loggerFactory.CreateLogger<TagRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    [Test]
    public async Task GetAllAsync_ReturnsTags()
    {
        // Arrange
        _context.Tags.AddRange(
            new TagEntity { Name = "action" },
            new TagEntity { Name = "comedy" },
            new TagEntity { Name = "drama" });
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Tag>>>()
                .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(3));
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Tag>>>()
                .With.Property(nameof(SuccessResult<>.Value)).One.Property(nameof(Tag.Name)).EqualTo("action"));
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Tag>>>()
                .With.Property(nameof(SuccessResult<>.Value)).One.Property(nameof(Tag.Name)).EqualTo("comedy"));
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Tag>>>()
                .With.Property(nameof(SuccessResult<>.Value)).One.Property(nameof(Tag.Name)).EqualTo("drama"));
        }
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoTags()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Tag>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByPrefixAsync_ReturnsMatchingTags()
    {
        // Arrange
        _context.Tags.AddRange(
            new TagEntity { Name = "genre:action" },
            new TagEntity { Name = "genre:comedy" },
            new TagEntity { Name = "genre:drama" },
            new TagEntity { Name = "rating:pg" },
            new TagEntity { Name = "rating:r" });
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByPrefixAsync("genre:");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Tag>>>()
                .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(3));
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Tag>>>()
                .With.Property(nameof(SuccessResult<>.Value)).One.Property(nameof(Tag.Name)).EqualTo("genre:action"));
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Tag>>>()
                .With.Property(nameof(SuccessResult<>.Value)).One.Property(nameof(Tag.Name)).EqualTo("genre:comedy"));
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Tag>>>()
                .With.Property(nameof(SuccessResult<>.Value)).One.Property(nameof(Tag.Name)).EqualTo("genre:drama"));
        }
    }

    [Test]
    public async Task GetByPrefixAsync_ReturnsEmptyList_WhenNoMatchingTags()
    {
        // Arrange
        _context.Tags.AddRange(
            new TagEntity { Name = "genre:action" },
            new TagEntity { Name = "genre:comedy" });
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByPrefixAsync("rating:");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Tag>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByPrefixAsync_ReturnsEmptyList_WhenNoTags()
    {
        // Act
        var result = await _subject.GetByPrefixAsync("genre:");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Tag>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByPrefixAsync_MatchesExactPrefixOnly()
    {
        // Arrange
        _context.Tags.AddRange(
            new TagEntity { Name = "genre:action" },
            new TagEntity { Name = "genreaction" },
            new TagEntity { Name = "other:comedy" });
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByPrefixAsync("genre:");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Tag>>>()
                .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(1));
            Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Tag>>>()
                .With.Property(nameof(SuccessResult<>.Value)).One.Property(nameof(Tag.Name)).EqualTo("genre:action"));
        }
    }

    [Test]
    public async Task GetByPrefixAsync_ReturnsAllTags_WhenPrefixIsEmpty()
    {
        // Arrange
        _context.Tags.AddRange(
            new TagEntity { Name = "action" },
            new TagEntity { Name = "comedy" },
            new TagEntity { Name = "drama" });
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByPrefixAsync("");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Tag>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(3));
    }
}
