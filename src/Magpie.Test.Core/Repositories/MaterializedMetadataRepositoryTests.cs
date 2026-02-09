using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Magpie.Test.Core.Repositories;

[TestFixture]
public class MaterializedMetadataRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IMaterializedMetadataRepository _subject = null!;

    [SetUp]
    public void SetUp()
    {
        var dbLoggerCategories = new List<string>();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddTestLogger());

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
    public async Task ListAllAsync_ReturnsAllContainers()
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
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Has.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_ReturnsEmptyList_WhenNoContainers()
    {
        // Act
        var result = await _subject.ListAllAsync();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListAllAsync_MaterializesScalarFields_FromHighestPriorityContribution()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("Primary Title"));
            Assert.That(result, Has.Property(nameof(result.Language)).EqualTo("en"));
            Assert.That(result, Has.Property(nameof(result.Overview)).EqualTo("Secondary Overview"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesAllScalarFieldTypes()
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
            ProductionStatus = "Released",
            Established = new(1990, 1, 1),
            Dissolved = new(2010, 12, 31),
            BirthDate = new(1970, 5, 20),
            DeathDate = new(2050, 1, 1),
            CreatedAt = DateTime.UtcNow
        });
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("First Title"));
            Assert.That(result, Has.Property(nameof(result.Overview)).EqualTo("Second Overview"));
            Assert.That(result, Has.Property(nameof(result.Language)).EqualTo("fr"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesGenres_FromMultipleContributions()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Genres)).With.Exactly(3).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesGenres_ByNameCaseInsensitive()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Genres)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_MaterializesExternalIds_FromMultipleContributions()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.ExternalIds.Add(new() { Ordinal = 0, Source = "imdb", Value = "tt1234567" });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.ExternalIds.Add(new() { Ordinal = 0, Source = "tmdb", Value = "12345" });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.ExternalIds)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesExternalIds_BySourceCaseInsensitive()
    {
        // Arrange
        var container = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "primary",
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow
        };
        contribution1.ExternalIds.Add(new() { Ordinal = 0, Source = "imdb", Value = "tt1234567" });

        var contribution2 = new MetadataContribution
        {
            ProviderType = "secondary",
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow
        };
        contribution2.ExternalIds.Add(new() { Ordinal = 0, Source = "IMDB", Value = "tt9999999" });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.ExternalIds)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.ExternalIds))
                .One.Property(nameof(ExternalId.Value)).EqualTo("tt1234567"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesContentRatings_FromMultipleContributions()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.ContentRatings)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesContentRatings_ByRatingsBody()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.ContentRatings)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.ContentRatings))
                .One.Property(nameof(ContentRating.Rating)).EqualTo("PG-13"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesAwards_FromMultipleContributions()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Awards)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesAwards_ByBodyNameYearCategory()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Awards)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Awards))
                .One.Property(nameof(Award.Status)).EqualTo("Won"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesAlternateTexts_FromMultipleContributions()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.AlternateTexts)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesAlternateTexts_ByKindLanguageText()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.AlternateTexts)).With.Exactly(1).Items);
    }

    [Test]
    public async Task ListAllAsync_MaterializesReferences_FromMultipleContributions()
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
            Source = "IMDB",
            Kind = "database",
            Url = new("https://www.imdb.com/title/tt1234567")
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.References)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesReferences_BySourceKindLanguage()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.References)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.References))
                .One.Property(nameof(Reference.Url)).Property(nameof(Uri.AbsoluteUri))
                .EqualTo("https://en.wikipedia.org/wiki/Movie"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesRelationships_FromMultipleContributions()
    {
        // Arrange
        var relatedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relatedContainer);
        await _context.SaveChangesAsync();

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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First(r => r.ContainerId == container.Id);

        // Assert
        Assert.That(result, Has.Property(nameof(result.Relationships)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesRelationships_ByKindRelatedIdQualifier()
    {
        // Arrange
        var relatedContainer = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        _context.MetadataContainers.Add(relatedContainer);
        await _context.SaveChangesAsync();

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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First(r => r.ContainerId == container.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Relationships)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Relationships))
                .One.Property(nameof(Relationship.Kind)).EqualTo("actor"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesImages_FromMultipleContributions()
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
            Url = new("https://example.com/poster1.jpg")
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
            Type = "backdrop",
            Url = new("https://example.com/backdrop1.jpg")
        });

        container.Contributions.Add(contribution1);
        container.Contributions.Add(contribution2);
        _context.MetadataContainers.Add(container);
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Images)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesImages_ByKind()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Images)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Images))
                .One.Property(nameof(Image.Url)).Property(nameof(Uri.AbsoluteUri))
                .EqualTo("https://example.com/poster-primary.jpg"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesPreviews_FromMultipleContributions()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Previews)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesPreviews_ByKind()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Previews)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Previews))
                .One.Property(nameof(Preview.Url)).Property(nameof(Uri.AbsoluteUri))
                .EqualTo("https://youtube.com/watch?v=primary"));
        }
    }

    [Test]
    public async Task ListAllAsync_MaterializesCustomMetadata_FromMultipleContributions()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.CustomMetadata)).With.Exactly(2).Items);
    }

    [Test]
    public async Task ListAllAsync_DeduplicatesCustomMetadata_ByKeyCaseInsensitive()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync()).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.CustomMetadata)).With.Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.CustomMetadata))
                .One.Property(nameof(CustomMetadataValue.Value)).EqualTo("100000000"));
        }
    }

    [Test]
    public async Task ListAllAsync_WithLanguagePreference_PrioritizesMatchingLanguage()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync("es")).First();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("Titulo en Espanol"));
            Assert.That(result, Has.Property(nameof(result.Overview)).EqualTo("Resumen en Espanol"));
            Assert.That(result, Has.Property(nameof(result.Language)).EqualTo("es"));
        }
    }

    [Test]
    public async Task ListAllAsync_WithLanguagePreference_FallsBackToOrdinal_WhenNoMatch()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync("fr")).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("English Title"));
    }

    [Test]
    public async Task ListAllAsync_WithNullLanguagePreference_UsesOrdinalOnly()
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
        await _context.SaveChangesAsync();

        // Act
        var result = (await _subject.ListAllAsync(null)).First();

        // Assert
        Assert.That(result, Has.Property(nameof(result.Title)).EqualTo("Primary Title"));
    }

    #region QueryAsync Tests

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
        await _context.SaveChangesAsync();

        // Act
        var result = await _subject.QueryAsync(new(), null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByTitleEquals()
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria()
        {
            Title = new()
            {
                And = [new() { Value = "The Matrix" }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("The Matrix"));
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(1));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByTitleContainsString()
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria()
        {
            Title = new()
            {
                And = [new()
                {
                    Operator = CriteriaValueOperator.ContainsString,
                    Value = "Matrix"
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(2));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByPublishDateGreaterThan()
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria()
        {
            PublishDate = new()
            {
                And = [new()
                {
                    Operator = CriteriaValueOperator.GreaterThan,
                    Value = new(2010, 1, 1)
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("New Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByAwardsContains()
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
        contribution1.Awards.Add(new()
        {
            Ordinal = 0,
            Body = "Academy Awards",
            Name = "Best Picture",
            Year = 2020
        });
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria()
        {
            Awards = new()
            {
                Some = [new()
                {
                    Body = new()
                    {
                        And = [new() { Value = "Academy Awards" }]
                    }
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Oscar Winner"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByAwardsNotContains()
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
        contribution1.Awards.Add(new()
        {
            Ordinal = 0,
            Body = "Academy Awards",
            Name = "Best Picture",
            Year = 2020
        });
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria()
        {
            Awards = new()
            {
                None = [new()
                {
                    Body = new()
                    {
                        And = [new() { Value = "Academy Awards" }]
                    }
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("No Awards"));
        }
    }

    [Test]
    public async Task QueryAsync_AppliesPaging()
    {
        // Arrange
        for (int i = 1; i <= 10; i++)
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
        await _context.SaveChangesAsync();

        var paging = new PagingParameter { PageNumber = 2, PageSize = 3 };

        // Act
        var result = await _subject.QueryAsync(new(), paging, null);

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
    public async Task QueryAsync_AppliesSortAscending()
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
        await _context.SaveChangesAsync();

        var sort = new SortParameter { PropertyName = "SortTitle", Direction = SortDirection.Ascending };

        // Act
        var result = await _subject.QueryAsync(new(), null, sort);

        // Assert
        var titles = result.Items.Select(r => r.Title).ToList();
        Assert.That(titles, Is.EqualTo(["Apple", "Mango", "Zebra"]));
    }

    [Test]
    public async Task QueryAsync_AppliesSortDescending()
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
        await _context.SaveChangesAsync();

        var sort = new SortParameter { PropertyName = "SortTitle", Direction = SortDirection.Descending };

        // Act
        var result = await _subject.QueryAsync(new(), null, sort);

        // Assert
        var titles = result.Items.Select(r => r.Title).ToList();
        Assert.That(titles, Is.EqualTo(["Zebra", "Mango", "Apple"]));
    }

    [Test]
    public async Task QueryAsync_CombinesFilteringPagingAndSorting()
    {
        // Arrange
        for (int i = 1; i <= 10; i++)
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
        for (int i = 1; i <= 5; i++)
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            Title = new()
            {
                And = [new()
                {
                    Operator = CriteriaValueOperator.ContainsString,
                    Value = "Action"
                }]
            }
        };
        var paging = new PagingParameter { PageNumber = 2, PageSize = 3 };
        var sort = new SortParameter { PropertyName = "SortTitle", Direction = SortDirection.Descending };

        // Act
        var result = await _subject.QueryAsync(criteria, paging, sort);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(3).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(10));
            var titles = result.Items.Select(r => r.Title).ToList();
            Assert.That(titles, Is.EqualTo(
            [
                "Action Movie 07",
                "Action Movie 06",
                "Action Movie 05"
            ]));
        }
    }

    [Test]
    public async Task QueryAsync_ReturnsCorrectTotalItems_AfterFiltering()
    {
        // Arrange
        for (int i = 0; i < 20; i++)
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            Title = new()
            {
                And = [new() { Value = "Match" }]
            }
        };
        var paging = new PagingParameter { PageNumber = 1, PageSize = 2 };

        // Act
        var result = await _subject.QueryAsync(criteria, paging, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(2).Items);
            Assert.That(result, Has.Property(nameof(result.TotalItems)).EqualTo(5));
        }
    }

    [Test]
    public void QueryAsync_ThrowsArgumentException_ForInvalidSortProperty()
    {
        // Arrange
        var sort = new SortParameter { PropertyName = "InvalidProperty", Direction = SortDirection.Ascending };

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _subject.QueryAsync(new(), null, sort));
    }

    [Test]
    public async Task QueryAsync_FiltersByMultipleAwardCriteria_UsingOrSemantics()
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
        contribution1.Awards.Add(new()
        {
            Ordinal = 0,
            Body = "Academy Awards",
            Name = "Best Picture",
            Year = 2020
        });
        contribution1.Awards.Add(new()
        {
            Ordinal = 1,
            Body = "Golden Globes",
            Name = "Best Drama",
            Year = 2020
        });
        container1.Contributions.Add(contribution1);

        var container2 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution2 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "Oscar Only",
            CreatedAt = DateTime.UtcNow
        };
        contribution2.Awards.Add(new()
        {
            Ordinal = 0,
            Body = "Academy Awards",
            Name = "Best Picture",
            Year = 2020
        });
        container2.Contributions.Add(contribution2);

        _context.MetadataContainers.AddRange(container1, container2);
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            Awards = new()
            {
                Some =
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
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(2).Items);
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByGenresContains()
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            Genres = new()
            {
                Some = [new() { Name = new() { And = [new() { Value = "Action" }] } }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Action Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByGenresNotContains()
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            Genres = new()
            {
                None = [new() { Name = new() { And = [new() { Value = "Action" }] } }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            Genres = new()
            {
                Some =
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
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Action Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByExternalIdsContains()
    {
        // Arrange
        var container1 = new MetadataContainer { CreatedAt = DateTime.UtcNow };
        var contribution1 = new MetadataContribution
        {
            ProviderType = "test",
            Ordinal = 0,
            Title = "IMDB Movie",
            CreatedAt = DateTime.UtcNow
        };
        contribution1.ExternalIds.Add(new() { Ordinal = 0, Source = "imdb", Value = "tt1234567" });
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            ExternalIds = new()
            {
                Some = [new()
                {
                    Source = new() { And = [new() { Value = "imdb" }] }
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("IMDB Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByContentRatingsContains()
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            ContentRatings = new()
            {
                Some = [new()
                {
                    Body = new() { And = [new() { Value = "MPAA" }] }
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Rated Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByAlternateTextsContains()
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            AlternateTexts = new()
            {
                Some = [new()
                {
                    Kind = new() { And = [new() { Value = "tagline" }] }
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Movie With Tagline"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByImagesContains()
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            Images = new()
            {
                Some = [new()
                {
                    Type = new() { And = [new() { Value = "poster" }] }
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Movie With Poster"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByPreviewsContains()
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            Previews = new()
            {
                Some = [new()
                {
                    Source = new() { And = [new() { Value = "youtube" }] }
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Movie With Trailer"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByReferencesContains()
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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            References = new()
            {
                Some = [new()
                {
                    Kind = new() { And = [new() { Value = "wikipedia" }] }
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Movie With Reference"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByRelationshipsContains()
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
        await _context.SaveChangesAsync();

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
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            Relationships = new()
            {
                Some = [new()
                {
                    Kind = new() { And = [new() { Value = "sequel" }] }
                }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Movie With Sequel"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByTagsContains()
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
        await _context.SaveChangesAsync();

        var tag = new Tag { Name = "favorite" };
        tag.MetadataContainers.Add(container1);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            Tags = new()
            {
                Some = [new() { Name = new() { And = [new() { Value = "favorite" }] } }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Tagged Movie"));
        }
    }

    [Test]
    public async Task QueryAsync_FiltersByTagsNotContains()
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
        await _context.SaveChangesAsync();

        var tag = new Tag { Name = "excluded" };
        tag.MetadataContainers.Add(container1);
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            Tags = new()
            {
                None = [new() { Name = new() { And = [new() { Value = "excluded" }] } }]
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
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
        await _context.SaveChangesAsync();

        var tag1 = new Tag { Name = "action-packed" };
        tag1.MetadataContainers.Add(container1);
        var tag2 = new Tag { Name = "adventurous" };
        tag2.MetadataContainers.Add(container2);
        _context.Tags.AddRange(tag1, tag2);
        await _context.SaveChangesAsync();

        var criteria = new MetadataCriteria
        {
            Tags = new()
            {
                Some =
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
            }
        };

        // Act
        var result = await _subject.QueryAsync(criteria, null, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Property(nameof(result.Items)).Exactly(1).Items);
            Assert.That(result, Has.Property(nameof(result.Items))
                .One.Property(nameof(MaterializedMetadata.Title)).EqualTo("Action Movie"));
        }
    }

    #endregion
}
