using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.Paging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public sealed class MaterializedMetadataRepositoryTests
{
    private static readonly MetadataCriteria[] ShadowedCollectionCriteria =
    [
        new()
        {
            ExternalIds = new() { Some = [[new() { Value = new() { And = [new() { Value = "obsolete" }] } }]] }
        },
        new()
        {
            ContentRatings = new() { Some = [[new() { Rating = new() { And = [new() { Value = "R" }] } }]] }
        },
        new()
        {
            Images = new() { Some = [[new() { Source = new() { And = [new() { Value = "obsolete" }] } }]] }
        },
        new()
        {
            Previews = new() { Some = [[new() { Title = new() { And = [new() { Value = "obsolete" }] } }]] }
        },
        new()
        {
            References = new() { Some = [[new() { DisplayName = new() { And = [new() { Value = "obsolete" }] } }]] }
        },
        new()
        {
            Awards = new() { Some = [[new() { Status = new() { And = [new() { Value = "obsolete" }] } }]] }
        }
    ];

    private MagpieDbContext _context = null!;
    private IMaterializedMetadataRepository _subject = null!;

    [SetUp]
    public void SetUp()
    {
        var dbLoggerCategories = new List<string>();
        var loggerFactory = NullLoggerFactory.Instance;

        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .EnableSensitiveDataLogging()
            .UseLoggerFactory(loggerFactory)
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _subject = new MaterializedMetadataRepository(
            _context,
            loggerFactory.CreateLogger<MaterializedMetadataRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Test]
    public async Task ListAllAsync_ReturnsAllContainers_WhenContainersExist()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container1.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Movie One",
            CreatedAt = DateTime.UtcNow
        });

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Movie Two",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoContainers()
    {
        // Act
        var result = await _subject.ListAllAsync().ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task QueryAsync_ReturnsRequestedContainerWithOwnCollections_WhenContainerIdIsSpecified()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Movie One",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Genres.Add(new() { Ordinal = 0, Name = "Action" });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution2 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Movie Two",
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Genres.Add(new() { Ordinal = 0, Name = "Comedy" });
        container2.Contributions.Add(contribution2);

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new() { ContainerIds = [container1.Id] },
            new() { PageNumber = 1, PageSize = 1 },
            null).ConfigureAwait(false);
        var metadata = result.Items.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalItems, Is.EqualTo(1));
            Assert.That(metadata.ContainerId, Is.EqualTo(container1.Id));
            Assert.That(metadata.Title, Is.EqualTo("Movie One"));
            Assert.That(metadata.Genres, Has.Exactly(1).Items);
            Assert.That(metadata.Genres, Has.All.Property(nameof(Genre.Name)).EqualTo("Action"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsEmpty_WhenContainerMissing()
    {
        // Act
        var result = await _subject.QueryAsync(
            new() { ContainerIds = [Guid.NewGuid()] },
            new() { PageNumber = 1, PageSize = 1 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task ListAllAsync_MaterializesScalarFields_WhenHighestPriorityContributionIsProvided()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container.Contributions.Add(new()
        {
            ProviderType = "primary",
            Ordinal = 0,
            Title = "Primary Title",
            Overview = null,
            Language = "en",
            CreatedAt = DateTime.UtcNow
        });
        container.Contributions.Add(new()
        {
            ProviderType = "secondary",
            Ordinal = 1,
            Title = "Secondary Title",
            Overview = "Secondary Overview",
            Language = "es",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("Primary Title"));
            Assert.That(result, Has.Property(nameof(result.Language)).EqualTo("en"));
            Assert.That(result, Has.Property(nameof(result.Overview)).EqualTo("Secondary Overview"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesAllScalarFields_WhenContributionsContainEveryScalarType()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container.Contributions.Add(new()
        {
            ProviderType = "complete",
            Ordinal = 0,
            Title = "Complete Title",
            OriginalTitle = "Original",
            SortTitle = "Complete, The",
            Language = "en",
            Overview = "A complete overview",
            PublishDate = new(2020, 6, 15),
            Duration = TimeSpan.FromMinutes(120),
            SequenceNumber = 3,
            ProductionStatus = "Released",
            Established = new(1990, 1, 1),
            Dissolved = new(2010, 12, 31),
            BirthDate = new(1970, 5, 20),
            DeathDate = new(2050, 1, 1),
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("Complete Title"));
            Assert.That(result, Has.Property(nameof(result.OriginalTitle)).EqualTo("Original"));
            Assert.That(result, Has.Property(nameof(result.SortTitle)).EqualTo("Complete, The"));
            Assert.That(result, Has.Property(nameof(result.Language)).EqualTo("en"));
            Assert.That(result, Has.Property(nameof(result.Overview)).EqualTo("A complete overview"));
            Assert.That(result, Has.Property(nameof(result.PublishDate)).EqualTo(new DateTime(2020, 6, 15)));
            Assert.That(result, Has.Property(nameof(result.Duration)).EqualTo(TimeSpan.FromMinutes(120)));
            Assert.That(result, Has.Property(nameof(result.SequenceNumber)).EqualTo(3));
            Assert.That(result, Has.Property(nameof(result.ProductionStatus)).EqualTo("Released"));
            Assert.That(result, Has.Property(nameof(result.Established)).EqualTo(new DateTime(1990, 1, 1)));
            Assert.That(result, Has.Property(nameof(result.Dissolved)).EqualTo(new DateTime(2010, 12, 31)));
            Assert.That(result, Has.Property(nameof(result.BirthDate)).EqualTo(new DateTime(1970, 5, 20)));
            Assert.That(result, Has.Property(nameof(result.DeathDate)).EqualTo(new DateTime(2050, 1, 1)));
        }
    }

    [Test]
    public async Task ListAllAsync_ReturnsNullScalars_WhenNoContributions()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.ContainerId)).Not.EqualTo(Guid.Empty));
            Assert.That(result, Has.Property(nameof(result.Title)).Null);
            Assert.That(result, Has.Property(nameof(result.Overview)).Null);
            Assert.That(result, Has.Property(nameof(result.PublishDate)).Null);
        }
    }

    [Test]
    public async Task ListAllAsync_ScalarFallthrough_WhenPrimaryHasNulls()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container.Contributions.Add(new()
        {
            ProviderType = "first",
            Ordinal = 0,
            Title = "First Title",
            Overview = null,
            Language = null,
            CreatedAt = DateTime.UtcNow
        });
        container.Contributions.Add(new()
        {
            ProviderType = "second",
            Ordinal = 1,
            Title = "Second Title",
            Overview = "Second Overview",
            Language = null,
            CreatedAt = DateTime.UtcNow
        });
        container.Contributions.Add(new()
        {
            ProviderType = "third",
            Ordinal = 2,
            Title = "Third Title",
            Overview = "Third Overview",
            Language = "fr",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("First Title"));
            Assert.That(result, Has.Property(nameof(result.Overview)).EqualTo("Second Overview"));
            Assert.That(result, Has.Property(nameof(result.Language)).EqualTo("fr"));
        }
    }

    [Test]
    public async Task ListAllAsync_LowestOrdinalWins_WhenScalarContributionsConflict()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container.Contributions.Add(new()
        {
            ProviderType = "provider-a",
            Ordinal = 0,
            Title = "Provider Title",
            Overview = "Provider Overview",
            CreatedAt = DateTime.UtcNow
        });
        container.Contributions.Add(new()
        {
            ProviderType = "user",
            Ordinal = -1,
            Overview = "User Overview",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Overview)).EqualTo("User Overview"));
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("Provider Title"));
        }
    }

    [Test]
    public async Task ListAllAsync_UsesLowerOrdinalScalarValues_WhenHigherOrdinalValuesExist()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container.Contributions.Add(new()
        {
            ProviderType = "primary",
            Ordinal = 0,
            Title = "Primary Title",
            Overview = "Primary Overview",
            CreatedAt = DateTime.UtcNow
        });
        container.Contributions.Add(new()
        {
            ProviderType = "secondary",
            Ordinal = 1,
            Title = "Secondary Title",
            Overview = "Secondary Overview",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("Primary Title"));
            Assert.That(result, Has.Property(nameof(result.Overview)).EqualTo("Primary Overview"));
        }
    }

    [Test]
    public async Task ListAllAsync_ReturnsTheSameMetadata_WhenRepeated()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container.Contributions.Add(new()
        {
            ProviderType = "primary",
            Ordinal = 0,
            Title = "Primary Title",
            Overview = "Primary Overview",
            Language = "en",
            CreatedAt = DateTime.UtcNow
        });
        container.Contributions.Add(new()
        {
            ProviderType = "secondary",
            Ordinal = 1,
            Title = "Secondary Title",
            Overview = "Secondary Overview",
            Language = "fr",
            CreatedAt = DateTime.UtcNow
        });
        container.Contributions.Add(new()
        {
            ProviderType = "tertiary",
            Ordinal = 2,
            Title = "Tertiary Title",
            Overview = "Tertiary Overview",
            Language = "de",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var runs = new List<(string? Title, string? Overview, string? Language)>();
        for (var i = 0; i < 5; i++)
        {
            var snapshot = (await _subject.ListAllAsync().ConfigureAwait(false)).First();
            runs.Add((snapshot.Title, snapshot.Overview, snapshot.Language));
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(runs.Distinct().ToList(), Has.Count.EqualTo(1));
            Assert.That(runs[0].Title, Is.EqualTo("Primary Title"));
            Assert.That(runs[0].Overview, Is.EqualTo("Primary Overview"));
            Assert.That(runs[0].Language, Is.EqualTo("en"));
        }
    }

    [Test]
    public async Task ListAllAsync_LowestOrdinalWins_WhenGenreContributionsConflict()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var user = new MetadataContribution
        {
            ProviderType = "user",
            Ordinal = -1,
            CreatedAt = DateTime.UtcNow
        };
        user.Genres.Add(new() { Ordinal = 0, Name = "ACTION" });
        user.Genres.Add(new() { Ordinal = 1, Name = "Drama" });

        var provider = new MetadataContribution
        {
            ProviderType = "provider-a",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        provider.Genres.Add(new() { Ordinal = 0, Name = "Action" });

        container.Contributions.Add(user);
        container.Contributions.Add(provider);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(result.Genres, Has.Some.Property(nameof(Genre.Name)).EqualTo("ACTION"));
        Assert.That(result.Genres, Has.Some.Property(nameof(Genre.Name)).EqualTo("Drama"));
    }

    [Test]
    public async Task ListAllAsync_LowestOrdinalWins_WhenExternalIdContributionsConflict()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var user = new MetadataContribution
        {
            ProviderType = "user",
            Ordinal = -1,
            CreatedAt = DateTime.UtcNow
        };
        user.ExternalIds.Add(new() { Ordinal = 0, Source = "provider-c", Value = "tt-user" });

        var provider = new MetadataContribution
        {
            ProviderType = "provider-a",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        provider.ExternalIds.Add(new() { Ordinal = 0, Source = "provider-c", Value = "tt-provider" });

        container.Contributions.Add(user);
        container.Contributions.Add(provider);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(result.ExternalIds, Has.Some.Property(nameof(ExternalId.Value)).EqualTo("tt-user"));
    }

    [Test]
    public async Task ListAllAsync_LowestOrdinalWins_WhenImageContributionsConflict()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var user = new MetadataContribution
        {
            ProviderType = "user",
            Ordinal = -1,
            CreatedAt = DateTime.UtcNow
        };
        user.Images.Add(new() { Ordinal = 0, Type = "poster", Url = new Uri("https://user/poster.jpg") });

        var provider = new MetadataContribution
        {
            ProviderType = "provider-a",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        provider.Images.Add(new() { Ordinal = 0, Type = "poster", Url = new Uri("https://provider/poster.jpg") });

        container.Contributions.Add(user);
        container.Contributions.Add(provider);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(result.Images, Has.Some.Property(nameof(Image.Url)).EqualTo(new Uri("https://user/poster.jpg")));
    }

    [Test]
    public async Task ListAllAsync_MaterializesGenres_WhenMultipleContributionsExist()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Genres.Add(new() { Ordinal = 0, Name = "Action" });
        contribution1.Genres.Add(new() { Ordinal = 1, Name = "Comedy" });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Genres.Add(new() { Ordinal = 0, Name = "Drama" });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Genres)).With.Exactly(3).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesGenres_WhenNameCaseInsensitiveMatches()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Genres.Add(new() { Ordinal = 0, Name = "Action" });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Genres.Add(new() { Ordinal = 0, Name = "ACTION" });
        contribution2.Genres.Add(new() { Ordinal = 1, Name = "Comedy" });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Genres)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_MaterializesExternalIds_WhenMultipleContributionsExist()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.ExternalIds.Add(new() { Ordinal = 0, Source = "provider-c", Value = "tt1234567" });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.ExternalIds.Add(new() { Ordinal = 0, Source = "provider-a", Value = "12345" });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.ExternalIds)).With.Exactly(2).Items);
    }

    [Test]
    public async Task QueryAsync_ReturnsSameExternalIdsAsFullScan_WhenContainerIsSpecified()
    {
        // Arrange
        var target = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var targetPrimary = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        targetPrimary.ExternalIds.Add(new() { Ordinal = 0, Source = "provider-c", Value = "tt1234567" });
        var targetSecondary = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        targetSecondary.ExternalIds.Add(new() { Ordinal = 0, Source = "provider-a", Value = "12345" });
        target.Contributions.Add(targetPrimary);
        target.Contributions.Add(targetSecondary);

        var other = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var otherContribution = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        otherContribution.ExternalIds.Add(new() { Ordinal = 0, Source = "provider-c", Value = "tt-other" });
        other.Contributions.Add(otherContribution);

        _context.MetadataContainers.AddRange(target, other);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var fullScanIds = (await _subject.ListAllAsync().ConfigureAwait(false))
            .Single(m => m.ContainerId == target.Id)
            .ExternalIds!
            .Select(e => (e.Source, e.Value))
            .OrderBy(e => e.Source)
            .ToList();

        // Act
        var scoped = (await _subject.QueryAsync(
                new() { ContainerIds = [target.Id] },
                new() { PageNumber = 1, PageSize = 1 },
                null).ConfigureAwait(false))
            .Items.Single().ExternalIds!
            .Select(e => (e.Source, e.Value))
            .OrderBy(e => e.Source)
            .ToList();

        // Assert
        Assert.That(scoped, Is.EqualTo(fullScanIds));
    }

    [Test]
    public async Task QueryAsync_ReturnsEmptyExternalIds_WhenContainerHasNoExternalIds()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container.Contributions.Add(new()
        {
            ProviderType = "primary",
            Ordinal = 0,
            Title = "No Ids",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new() { ContainerIds = [container.Id] },
            new() { PageNumber = 1, PageSize = 1 },
            null).ConfigureAwait(false);

        // Assert
        Assert.That(result.Items.Single().ExternalIds, Is.Empty);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesExternalIds_WhenSourceCaseInsensitiveMatches()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.ExternalIds.Add(new() { Ordinal = 0, Source = "provider-c", Value = "tt1234567" });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.ExternalIds.Add(new() { Ordinal = 0, Source = "PROVIDER-C", Value = "tt9999999" });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.ExternalIds)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.ExternalIds))
                    .One.Property(nameof(ExternalId.Value)).EqualTo("tt1234567"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesContentRatings_WhenMultipleContributionsExist()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.ContentRatings.Add(new() { Ordinal = 0, Body = "MPAA", Rating = "PG-13" });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.ContentRatings.Add(new() { Ordinal = 0, Body = "BBFC", Rating = "12A" });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.ContentRatings)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesContentRatings_WhenRatingsBodyMatches()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.ContentRatings.Add(new() { Ordinal = 0, Body = "MPAA", Rating = "PG-13" });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.ContentRatings.Add(new() { Ordinal = 0, Body = "mpaa", Rating = "R" });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.ContentRatings)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.ContentRatings))
                    .One.Property(nameof(ContentRating.Rating)).EqualTo("PG-13"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesAwards_WhenMultipleContributionsExist()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Awards.Add(new()
        {
            Ordinal = 0,
            Body = "Academy Awards",
            Name = "Best Picture",
            Year = 2020,
            Category = null
        });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Awards.Add(new()
        {
            Ordinal = 0,
            Body = "Golden Globes",
            Name = "Best Drama",
            Year = 2020,
            Category = null
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Awards)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesAwards_WhenBodyNameYearCategoryMatches()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Awards.Add(new()
        {
            Ordinal = 0,
            Body = "Academy Awards",
            Name = "Best Picture",
            Year = 2020,
            Category = "Feature Film",
            Status = "Won"
        });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Awards.Add(new()
        {
            Ordinal = 0,
            Body = "ACADEMY AWARDS",
            Name = "BEST PICTURE",
            Year = 2020,
            Category = "FEATURE FILM",
            Status = "Nominated"
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Awards)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Awards)).One.Property(nameof(Award.Status)).EqualTo("Won"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesAlternateTexts_WhenMultipleContributionsExist()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.AlternateTexts.Add(new()
        {
            Ordinal = 0,
            Kind = "tagline",
            Language = "en",
            Text = "In space no one can hear you scream"
        });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.AlternateTexts.Add(new()
        {
            Ordinal = 0,
            Kind = "tagline",
            Language = "es",
            Text = "En el espacio nadie puede oirte gritar"
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.AlternateTexts)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesAlternateTexts_WhenKindLanguageTextMatches()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.AlternateTexts.Add(new()
        {
            Ordinal = 0,
            Kind = "tagline",
            Language = "en",
            Text = "In space no one can hear you scream"
        });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.AlternateTexts.Add(new()
        {
            Ordinal = 0,
            Kind = "TAGLINE",
            Language = "EN",
            Text = "IN SPACE NO ONE CAN HEAR YOU SCREAM"
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.AlternateTexts)).With.Exactly(1).Items);
    }

    [Test]
    public async Task ListAllAsync_MaterializesReferences_WhenMultipleContributionsExist()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.References.Add(new()
        {
            Ordinal = 0,
            Source = "Wikipedia",
            Kind = "homepage",
            Url = new("https://en.wikipedia.org/wiki/Movie")
        });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.References.Add(new()
        {
            Ordinal = 0,
            Source = "PROVIDER-H",
            Kind = "database",
            Url = new("https://www.provider-h.example/title/tt1234567")
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.References)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesReferences_WhenSourceKindLanguageMatches()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.References.Add(new()
        {
            Ordinal = 0,
            Source = "Wikipedia",
            Kind = "homepage",
            Language = "en",
            Url = new("https://en.wikipedia.org/wiki/Movie")
        });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.References.Add(new()
        {
            Ordinal = 0,
            Source = "WIKIPEDIA",
            Kind = "HOMEPAGE",
            Language = "EN",
            Url = new("https://en.wikipedia.org/wiki/Different")
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.References)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.References))
                    .One.Property(nameof(Reference.Url)).Property(nameof(Uri.AbsoluteUri))
                    .EqualTo("https://en.wikipedia.org/wiki/Movie"));
        }
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesReferences_WhenLanguagesAreNull()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var primary = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        primary.References.Add(new()
        {
            Ordinal = 0,
            Source = "Wikipedia",
            Kind = "homepage",
            Url = new("https://example.com/primary")
        });
        var secondary = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        secondary.References.Add(new()
        {
            Ordinal = 0,
            Source = "WIKIPEDIA",
            Kind = "HOMEPAGE",
            Url = new("https://example.com/secondary")
        });
        container.Contributions.Add(primary);
        container.Contributions.Add(secondary);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.References, Has.Exactly(1).Items);
            Assert.That(result.References!.Single().Url.AbsoluteUri, Is.EqualTo("https://example.com/primary"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesRelationships_WhenMultipleContributionsExist()
    {
        // Arrange
        var relatedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relatedContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "actor",
            RelatedMetadataContainerId = relatedContainer.Id,
            Qualifier = "Lead"
        });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "director",
            RelatedMetadataContainerId = relatedContainer.Id,
            Qualifier = null
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First(r => r.ContainerId == container.Id);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Relationships)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesRelationships_WhenKindRelatedIdQualifierMatches()
    {
        // Arrange
        var relatedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relatedContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "actor",
            RelatedMetadataContainerId = relatedContainer.Id,
            Qualifier = "Lead"
        });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "ACTOR",
            RelatedMetadataContainerId = relatedContainer.Id,
            Qualifier = "LEAD"
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First(r => r.ContainerId == container.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Relationships)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Relationships))
                    .One.Property(nameof(Relationship.Kind)).EqualTo("actor"));
        }
    }

    [Test]
    public async Task ListAllAsync_DropsUnqualifiedRelationship_WhenQualifiedDuplicateExists()
    {
        // Arrange
        var relatedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relatedContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "actor",
            RelatedMetadataContainerId = relatedContainer.Id,
            Qualifier = "Lead"
        });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "actor",
            RelatedMetadataContainerId = relatedContainer.Id,
            Qualifier = null
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First(r => r.ContainerId == container.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Relationships)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Relationships))
                    .One.Property(nameof(Relationship.Qualifier)).EqualTo("Lead"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesImages_WhenMultipleContributionsExist()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Images.Add(new() { Ordinal = 0, Type = "poster", Url = new("https://example.com/poster1.jpg") });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Images.Add(new()
        {
            Ordinal = 0,
            Type = "backdrop",
            Url = new("https://example.com/backdrop1.jpg")
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Images)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsImagesInOrdinalOrder_WhenInsertedInReverseOrder()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution = new MetadataContribution
        {
            ProviderType = "provider",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution.Images.Add(new()
        {
            Ordinal = 1,
            Type = "backdrop",
            Url = new("https://example.com/second.jpg")
        });
        contribution.Images.Add(new()
        {
            Ordinal = 0,
            Type = "poster",
            Url = new("https://example.com/first.jpg")
        });

        container.Contributions.Add(contribution);
        _context.MetadataContainers.Add(container);

        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(
            result.Images!.Select(image => image.Url.AbsoluteUri),
            Is.EqualTo(["https://example.com/first.jpg", "https://example.com/second.jpg"]));
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesImages_WhenKindMatches()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Images.Add(new()
        {
            Ordinal = 0,
            Type = "poster",
            Url = new("https://example.com/poster-primary.jpg")
        });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Images.Add(new()
        {
            Ordinal = 0,
            Type = "POSTER",
            Url = new("https://example.com/poster-secondary.jpg")
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Images)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Images))
                    .One.Property(nameof(Image.Url)).Property(nameof(Uri.AbsoluteUri))
                    .EqualTo("https://example.com/poster-primary.jpg"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesPreviews_WhenMultipleContributionsExist()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Previews.Add(new()
        {
            Ordinal = 0,
            Kind = "trailer",
            Url = new("https://youtube.com/watch?v=abc123")
        });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Previews.Add(new()
        {
            Ordinal = 0,
            Kind = "teaser",
            Url = new("https://youtube.com/watch?v=xyz789")
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Previews)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesPreviews_WhenKindMatches()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Previews.Add(new()
        {
            Ordinal = 0,
            Kind = "trailer",
            Url = new("https://youtube.com/watch?v=primary")
        });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Previews.Add(new()
        {
            Ordinal = 0,
            Kind = "TRAILER",
            Url = new("https://youtube.com/watch?v=secondary")
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Previews)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Previews))
                    .One.Property(nameof(Preview.Url)).Property(nameof(Uri.AbsoluteUri))
                    .EqualTo("https://youtube.com/watch?v=primary"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesCustomMetadata_WhenMultipleContributionsExist()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.CustomMetadata.Add(new() { Key = "budget", Value = "100000000" });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.CustomMetadata.Add(new() { Key = "revenue", Value = "500000000" });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.CustomMetadata)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesCustomMetadata_WhenKeyCaseInsensitiveMatches()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.CustomMetadata.Add(new() { Key = "budget", Value = "100000000" });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.CustomMetadata.Add(new() { Key = "BUDGET", Value = "999999999" });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.ListAllAsync().ConfigureAwait(false)).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.CustomMetadata)).With.Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.CustomMetadata))
                    .One.Property(nameof(CustomMetadataValue.Value)).EqualTo("100000000"));
        }
    }

    [Test]
    public async Task QueryAsync_PrioritizesMatchingLanguage_WhenLanguagePreferenceIsProvided()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container.Contributions.Add(new()
        {
            ProviderType = "primary",
            Ordinal = 0,
            Title = "English Title",
            Overview = "English Overview",
            Language = "en",
            CreatedAt = DateTime.UtcNow
        });
        container.Contributions.Add(new()
        {
            ProviderType = "secondary",
            Ordinal = 1,
            Title = "Titulo en Espanol",
            Overview = "Resumen en Espanol",
            Language = "es",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(
            new() { PreferredLanguage = "es" },
            new() { PageNumber = 1, PageSize = 1 },
            null).ConfigureAwait(false)).Items.First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("Titulo en Espanol"));
            Assert.That(result, Has.Property(nameof(result.Overview)).EqualTo("Resumen en Espanol"));
            Assert.That(result, Has.Property(nameof(result.Language)).EqualTo("es"));
        }
    }

    [Test]
    public async Task QueryAsync_UsesOrdinalFallback_WhenPreferredLanguageDoesNotMatch()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container.Contributions.Add(new()
        {
            ProviderType = "primary",
            Ordinal = 0,
            Title = "English Title",
            Language = "en",
            CreatedAt = DateTime.UtcNow
        });
        container.Contributions.Add(new()
        {
            ProviderType = "secondary",
            Ordinal = 1,
            Title = "German Title",
            Language = "de",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(
            new() { PreferredLanguage = "fr" },
            new() { PageNumber = 1, PageSize = 1 },
            null).ConfigureAwait(false)).Items.First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("English Title"));
    }

    [Test]
    public async Task QueryAsync_UsesOrdinalOnly_WhenNullLanguagePreferenceIsProvided()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container.Contributions.Add(new()
        {
            ProviderType = "primary",
            Ordinal = 0,
            Title = "Primary Title",
            Language = "en",
            CreatedAt = DateTime.UtcNow
        });
        container.Contributions.Add(new()
        {
            ProviderType = "secondary",
            Ordinal = 1,
            Title = "Secondary Title",
            Language = "es",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = (await _subject.QueryAsync(
            new() { PreferredLanguage = null },
            new() { PageNumber = 1, PageSize = 1 },
            null).ConfigureAwait(false)).Items.First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("Primary Title"));
    }

    [Test]
    public async Task QueryAsync_ReturnsOnlyTheNamedContainers_WhenContainerIdsAreGiven()
    {
        // Arrange
        var bravo = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        bravo.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Bravo",
            SortTitle = "Bravo",
            CreatedAt = DateTime.UtcNow
        });
        var alpha = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        alpha.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Alpha",
            SortTitle = "Alpha",
            CreatedAt = DateTime.UtcNow
        });
        var charlie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        charlie.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Charlie",
            SortTitle = "Charlie",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.AddRange(bravo, alpha, charlie);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(
            new() { ContainerIds = [bravo.Id, charlie.Id] },
            new() { PageNumber = 1, PageSize = 1 },
            new() { PropertyName = nameof(MaterializedMetadata.SortTitle), Direction = SortDirection.Ascending })
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalItems, Is.EqualTo(2));
            Assert.That(result.Items.Single().ContainerId, Is.EqualTo(bravo.Id));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsAllItems_WhenNoCriteria()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container1.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Movie One",
            CreatedAt = DateTime.UtcNow
        });
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Movie Two",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenTitleEqualsFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container1.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "The Matrix",
            CreatedAt = DateTime.UtcNow
        });
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Inception",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria()
        {
            Title = new()
            {
                And = [new() { Value = "The Matrix" }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("The Matrix"));
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(1));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenTitleContainsFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container1.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "The Matrix",
            CreatedAt = DateTime.UtcNow
        });
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "The Matrix Reloaded",
            CreatedAt = DateTime.UtcNow
        });
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container3.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Inception",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.AddRange(container1, container2, container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria()
        {
            Title = new()
            {
                And = [new() { Operator = CriteriaValueOperator.ContainsString, Value = "Matrix" }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenPublishDateFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container1.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Old Movie",
            PublishDate = new(2000, 1, 1),
            CreatedAt = DateTime.UtcNow
        });
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "New Movie",
            PublishDate = new(2020, 1, 1),
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria()
        {
            PublishDate = new()
            {
                And = [new()
                {
                    Operator = CriteriaValueOperator.GreaterThan,
                    Value = new TemporalValue
                    {
                        Value = new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                        Precision = TemporalPrecision.Instant
                    }
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("New Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenAwardsContainsFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Oscar Winner",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Awards.Add(new() { Ordinal = 0, Body = "Academy Awards", Name = "Best Picture", Year = 2020 });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "No Awards",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria()
        {
            Awards = new()
            {
                Some =
[[new() { Body = new() { And = [new() { Value = "Academy Awards" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Oscar Winner"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenAwardsNotContainsFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Oscar Winner",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Awards.Add(new() { Ordinal = 0, Body = "Academy Awards", Name = "Best Picture", Year = 2020 });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "No Awards",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria()
        {
            Awards = new()
            {
                None = [new() { Body = new() { And = [new() { Value = "Academy Awards" }] } }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("No Awards"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsRequestedPage_WhenPagingIsSet()
    {
        // Arrange
        for (var i = 1; i <= 10; i++)
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            container.Contributions.Add(new()
            {
                ProviderType = "test",
                Ordinal = 0,
                Title = $"Movie {i:D2}",
                CreatedAt = DateTime.UtcNow
            });
            _context.MetadataContainers.Add(container);
        }

        await _context.SaveChangesAsync().ConfigureAwait(false);

        var paging = new PagingParameter { PageNumber = 2, PageSize = 3 };

        // Act
        var result = await _subject.QueryAsync(new(), paging, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(3).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(10));
            Assert.That(result, Has.Property(nameof(result.PageNumber)).EqualTo(2));
            Assert.That(result, Has.Property(nameof(result.PageSize)).EqualTo(3));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsAscendingItems_WhenAscendingSortIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container1.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Zebra",
            SortTitle = "Zebra",
            CreatedAt = DateTime.UtcNow
        });
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Apple",
            SortTitle = "Apple",
            CreatedAt = DateTime.UtcNow
        });
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container3.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Mango",
            SortTitle = "Mango",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.AddRange(container1, container2, container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "SortTitle", Direction = SortDirection.Ascending };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var titles = result.Items.Select(r => r.Title).ToList();
        Assert.That(titles, Is.EqualTo(["Apple", "Mango", "Zebra"]));
    }

    [Test]
    public async Task QueryAsync_ReturnsDescendingItems_WhenDescendingSortIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container1.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Zebra",
            SortTitle = "Zebra",
            CreatedAt = DateTime.UtcNow
        });
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Apple",
            SortTitle = "Apple",
            CreatedAt = DateTime.UtcNow
        });
        var container3 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container3.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Mango",
            SortTitle = "Mango",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.AddRange(container1, container2, container3);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "SortTitle", Direction = SortDirection.Descending };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var titles = result.Items.Select(r => r.Title).ToList();
        Assert.That(titles, Is.EqualTo(["Zebra", "Mango", "Apple"]));
    }

    [Test]
    public async Task QueryAsync_ReturnsFilteredSortedPage_WhenCombinedOptionsAreSet()
    {
        // Arrange
        for (var i = 1; i <= 10; i++)
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            container.Contributions.Add(new()
            {
                ProviderType = "test",
                Ordinal = 0,
                Title = $"Action Movie {i:D2}",
                SortTitle = $"Action Movie {i:D2}",
                Language = "en",
                CreatedAt = DateTime.UtcNow
            });
            _context.MetadataContainers.Add(container);
        }

        for (var i = 1; i <= 5; i++)
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            container.Contributions.Add(new()
            {
                ProviderType = "test",
                Ordinal = 0,
                Title = $"Drama Movie {i:D2}",
                SortTitle = $"Drama Movie {i:D2}",
                Language = "en",
                CreatedAt = DateTime.UtcNow
            });
            _context.MetadataContainers.Add(container);
        }

        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Title = new()
            {
                And = [new() { Operator = CriteriaValueOperator.ContainsString, Value = "Action" }]
            }
        };
        var paging = new PagingParameter { PageNumber = 2, PageSize = 3 };
        var sort = new SortParameter { PropertyName = "SortTitle", Direction = SortDirection.Descending };

        // Act
        var result = await _subject.QueryAsync(criteria, paging, sort).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(3).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(10));
            var titles = result.Items.Select(r => r.Title).ToList();
            Assert.That(titles, Is.EqualTo(["Action Movie 07", "Action Movie 06", "Action Movie 05"]));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsCorrectTotalItems_WhenFiltersExcludeRows()
    {
        // Arrange
        for (var i = 0; i < 20; i++)
        {
            var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
            container.Contributions.Add(new()
            {
                ProviderType = "test",
                Ordinal = 0,
                Title = i < 5 ? "Match" : "NoMatch",
                CreatedAt = DateTime.UtcNow
            });
            _context.MetadataContainers.Add(container);
        }

        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Title = new()
            {
                And = [new() { Value = "Match" }]
            }
        };
        var paging = new PagingParameter { PageNumber = 1, PageSize = 2 };

        // Act
        var result = await _subject.QueryAsync(criteria, paging, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(5));
        }
    }

    [Test]
    public async Task QueryAsync_FallsBackToDefaultOrder_WhenInvalidSortPropertyIsRequested()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Solo",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "InvalidProperty", Direction = SortDirection.Ascending };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(1));
    }

    [Test]
    public async Task QueryAsync_FiltersByMultipleAwardCriteria_WhenMultipleAwardAlternativesExist()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Multi Award Winner",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Awards.Add(new() { Ordinal = 0, Body = "Academy Awards", Name = "Best Picture", Year = 2020 });
        contribution1.Awards.Add(new() { Ordinal = 1, Body = "Golden Globes", Name = "Best Drama", Year = 2020 });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution2 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Oscar Only",
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Awards.Add(new() { Ordinal = 0, Body = "Academy Awards", Name = "Best Picture", Year = 2020 });
        container2.Contributions.Add(contribution2);

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Awards = new()
            {
                Some =
[
[
                    new()
                    {
                        Body = new()
                        {
                            And = [new() { Value = "Academy Awards" }]
                        }
                    },
                    new()
                    {
                        Body = new()
                        {
                            And = [new() { Value = "Golden Globes" }]
                        }
                    }
]
]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(2).Items);
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenGenresContainsFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Action Movie",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Genres.Add(new() { Ordinal = 0, Name = "Action" });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Drama Movie",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Genres = new()
            {
                Some =
[[new() { Name = new() { And = [new() { Value = "Action" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Action Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenGenresNotContainsFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Action Movie",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Genres.Add(new() { Ordinal = 0, Name = "Action" });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "No Genre",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Genres = new()
            {
                None = [new() { Name = new() { And = [new() { Value = "Action" }] } }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("No Genre"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenGenreHasMultipleConditions()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Action Movie",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Genres.Add(new() { Ordinal = 0, Name = "Action" });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution2 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Science Fiction Movie",
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Genres.Add(new() { Ordinal = 0, Name = "Science Fiction" });
        container2.Contributions.Add(contribution2);

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Genres = new()
            {
                Some =
[
[
                    new()
                    {
                        Name = new()
                        {
                            And =
                            [
                                new() { Value = "ction", Operator = CriteriaValueOperator.EndsWithString },
                                new() { Value = "A", Operator = CriteriaValueOperator.StartsWithString }
                            ]
                        }
                    }
]
]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Action Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsNoItems_WhenTheCriterionNamesAShadowedValue(
        [ValueSource(nameof(ShadowedCollectionCriteria))] MetadataCriteria criteria)
    {
        // Arrange
        var container = new MetadataContainer();
        var primary = new MetadataContribution
        {
            Id = Guid.NewGuid(),
            ProviderType = "primary",
            Ordinal = 0,
            Title = "Movie"
        };
        primary.ExternalIds.Add(new() { Source = "IMDB", Value = "current" });
        primary.ContentRatings.Add(new() { Body = "MPAA", Rating = "PG" });
        primary.Images.Add(new() { Type = "POSTER", Source = "current", Url = new("https://example.com/current") });
        primary.Previews.Add(new() { Kind = "TRAILER", Title = "current", Url = new("https://example.com/current") });
        primary.References.Add(new()
        {
            Kind = "HOME",
            Source = "SITE",
            DisplayName = "current",
            Url = new("https://example.com/current"),
        });
        primary.Awards.Add(new() { Body = "ACADEMY", Name = "ACTOR", Status = "current" });
        var secondary = new MetadataContribution
        {
            Id = Guid.NewGuid(),
            ProviderType = "secondary",
            Ordinal = 1,
            Title = "Movie"
        };
        secondary.ExternalIds.Add(new() { Source = "imdb", Value = "obsolete" });
        secondary.ContentRatings.Add(new() { Body = "mpaa", Rating = "R" });
        secondary.Images.Add(new() { Type = "poster", Source = "obsolete", Url = new("https://example.com/obsolete") });
        secondary.Previews.Add(new()
        {
            Kind = "trailer",
            Title = "obsolete",
            Url = new("https://example.com/obsolete"),
        });
        secondary.References.Add(new()
        {
            Kind = "home",
            Source = "site",
            DisplayName = "obsolete",
            Url = new("https://example.com/obsolete"),
        });
        secondary.Awards.Add(new() { Body = "academy", Name = "actor", Status = "obsolete" });
        container.Contributions.Add(primary);
        container.Contributions.Add(secondary);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(criteria, new() { PageNumber = 1, PageSize = 25 }, null)
            .ConfigureAwait(false);

        // Assert
        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public async Task QueryAsync_ReturnsTheEffectiveExternalId_WhenLaterContributionsAreShadowed()
    {
        // Arrange
        var container = new MetadataContainer();
        var primary = new MetadataContribution
        {
            Id = Guid.NewGuid(),
            ProviderType = "primary",
            Ordinal = 0,
            Title = "Movie"
        };
        primary.ExternalIds.Add(new() { Source = "IMDB", Value = "current" });
        primary.ContentRatings.Add(new() { Body = "MPAA", Rating = "PG" });
        primary.Images.Add(new() { Type = "POSTER", Source = "current", Url = new("https://example.com/current") });
        primary.Previews.Add(new() { Kind = "TRAILER", Title = "current", Url = new("https://example.com/current") });
        primary.References.Add(new()
        {
            Kind = "HOME",
            Source = "SITE",
            DisplayName = "current",
            Url = new("https://example.com/current"),
        });
        primary.Awards.Add(new() { Body = "ACADEMY", Name = "ACTOR", Status = "current" });
        var secondary = new MetadataContribution
        {
            Id = Guid.NewGuid(),
            ProviderType = "secondary",
            Ordinal = 1,
            Title = "Movie"
        };
        secondary.ExternalIds.Add(new() { Source = "imdb", Value = "obsolete" });
        secondary.ContentRatings.Add(new() { Body = "mpaa", Rating = "R" });
        secondary.Images.Add(new() { Type = "poster", Source = "obsolete", Url = new("https://example.com/obsolete") });
        secondary.Previews.Add(new()
        {
            Kind = "trailer",
            Title = "obsolete",
            Url = new("https://example.com/obsolete"),
        });
        secondary.References.Add(new()
        {
            Kind = "home",
            Source = "site",
            DisplayName = "obsolete",
            Url = new("https://example.com/obsolete"),
        });
        secondary.Awards.Add(new() { Body = "academy", Name = "actor", Status = "obsolete" });
        container.Contributions.Add(primary);
        container.Contributions.Add(secondary);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        var result = await _subject.QueryAsync(new()
        {
            ExternalIds = new() { None = [new() { Value = new() { And = [new() { Value = "obsolete" }] } }] }
        }, new() { PageNumber = 1, PageSize = 25 }, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Items, Has.Exactly(1).Items);
            Assert.That(result.Items.Single().ExternalIds!.Single().Value, Is.EqualTo("current"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenExternalIdsFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "PROVIDER-H Movie",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.ExternalIds.Add(new() { Ordinal = 0, Source = "provider-c", Value = "tt1234567" });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "No External ID",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            ExternalIds = new()
            {
                Some =
[[new() { Source = new() { And = [new() { Value = "provider-c" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("PROVIDER-H Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenContentRatingsFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Rated Movie",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.ContentRatings.Add(new() { Ordinal = 0, Body = "MPAA", Rating = "PG-13" });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Unrated Movie",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            ContentRatings = new()
            {
                Some =
[[new() { Body = new() { And = [new() { Value = "MPAA" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Rated Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenAlternateTextsFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Movie With Tagline",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.AlternateTexts.Add(new()
        {
            Ordinal = 0,
            Kind = "tagline",
            Text = "Just when you thought it was safe"
        });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "No Tagline",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            AlternateTexts = new()
            {
                Some =
[[new() { Kind = new() { And = [new() { Value = "tagline" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Movie With Tagline"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenImagesFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Movie With Poster",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Images.Add(new()
        {
            Ordinal = 0,
            Type = "poster",
            Url = new Uri("https://example.com/poster.jpg")
        });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "No Images",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Images = new()
            {
                Some =
[[new() { Type = new() { And = [new() { Value = "poster" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Movie With Poster"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenPreviewsFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Movie With Trailer",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Previews.Add(new()
        {
            Ordinal = 0,
            Kind = "trailer",
            Url = new Uri("https://example.com/trailer.mp4"),
            Source = "youtube"
        });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "No Previews",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Previews = new()
            {
                Some =
[[new() { Source = new() { And = [new() { Value = "youtube" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Movie With Trailer"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenReferencesFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Movie With Reference",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.References.Add(new()
        {
            Ordinal = 0,
            Kind = "wikipedia",
            Source = "en.wikipedia.org",
            Url = new Uri("https://en.wikipedia.org/wiki/Movie")
        });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "No References",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            References = new()
            {
                Some =
[[new() { Kind = new() { And = [new() { Value = "wikipedia" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Movie With Reference"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenRelationshipsFilterIsSet()
    {
        // Arrange
        var relatedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        relatedContainer.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Related Movie",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.Add(relatedContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Movie With Sequel",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "sequel",
            RelatedMetadataContainerId = relatedContainer.Id
        });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "No Relationships",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
[[new() { Kind = new() { And = [new() { Value = "sequel" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Movie With Sequel"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByRelationships_WhenRelationshipKindCaseDiffers()
    {
        // Arrange
        var relatedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        relatedContainer.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Related Movie",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.Add(relatedContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Movie With Sequel",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "sequel",
            RelatedMetadataContainerId = relatedContainer.Id
        });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "No Relationships",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
[[new() { Kind = new() { And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Sequel" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Movie With Sequel"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByRelationships_WhenFilteringBySubjectTitle()
    {
        // Arrange
        var delphineSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        delphineSubject.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Delphine Ashworth",
            CreatedAt = DateTime.UtcNow
        });
        var marloweSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        marloweSubject.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Marlowe Quinn",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.AddRange(delphineSubject, marloweSubject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var delphineMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var delphineContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Delphine Movie",
            CreatedAt = DateTime.UtcNow
        };
        delphineContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "director",
            RelatedMetadataContainerId = delphineSubject.Id
        });
        delphineMovie.Contributions.Add(delphineContribution);

        var marloweMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var marloweContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Marlowe Movie",
            CreatedAt = DateTime.UtcNow
        };
        marloweContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "director",
            RelatedMetadataContainerId = marloweSubject.Id
        });
        marloweMovie.Contributions.Add(marloweContribution);

        _context.MetadataContainers.AddRange(delphineMovie, marloweMovie);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
[[new() { Subject = new() { And = [new() { Operator = CriteriaValueOperator.ContainsString, Value = "Delphine" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Delphine Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenSubjectAliasDiffersByCase()
    {
        // Arrange
        var kerrSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var kerrContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Odessandra Kerr",
            CreatedAt = DateTime.UtcNow
        };
        kerrContribution.AlternateTexts.Add(new() { Ordinal = 0, Kind = "Name", Text = "Odessa Kerr" });
        kerrSubject.Contributions.Add(kerrContribution);

        var otherSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        otherSubject.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Marlowe Quinn",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.AddRange(kerrSubject, otherSubject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var kerrMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var kerrMovieContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Kerr Movie",
            CreatedAt = DateTime.UtcNow
        };
        kerrMovieContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "director",
            RelatedMetadataContainerId = kerrSubject.Id
        });
        kerrMovie.Contributions.Add(kerrMovieContribution);

        var otherMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var otherMovieContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Other Movie",
            CreatedAt = DateTime.UtcNow
        };
        otherMovieContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "director",
            RelatedMetadataContainerId = otherSubject.Id
        });
        otherMovie.Contributions.Add(otherMovieContribution);

        _context.MetadataContainers.AddRange(kerrMovie, otherMovie);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var containsAliasCriteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
                [
                    [new()
                    {
                        Subject = new()
                        {
                            And = [new()
                            {
                                Operator = CriteriaValueOperator.ContainsString,
                                Value = "odessa kerr"
                            }]
                        }
                    }                                                            ]
                ]
            }
        };
        var equalsAliasCriteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
                [
                    [new()
                    {
                        Subject = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Odessa Kerr" }]
                        }
                    }                                                            ]
                ]
            }
        };
        var containsTitleCriteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
                [
                    [new()
                    {
                        Subject = new()
                        {
                            And = [new()
                            {
                                Operator = CriteriaValueOperator.ContainsString,
                                Value = "odessandra"
                            }]
                        }
                    }                                                            ]
                ]
            }
        };

        // Act
        var containsAliasResult = await _subject.QueryAsync(containsAliasCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);
        var containsAliasItems = containsAliasResult.Items.ToList();
        var equalsAliasResult = await _subject.QueryAsync(equalsAliasCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);
        var equalsAliasItems = equalsAliasResult.Items.ToList();
        var containsTitleResult = await _subject.QueryAsync(containsTitleCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);
        var containsTitleItems = containsTitleResult.Items.ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(containsAliasItems, Has.Exactly(1).Items);
            Assert.That(containsAliasItems[0].Title, Is.EqualTo("Kerr Movie"));
            Assert.That(equalsAliasItems, Has.Exactly(1).Items);
            Assert.That(equalsAliasItems[0].Title, Is.EqualTo("Kerr Movie"));
            Assert.That(containsTitleItems, Has.Exactly(1).Items);
            Assert.That(containsTitleItems[0].Title, Is.EqualTo("Kerr Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_ExcludesAliasMatch_WhenNegativeSubjectOperatorIsSet()
    {
        // Arrange
        var bobSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var bobContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Bob Smith",
            CreatedAt = DateTime.UtcNow
        };
        bobContribution.AlternateTexts.Add(new() { Ordinal = 0, Kind = "Name", Text = "Nova" });
        bobSubject.Contributions.Add(bobContribution);

        var marloweSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        marloweSubject.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Marlowe Quinn",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.AddRange(bobSubject, marloweSubject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var bobMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var bobMovieContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Bob Movie",
            CreatedAt = DateTime.UtcNow
        };
        bobMovieContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "director",
            RelatedMetadataContainerId = bobSubject.Id
        });
        bobMovie.Contributions.Add(bobMovieContribution);

        var marloweMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var marloweMovieContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Marlowe Movie",
            CreatedAt = DateTime.UtcNow
        };
        marloweMovieContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "director",
            RelatedMetadataContainerId = marloweSubject.Id
        });
        marloweMovie.Contributions.Add(marloweMovieContribution);

        _context.MetadataContainers.AddRange(bobMovie, marloweMovie);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var notContainsCriteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
                [
                    [new()
                    {
                        Subject = new()
                        {
                            And = [new()
                            {
                                Operator = CriteriaValueOperator.NotContainsString,
                                Value = "nova"
                            }]
                        }
                    }                                                            ]
                ]
            }
        };
        var notEqualsCriteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
                [
                    [new()
                    {
                        Subject = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.NotEquals, Value = "Nova" }]
                        }
                    }                                                            ]
                ]
            }
        };

        // Act
        var notContainsResult = await _subject.QueryAsync(notContainsCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);
        var notContainsTitles = notContainsResult.Items.Select(i => i.Title).ToList();
        var notEqualsResult = await _subject.QueryAsync(notEqualsCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);
        var notEqualsTitles = notEqualsResult.Items.Select(i => i.Title).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(notContainsTitles, Is.EquivalentTo(["Marlowe Movie"]));
            Assert.That(notEqualsTitles, Is.EquivalentTo(["Marlowe Movie"]));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByRelationships_WhenFilteringByTypePersonSubject()
    {
        // Arrange
        var personSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var studioSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(personSubject, studioSubject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.Subjects.AddRange(
            new Subject { MetadataContainerId = personSubject.Id, Kind = "person", CreatedAt = DateTime.UtcNow },
            new Subject { MetadataContainerId = studioSubject.Id, Kind = "studio", CreatedAt = DateTime.UtcNow });

        var personMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var personContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Person Movie",
            CreatedAt = DateTime.UtcNow
        };
        personContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "director",
            RelatedMetadataContainerId = personSubject.Id
        });
        personMovie.Contributions.Add(personContribution);

        var studioMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var studioContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Studio Movie",
            CreatedAt = DateTime.UtcNow
        };
        studioContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "publisher",
            RelatedMetadataContainerId = studioSubject.Id
        });
        studioMovie.Contributions.Add(studioContribution);

        _context.MetadataContainers.AddRange(personMovie, studioMovie);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
[[new() { Type = new() { And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Person" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Person Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByRelationships_WhenFilteringByTypeContentLevel()
    {
        // Arrange
        var levelTarget = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var personTarget = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(levelTarget, personTarget);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.ContentHierarchyItems.Add(new ContentHierarchyItem
        {
            MetadataContainerId = levelTarget.Id,
            ContentType = "type-e",
            Level = "type-e-level",
            Slug = "level-target",
            Name = "Level Target",
            Monitoring = true,
            CreatedAt = DateTime.UtcNow
        });
        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = personTarget.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });

        var linkedMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var linkedContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Linked Movie",
            CreatedAt = DateTime.UtcNow
        };
        linkedContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "related",
            RelatedMetadataContainerId = levelTarget.Id
        });
        linkedMovie.Contributions.Add(linkedContribution);

        var personMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var personContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Person Movie",
            CreatedAt = DateTime.UtcNow
        };
        personContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "director",
            RelatedMetadataContainerId = personTarget.Id
        });
        personMovie.Contributions.Add(personContribution);

        _context.MetadataContainers.AddRange(linkedMovie, personMovie);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
[[new() { Type = new() { And = [new() { Operator = CriteriaValueOperator.Equals, Value = "type-e-level" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Linked Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_ExcludesByRelationships_WhenFilteringByTypeNone()
    {
        // Arrange
        var personSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var studioSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(personSubject, studioSubject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.Subjects.AddRange(
            new Subject { MetadataContainerId = personSubject.Id, Kind = "person", CreatedAt = DateTime.UtcNow },
            new Subject { MetadataContainerId = studioSubject.Id, Kind = "studio", CreatedAt = DateTime.UtcNow });

        var personMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var personContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Person Movie",
            CreatedAt = DateTime.UtcNow
        };
        personContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "director",
            RelatedMetadataContainerId = personSubject.Id
        });
        personMovie.Contributions.Add(personContribution);

        var studioMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var studioContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Studio Movie",
            CreatedAt = DateTime.UtcNow
        };
        studioContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "publisher",
            RelatedMetadataContainerId = studioSubject.Id
        });
        studioMovie.Contributions.Add(studioContribution);

        _context.MetadataContainers.AddRange(personMovie, studioMovie);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Relationships = new()
            {
                None = [new()
                {
                    Type = new()
                    {
                        And = [new() { Operator = CriteriaValueOperator.Equals, Value = "person" }]
                    }
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        var titles = result.Items.Select(i => i.Title).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(titles, Does.Contain("Studio Movie"));
            Assert.That(titles, Does.Not.Contain("Person Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByRelationships_WhenFilteringByTypeNotEquals()
    {
        // Arrange
        var personSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var studioSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.AddRange(personSubject, studioSubject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.Subjects.AddRange(
            new Subject { MetadataContainerId = personSubject.Id, Kind = "person", CreatedAt = DateTime.UtcNow },
            new Subject { MetadataContainerId = studioSubject.Id, Kind = "studio", CreatedAt = DateTime.UtcNow });

        var personMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var personContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Person Movie",
            CreatedAt = DateTime.UtcNow
        };
        personContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "director",
            RelatedMetadataContainerId = personSubject.Id
        });
        personMovie.Contributions.Add(personContribution);

        var studioMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var studioContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Studio Movie",
            CreatedAt = DateTime.UtcNow
        };
        studioContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "publisher",
            RelatedMetadataContainerId = studioSubject.Id
        });
        studioMovie.Contributions.Add(studioContribution);

        _context.MetadataContainers.AddRange(personMovie, studioMovie);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
[[new() { Type = new() { And = [new() { Operator = CriteriaValueOperator.NotEquals, Value = "person" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Studio Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByRelationships_WhenFilteringByTypeIsNullAndIsNotNull()
    {
        // Arrange
        var personSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var bareTarget = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        bareTarget.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Bare Target",
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.AddRange(personSubject, bareTarget);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        _context.Subjects.Add(new Subject
        {
            MetadataContainerId = personSubject.Id,
            Kind = "person",
            CreatedAt = DateTime.UtcNow
        });

        var materializedMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var materializedContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Materialized Movie",
            CreatedAt = DateTime.UtcNow
        };
        materializedContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "director",
            RelatedMetadataContainerId = personSubject.Id
        });
        materializedMovie.Contributions.Add(materializedContribution);

        var bareMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var bareContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Bare Movie",
            CreatedAt = DateTime.UtcNow
        };
        bareContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "related",
            RelatedMetadataContainerId = bareTarget.Id
        });
        bareMovie.Contributions.Add(bareContribution);

        _context.MetadataContainers.AddRange(materializedMovie, bareMovie);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var notNullCriteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
[[new() { Type = new() { And = [new() { Operator = CriteriaValueOperator.IsNotNull }] } }]]
            }
        };
        var nullCriteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
[[new() { Type = new() { And = [new() { Operator = CriteriaValueOperator.IsNull }] } }]]
            }
        };

        // Act
        var notNullResult = await _subject.QueryAsync(notNullCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);
        var nullResult = await _subject.QueryAsync(nullCriteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(notNullResult, Has.Property(nameof(notNullResult.Items)).Exactly(1).Items);
            Assert.That(
                notNullResult,
                Has.Property(nameof(notNullResult.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Materialized Movie"));
            Assert.That(nullResult, Has.Property(nameof(nullResult.Items)).Exactly(1).Items);
            Assert.That(
                nullResult,
                Has.Property(nameof(nullResult.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Bare Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByRelationships_WhenFilteringBySubjectExternalId()
    {
        // Arrange
        var pinnedSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var pinnedSubjectContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Delphine Ashworth",
            CreatedAt = DateTime.UtcNow
        };
        pinnedSubjectContribution.ExternalIds.Add(new()
        {
            Ordinal = 0,
            Source = "provider-b.director",
            Value = "12345"
        });
        pinnedSubject.Contributions.Add(pinnedSubjectContribution);

        var otherSubject = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var otherSubjectContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Delphine Ashworth",
            CreatedAt = DateTime.UtcNow
        };
        otherSubjectContribution.ExternalIds.Add(new()
        {
            Ordinal = 0,
            Source = "provider-b.director",
            Value = "99999"
        });
        otherSubject.Contributions.Add(otherSubjectContribution);
        _context.MetadataContainers.AddRange(pinnedSubject, otherSubject);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var pinnedMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var pinnedContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Pinned Movie",
            CreatedAt = DateTime.UtcNow
        };
        pinnedContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "director",
            RelatedMetadataContainerId = pinnedSubject.Id
        });
        pinnedMovie.Contributions.Add(pinnedContribution);

        var otherMovie = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var otherContribution = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Other Movie",
            CreatedAt = DateTime.UtcNow
        };
        otherContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "director",
            RelatedMetadataContainerId = otherSubject.Id
        });
        otherMovie.Contributions.Add(otherContribution);

        _context.MetadataContainers.AddRange(pinnedMovie, otherMovie);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
[
[new()
                {
                    ExternalIds = new()
                    {
                        Some = [[new()
                        {
                            Value = new()
                            {
                                And = [new() { Operator = CriteriaValueOperator.Equals, Value = "12345" }]
                            },
                            Source = new()
                            {
                                And =
                                [
                                    new()
                                    {
                                        Operator = CriteriaValueOperator.Equals,
                                        Value = "provider-b.director"
                                    }
                                ]
                            }
                        }]]
                    }
                }]
]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Pinned Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenTagsContainsFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container1.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Tagged Movie",
            CreatedAt = DateTime.UtcNow
        });

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Untagged Movie",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var tag = new Tag { Name = "favorite" };
        tag.MetadataContainers.Add(container1);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Tags = new()
            {
                Some =
[[new() { Name = new() { And = [new() { Value = "favorite" }] } }]]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Tagged Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenTagsNotContainsFilterIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container1.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Tagged Movie",
            CreatedAt = DateTime.UtcNow
        });

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Untagged Movie",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var tag = new Tag { Name = "excluded" };
        tag.MetadataContainers.Add(container1);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Tags = new()
            {
                None = [new() { Name = new() { And = [new() { Value = "excluded" }] } }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Untagged Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsMatchingItems_WhenTagHasMultipleConditions()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container1.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Action Movie",
            CreatedAt = DateTime.UtcNow
        });

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Adventure Movie",
            CreatedAt = DateTime.UtcNow
        });

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var tag1 = new Tag { Name = "action-packed" };
        tag1.MetadataContainers.Add(container1);
        var tag2 = new Tag { Name = "adventurous" };
        tag2.MetadataContainers.Add(container2);
        _context.Tags.AddRange(tag1, tag2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Tags = new()
            {
                Some =
[
[
                    new()
                    {
                        Name = new()
                        {
                            And =
                            [
                                new() { Value = "-packed", Operator = CriteriaValueOperator.EndsWithString },
                                new() { Value = "action", Operator = CriteriaValueOperator.StartsWithString }
                            ]
                        }
                    }
]
]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Action Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_SortsItems_WhenCamelCasePropertyNameIsSet()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container1.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Later",
            PublishDate = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        });
        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        container2.Contributions.Add(new()
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Earlier",
            PublishDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var sort = new SortParameter { PropertyName = "publishDate", Direction = SortDirection.Ascending };

        // Act
        var result = await _subject.QueryAsync(new(),
            new() { PageNumber = 1, PageSize = 100 },
            sort).ConfigureAwait(false);

        // Assert
        var titles = result.Items.Select(m => m.Title).ToList();
        Assert.That(titles, Is.EqualTo(["Earlier", "Later"]));
    }

    [Test]
    public async Task QueryAsync_SkipsTheShadowedRelationship_WhenAQualifiedDuplicateExists()
    {
        // Arrange
        var relatedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relatedContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var shadowedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var qualifiedContribution = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            Title = "Shadowed",
            CreatedAt = DateTime.UtcNow
        };
        qualifiedContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "actor",
            RelatedMetadataContainerId = relatedContainer.Id,
            Qualifier = "Lead"
        });
        var unqualifiedContribution = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            Title = "Shadowed",
            CreatedAt = DateTime.UtcNow
        };
        unqualifiedContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "actor",
            RelatedMetadataContainerId = relatedContainer.Id,
            Qualifier = null
        });
        shadowedContainer.Contributions.Add(qualifiedContribution);
        shadowedContainer.Contributions.Add(unqualifiedContribution);

        var plainContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var plainContribution = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            Title = "Unqualified",
            CreatedAt = DateTime.UtcNow
        };
        plainContribution.Relationships.Add(new()
        {
            Ordinal = 0,
            Kind = "actor",
            RelatedMetadataContainerId = relatedContainer.Id,
            Qualifier = null
        });
        plainContainer.Contributions.Add(plainContribution);

        _context.MetadataContainers.AddRange(shadowedContainer, plainContainer);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        var criteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some =
                [
                    [
                        new()
                        {
                            Kind = new() { And = [new() { Value = "actor" }] },
                            Qualifier = new() { And = [new() { Operator = CriteriaValueOperator.IsNull }] }
                        }
                    ]
                ]
            }
        };

        // Act
        var result = await _subject.QueryAsync(
            criteria,
            new() { PageNumber = 1, PageSize = 100 },
            null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(
                result,
                Has.Property(nameof(result.Items))
                    .One.Property(nameof(MaterializedMetadata.ContainerId)).EqualTo(plainContainer.Id));
        }
    }
}
