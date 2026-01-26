using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models.Filters.Content;
using Magpie.Models.Result;
using Magpie.Models.Users;
using Magpie.Models.Wishlists;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using ContentFilterEntity = Magpie.Data.Database.Entities.ContentFilter;
using HistoryEntryEntity = Magpie.Data.Database.Entities.HistoryEntry;
using UserEntity = Magpie.Data.Database.Entities.User;
using WishlistEntity = Magpie.Data.Database.Entities.Wishlist;
using WishlistTagEntity = Magpie.Data.Database.Entities.WishlistTag;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class WishlistRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IWishlistRepository _subject = null!;
    private Guid _userId;
    private Guid _contentFilterId;

    [SetUp]
    public void SetUp()
    {
        _context = new(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var user = new UserEntity
        {
            Username = "TestUser",
            Enabled = true,
            Role = Role.Admin,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Users.Add(user);

        var contentFilter = new ContentFilterEntity
        {
            Name = "TestFilter",
            Criteria = JsonSerializer.Serialize(new ContentItemCriteria
            {
                TargetLevel = new()
            }),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ContentFilters.Add(contentFilter);

        _context.SaveChanges();

        _userId = user.Id;
        _contentFilterId = contentFilter.Id;

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        var historyRepo = new HistoryRepository(_context, loggerFactory.CreateLogger<HistoryRepository>());

        _subject = new WishlistRepository(
            _context,
            historyRepo,
            loggerFactory.CreateLogger<WishlistRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    private void EnsureTagsExist(params string[] tagNames)
    {
        foreach (var tagName in tagNames)
        {
            if (!_context.Tags.Any(t => t.Name == tagName))
            {
                _context.Tags.Add(new() { Name = tagName });
            }
        }
        _context.SaveChanges();
    }

    [Test]
    public async Task GetByIdAsync_ReturnsWishlist()
    {
        // Arrange
        var entity = new WishlistEntity
        {
            Name = "TestWishlist",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Wishlists.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Wishlist?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Wishlist.Name)).EqualTo("TestWishlist"));
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Wishlist?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByIdAsync_IncludesTags()
    {
        // Arrange
        EnsureTagsExist("movies", "tv");
        var entity = new WishlistEntity
        {
            Name = "WishlistWithTags",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "movies" });
        entity.Tags.Add(new() { TagName = "tv" });
        _context.Wishlists.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByIdAsync(entity.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Wishlist?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Wishlist.Tags)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsAllWishlists()
    {
        // Arrange
        var entity1 = new WishlistEntity
        {
            Name = "Wishlist1",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new WishlistEntity
        {
            Name = "Wishlist2",
            ContentFilterId = _contentFilterId,
            Monitored = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Wishlists.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Wishlist>>>()
            .With.Property(nameof(SuccessResult<>.Value)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task GetAllAsync_ReturnsEmptyList_WhenNoWishlists()
    {
        // Act
        var result = await _subject.GetAllAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Wishlist>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByNameAsync_ReturnsWishlist()
    {
        // Arrange
        var entity = new WishlistEntity
        {
            Name = "UniqueWishlistName",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Wishlists.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByNameAsync("UniqueWishlistName");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Wishlist?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Wishlist.Name))
                .EqualTo("UniqueWishlistName"));
    }

    [Test]
    public async Task GetByNameAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _subject.GetByNameAsync("NonExistentWishlist");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<Wishlist?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task GetByUserIdAsync_ReturnsWishlists()
    {
        // Arrange
        var entity1 = new WishlistEntity
        {
            Name = "UserWishlist1",
            UserId = _userId,
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new WishlistEntity
        {
            Name = "UserWishlist2",
            UserId = _userId,
            ContentFilterId = _contentFilterId,
            Monitored = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new WishlistEntity
        {
            Name = "OtherWishlist",
            UserId = null,
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Wishlists.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByUserIdAsync(_userId);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Wishlist>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByTagAsync_ReturnsWishlists()
    {
        // Arrange
        EnsureTagsExist("shared", "unique");
        var entity1 = new WishlistEntity
        {
            Name = "Wishlist1",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity1.Tags.Add(new() { TagName = "shared" });
        var entity2 = new WishlistEntity
        {
            Name = "Wishlist2",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity2.Tags.Add(new() { TagName = "shared" });
        var entity3 = new WishlistEntity
        {
            Name = "Wishlist3",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity3.Tags.Add(new() { TagName = "unique" });
        _context.Wishlists.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByTagAsync("shared");

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Wishlist>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetByTagAsync_ReturnsEmptyList_WhenNoMatches()
    {
        // Act
        var result = await _subject.GetByTagAsync("nonexistent");

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<IReadOnlyList<Wishlist>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Empty);
    }

    [Test]
    public async Task GetByContentFilterIdAsync_ReturnsWishlists()
    {
        // Arrange
        var contentFilter2 = new ContentFilterEntity
        {
            Name = "AnotherFilter",
            Criteria = JsonSerializer.Serialize(new ContentItemCriteria
            {
                TargetLevel = new()
            }),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.ContentFilters.Add(contentFilter2);
        _context.SaveChanges();

        var entity1 = new WishlistEntity
        {
            Name = "Wishlist1",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new WishlistEntity
        {
            Name = "Wishlist2",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity3 = new WishlistEntity
        {
            Name = "Wishlist3",
            ContentFilterId = contentFilter2.Id,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Wishlists.AddRange(entity1, entity2, entity3);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetByContentFilterIdAsync(_contentFilterId);

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Wishlist>>;
        Assert.That(success, Is.Not.Null);
        Assert.That(success!.Value, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetMonitoredAsync_ReturnsMonitoredWishlists()
    {
        // Arrange
        var entity1 = new WishlistEntity
        {
            Name = "MonitoredWishlist",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var entity2 = new WishlistEntity
        {
            Name = "UnmonitoredWishlist",
            ContentFilterId = _contentFilterId,
            Monitored = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Wishlists.AddRange(entity1, entity2);
        _context.SaveChanges();

        // Act
        var result = await _subject.GetMonitoredAsync();

        // Assert
        var success = result as SuccessResult<IReadOnlyList<Wishlist>>;
        Assert.That(success, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success!.Value, Has.Count.EqualTo(1));
            Assert.That(success.Value[0].Name, Is.EqualTo("MonitoredWishlist"));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesWishlist()
    {
        // Arrange
        var model = new Wishlist(
            Guid.Empty,
            "NewWishlist",
            _userId,
            false,
            _contentFilterId,
            null,
            true,
            [],
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from w in _context.Wishlists.AsNoTracking()
            where w.Name == "NewWishlist"
            select w).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Wishlist>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Wishlist.Name)).EqualTo("NewWishlist"));
            Assert.That(entity, Is.InstanceOf<WishlistEntity>()
                .With.Property(nameof(WishlistEntity.Name)).EqualTo("NewWishlist")
                .And.Property(nameof(WishlistEntity.CreatedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Create")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Wishlist")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task CreateAsync_CreatesWishlistWithTags()
    {
        // Arrange
        EnsureTagsExist("movies", "tv", "anime");
        var model = new Wishlist(
            Guid.Empty,
            "WishlistWithTags",
            _userId,
            false,
            _contentFilterId,
            null,
            true,
            ["movies", "tv", "anime"],
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.CreateAsync(model, _userId);

        // Assert
        var entity = await (
            from w in _context.Wishlists.AsNoTracking().Include(w => w.Tags)
            where w.Name == "WishlistWithTags"
            select w).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<Wishlist>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(Wishlist.Tags)).With.Count.EqualTo(3));
            Assert.That(entity, Is.Not.Null);
            Assert.That(entity!.Tags, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task UpdateAsync_UpdatesWishlist()
    {
        // Arrange
        var entity = new WishlistEntity
        {
            Name = "OriginalName",
            ContentFilterId = _contentFilterId,
            Monitored = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Wishlists.Add(entity);
        _context.SaveChanges();

        var model = new Wishlist(
            entity.Id,
            "UpdatedName",
            _userId,
            true,
            _contentFilterId,
            null,
            true,
            [],
            "provider",
            "listId",
            TimeSpan.FromHours(1),
            null,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var updatedEntity = await (
            from w in _context.Wishlists.AsNoTracking()
            where w.Id == entity.Id
            select w).SingleOrDefaultAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<WishlistEntity>()
                .With.Property(nameof(WishlistEntity.Name)).EqualTo("UpdatedName")
                .And.Property(nameof(WishlistEntity.Monitored)).True
                .And.Property(nameof(WishlistEntity.OwnerLock)).True
                .And.Property(nameof(WishlistEntity.SyncProvider)).EqualTo("provider")
                .And.Property(nameof(WishlistEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(WishlistEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Update")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Wishlist")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var model = new Wishlist(
            Guid.NewGuid(),
            "NonExistent",
            _userId,
            false,
            _contentFilterId,
            null,
            true,
            [],
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateAsync_AddsAndRemovesTags()
    {
        // Arrange
        EnsureTagsExist("existing", "toRemove", "toAdd");
        var entity = new WishlistEntity
        {
            Name = "WishlistWithTags",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        entity.Tags.Add(new() { TagName = "toRemove" });
        _context.Wishlists.Add(entity);
        _context.SaveChanges();

        var model = new Wishlist(
            entity.Id,
            entity.Name,
            null,
            false,
            _contentFilterId,
            null,
            true,
            ["existing", "toAdd"],
            null,
            null,
            null,
            null,
            entity.CreatedAt,
            null,
            null,
            null);

        // Act
        var result = await _subject.UpdateAsync(model, _userId);

        // Assert
        var tagEntities = await _context.WishlistTags
            .AsNoTracking()
            .Where(t => t.WishlistId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(WishlistTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(WishlistTagEntity.TagName)).EqualTo("toAdd"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(WishlistTagEntity.TagName)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task DeleteAsync_DeletesWishlist()
    {
        // Arrange
        var entity = new WishlistEntity
        {
            Name = "ToDelete",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Wishlists.Add(entity);
        _context.SaveChanges();

        // Act
        var result = await _subject.DeleteAsync(entity.Id, _userId);

        // Assert
        var entityExists = await _context.Wishlists.AnyAsync(w => w.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(entityExists, Is.False);
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Delete")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Wishlist")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.DeleteAsync(Guid.NewGuid(), _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task DeleteAsync_CascadesToTags()
    {
        // Arrange
        EnsureTagsExist("tag1", "tag2");
        var entity = new WishlistEntity
        {
            Name = "WishlistWithTags",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "tag1" });
        entity.Tags.Add(new() { TagName = "tag2" });
        _context.Wishlists.Add(entity);
        _context.SaveChanges();

        var wishlistId = entity.Id;

        // Act
        var result = await _subject.DeleteAsync(wishlistId, _userId);

        // Assert
        var tagsExist = await _context.WishlistTags
            .AnyAsync(t => t.WishlistId == wishlistId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagsExist, Is.False);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesName()
    {
        // Arrange
        var entity = new WishlistEntity
        {
            Name = "OriginalName",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Wishlists.Add(entity);
        _context.SaveChanges();

        var patch = new WishlistPatch
        {
            Id = entity.Id,
            Name = new("PatchedName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.Wishlists
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.InstanceOf<WishlistEntity>()
                .With.Property(nameof(WishlistEntity.Name)).EqualTo("PatchedName")
                .And.Property(nameof(WishlistEntity.ModifiedAt)).Not.Null
                .And.Property(nameof(WishlistEntity.ModifiedByUserId)).EqualTo(_userId));
            Assert.That(await _context.HistoryEntries.ToListAsync(), Has.One
                .With.Property(nameof(HistoryEntryEntity.Operation)).EqualTo("Patch")
                .And.Property(nameof(HistoryEntryEntity.EntityType)).EqualTo("Wishlist")
                .And.Property(nameof(HistoryEntryEntity.UserId)).EqualTo(_userId));
        }
    }

    [Test]
    public async Task PatchAsync_PatchesMonitored()
    {
        // Arrange
        var entity = new WishlistEntity
        {
            Name = "TestWishlist",
            ContentFilterId = _contentFilterId,
            Monitored = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Wishlists.Add(entity);
        _context.SaveChanges();

        var patch = new WishlistPatch
        {
            Id = entity.Id,
            Monitored = new(true)
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var updatedEntity = await _context.Wishlists
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.Not.Null);
            Assert.That(updatedEntity!.Monitored, Is.True);
        }
    }

    [Test]
    public async Task PatchAsync_PatchesTags_AddAndRemove()
    {
        // Arrange
        EnsureTagsExist("existing", "toRemove", "toAdd");
        var entity = new WishlistEntity
        {
            Name = "TestWishlist",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        entity.Tags.Add(new() { TagName = "existing" });
        entity.Tags.Add(new() { TagName = "toRemove" });
        _context.Wishlists.Add(entity);
        _context.SaveChanges();

        var patch = new WishlistPatch
        {
            Id = entity.Id,
            Tags = new(Add: ["toAdd"], Remove: ["toRemove"])
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        var tagEntities = await _context.WishlistTags
            .AsNoTracking()
            .Where(t => t.WishlistId == entity.Id)
            .ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(tagEntities, Has.Count.EqualTo(2));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(WishlistTagEntity.TagName)).EqualTo("existing"));
            Assert.That(tagEntities, Has.One
                .With.Property(nameof(WishlistTagEntity.TagName)).EqualTo("toAdd"));
            Assert.That(tagEntities, Has.None
                .With.Property(nameof(WishlistTagEntity.TagName)).EqualTo("toRemove"));
        }
    }

    [Test]
    public async Task PatchAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var patch = new WishlistPatch
        {
            Id = Guid.NewGuid(),
            Name = new("NewName")
        };

        // Act
        var result = await _subject.PatchAsync(patch, _userId);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }

    [Test]
    public async Task UpdateLastSyncedAsync_UpdatesTimestamp()
    {
        // Arrange
        var entity = new WishlistEntity
        {
            Name = "TestWishlist",
            ContentFilterId = _contentFilterId,
            Monitored = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Wishlists.Add(entity);
        _context.SaveChanges();

        var syncTime = DateTimeOffset.UtcNow;

        // Act
        var result = await _subject.UpdateLastSyncedAsync(entity.Id, syncTime);

        // Assert
        var updatedEntity = await _context.Wishlists
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).True);
            Assert.That(updatedEntity, Is.Not.Null);
            Assert.That(updatedEntity!.LastSyncedAt, Is.EqualTo(syncTime).Within(TimeSpan.FromSeconds(1)));
        }
    }

    [Test]
    public async Task UpdateLastSyncedAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var result = await _subject.UpdateLastSyncedAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<bool>>().With.Property(nameof(SuccessResult<>.Value)).False);
    }
}
