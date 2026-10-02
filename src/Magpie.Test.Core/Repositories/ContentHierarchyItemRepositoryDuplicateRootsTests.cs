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
        _context.Dispose();
    }

    [Test]
    public async Task FindDuplicateRootsAsync_ReportsRoots_WhenTheyShareAnIdentityAtTheSameTypeAndLevel()
    {
        // Arrange
        var tvmaze = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            ContentType = "tv",
            Level = "show",
            Slug = $"item-{Guid.NewGuid():N}",
            Name = "Item",
            CreatedAt = new DateTime(2026, 1, 1),
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "tvmaze",
                        Ordinal = 0,
                        ExternalIds =
                        {
                            new ExternalId { Source = "tvmaze", Value = "1" },
                            new ExternalId { Source = "imdb", Value = "tt1" },
                        }
                    },
                }
            }
        };
        _context.ContentHierarchyItems.Add(tvmaze);
        var wikidata = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            ContentType = "tv",
            Level = "show",
            Slug = $"item-{Guid.NewGuid():N}",
            Name = "Item",
            CreatedAt = new DateTime(2026, 1, 1),
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "wikidata",
                        Ordinal = 0,
                        ExternalIds =
                        {
                            new ExternalId { Source = "wikidata", Value = "Q1" },
                            new ExternalId { Source = "imdb", Value = "tt1" },
                        }
                    },
                }
            }
        };
        _context.ContentHierarchyItems.Add(wikidata);
        var item3 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            ContentType = "tv",
            Level = "show",
            Slug = $"item-{Guid.NewGuid():N}",
            Name = "Item",
            CreatedAt = new DateTime(2026, 1, 1),
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "first",
                        Ordinal = 0,
                        ExternalIds =
                        {
                            new ExternalId { Source = "imdb", Value = "tt2" },
                        }
                    },
                }
            }
        };
        _context.ContentHierarchyItems.Add(item3);
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
        var upper = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            ContentType = "tv",
            Level = "show",
            Slug = $"item-{Guid.NewGuid():N}",
            Name = "Item",
            CreatedAt = new DateTime(2026, 1, 1),
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "first",
                        Ordinal = 0,
                        ExternalIds =
                        {
                            new ExternalId { Source = "IMDB", Value = "TT1" },
                        }
                    },
                }
            }
        };
        _context.ContentHierarchyItems.Add(upper);
        var lower = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            ContentType = "tv",
            Level = "show",
            Slug = $"item-{Guid.NewGuid():N}",
            Name = "Item",
            CreatedAt = new DateTime(2026, 1, 1),
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "second",
                        Ordinal = 0,
                        ExternalIds =
                        {
                            new ExternalId { Source = "imdb", Value = "tt1" },
                        }
                    },
                }
            }
        };
        _context.ContentHierarchyItems.Add(lower);
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
        var item6 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            ContentType = "tv",
            Level = "show",
            Slug = $"item-{Guid.NewGuid():N}",
            Name = "Item",
            CreatedAt = new DateTime(2026, 1, 1),
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "current",
                        Ordinal = 0,
                        ExternalIds =
                        {
                            new ExternalId { Source = "imdb", Value = "tt5" },
                        }
                    },
                    new MetadataContribution
                    {
                        ProviderType = "stale",
                        Ordinal = 1,
                        ExternalIds =
                        {
                            new ExternalId { Source = "imdb", Value = "tt1" },
                        }
                    },
                }
            }
        };
        _context.ContentHierarchyItems.Add(item6);
        var item7 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            ContentType = "tv",
            Level = "show",
            Slug = $"item-{Guid.NewGuid():N}",
            Name = "Item",
            CreatedAt = new DateTime(2026, 1, 1),
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "first",
                        Ordinal = 0,
                        ExternalIds =
                        {
                            new ExternalId { Source = "imdb", Value = "tt1" },
                        }
                    },
                }
            }
        };
        _context.ContentHierarchyItems.Add(item7);
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
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            ContentType = "tv",
            Level = "show",
            Slug = $"item-{Guid.NewGuid():N}",
            Name = "Item",
            CreatedAt = new DateTime(2026, 1, 1),
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "first",
                        Ordinal = 0,
                        ExternalIds =
                        {
                            new ExternalId { Source = "imdb", Value = "tt1" },
                        }
                    },
                }
            }
        };
        _context.ContentHierarchyItems.Add(show);
        var item9 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            ContentType = "tv",
            Level = "special",
            Slug = $"item-{Guid.NewGuid():N}",
            Name = "Item",
            CreatedAt = new DateTime(2026, 1, 1),
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "first",
                        Ordinal = 0,
                        ExternalIds =
                        {
                            new ExternalId { Source = "imdb", Value = "tt1" },
                        }
                    },
                }
            }
        };
        _context.ContentHierarchyItems.Add(item9);
        var item10 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            ContentType = "ebook",
            Level = "show",
            Slug = $"item-{Guid.NewGuid():N}",
            Name = "Item",
            CreatedAt = new DateTime(2026, 1, 1),
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "first",
                        Ordinal = 0,
                        ExternalIds =
                        {
                            new ExternalId { Source = "imdb", Value = "tt1" },
                        }
                    },
                }
            }
        };
        _context.ContentHierarchyItems.Add(item10);
        var item11 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = show.Id,
            ContentType = "tv",
            Level = "show",
            Slug = $"item-{Guid.NewGuid():N}",
            Name = "Item",
            CreatedAt = new DateTime(2026, 1, 1),
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "first",
                        Ordinal = 0,
                        ExternalIds =
                        {
                            new ExternalId { Source = "imdb", Value = "tt1" },
                        }
                    },
                }
            }
        };
        _context.ContentHierarchyItems.Add(item11);
        var item12 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            ContentType = "tv",
            Level = "show",
            Slug = $"item-{Guid.NewGuid():N}",
            Name = "Item",
            CreatedAt = new DateTime(2026, 1, 1),
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "first",
                        Ordinal = 0,
                        ExternalIds =
                        {
                            new ExternalId { Source = "tvdb", Value = "tt1" },
                        }
                    },
                }
            }
        };
        _context.ContentHierarchyItems.Add(item12);
        var item13 = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = null,
            ContentType = "tv",
            Level = "show",
            Slug = $"item-{Guid.NewGuid():N}",
            Name = "Item",
            CreatedAt = new DateTime(2026, 1, 1),
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "first",
                        Ordinal = 0,
                        ExternalIds =
                        {
                            new ExternalId { Source = "imdb", Value = "tt9" },
                        }
                    },
                    new MetadataContribution
                    {
                        ProviderType = "second",
                        Ordinal = 1,
                        ExternalIds =
                        {
                            new ExternalId { Source = "imdb", Value = "tt9" },
                        }
                    },
                }
            }
        };
        _context.ContentHierarchyItems.Add(item13);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var shared = await _subject.FindDuplicateRootsAsync().ConfigureAwait(false);

        // Assert
        Assert.That(shared, Is.Empty);
    }
}
