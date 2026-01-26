using System;
using System.Linq;
using System.Threading.Tasks;

using Magpie.Abstractions.Data;
using Magpie.Core.Data;
using Magpie.Data.Database;
using Magpie.Models;
using Magpie.Models.Filters;
using Magpie.Models.Filters.Metadata;
using Magpie.Models.Metadata;
using Magpie.Models.Result;
using Magpie.Test.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

using MetadataContainerEntity = Magpie.Data.Database.Entities.MetadataContainer;
using MetadataContributionEntity = Magpie.Data.Database.Entities.MetadataContribution;

namespace Magpie.Test.Core.Data;

[TestFixture]
public class MaterializedMetadataRepositoryTests
{
    private MagpieDbContext _context = null!;
    private IMaterializedMetadataRepository _repository = null!;

    [SetUp]
    public void SetUp()
    {
        _context = new(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var loggerFactory = LoggerFactory.Create(b => b.AddTestLogger());
        _repository = new MaterializedMetadataRepository(
            _context,
            loggerFactory.CreateLogger<MaterializedMetadataRepository>());
    }

    [TearDown]
    public void TearDown()
    {
        _context?.Dispose();
    }

    private MetadataContainerEntity CreateContainer()
    {
        var container = new MetadataContainerEntity
        {
            DateAdded = DateTimeOffset.UtcNow
        };
        _context.MetadataContainers.Add(container);
        _context.SaveChanges();
        return container;
    }

    [Test]
    public async Task MaterializeAsync_ReturnsNull_WhenContainerNotFound()
    {
        // Act
        var result = await _repository.MaterializeAsync(Guid.NewGuid());

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<MaterializedMetadata?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Null);
    }

    [Test]
    public async Task MaterializeAsync_ReturnsEmptyMetadata_WhenNoContributions()
    {
        // Arrange
        var container = CreateContainer();

        // Act
        var result = await _repository.MaterializeAsync(container.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<MaterializedMetadata?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MaterializedMetadata.Title)).Null);
    }

    [Test]
    public async Task MaterializeAsync_UsesFirstNonNullScalarByOrdinal()
    {
        // Arrange
        var container = CreateContainer();

        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Provider = "Provider1",
            Title = null,
            Overview = "First Overview",
            DateAdded = DateTimeOffset.UtcNow
        };
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 1,
            Provider = "Provider2",
            Title = "Second Title",
            Overview = "Second Overview",
            DateAdded = DateTimeOffset.UtcNow
        };
        _context.MetadataContributions.AddRange(contribution1, contribution2);
        _context.SaveChanges();

        // Act
        var result = await _repository.MaterializeAsync(container.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<MaterializedMetadata?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Not.Null
            .And.Property(nameof(SuccessResult<>.Value))
                .Property(nameof(MaterializedMetadata.Title)).EqualTo("Second Title")
            .And.Property(nameof(SuccessResult<>.Value))
                .Property(nameof(MaterializedMetadata.Overview)).EqualTo("First Overview"));
    }

    [Test]
    public async Task MaterializeAsync_MergesCollectionsInOrdinalOrder()
    {
        // Arrange
        var container = CreateContainer();

        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Provider = "Provider1",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.Genres.Add(new() { Ordinal = 0, GenreName = "Drama" });
        contribution1.Genres.Add(new() { Ordinal = 1, GenreName = "Action" });

        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 1,
            Provider = "Provider2",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.Genres.Add(new() { Ordinal = 0, GenreName = "Drama" });
        contribution2.Genres.Add(new() { Ordinal = 1, GenreName = "Comedy" });

        _context.MetadataContributions.AddRange(contribution1, contribution2);
        _context.SaveChanges();

        // Act
        var result = await _repository.MaterializeAsync(container.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<MaterializedMetadata?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MaterializedMetadata.Genres))
                .Count.EqualTo(3));
            Assert.That(result, Is.InstanceOf<SuccessResult<MaterializedMetadata?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MaterializedMetadata.Genres))
                .Some.Property(nameof(GenreMetadata.GenreName)).EqualTo("Drama"));
            Assert.That(result, Is.InstanceOf<SuccessResult<MaterializedMetadata?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MaterializedMetadata.Genres))
                .Some.Property(nameof(GenreMetadata.GenreName)).EqualTo("Action"));
            Assert.That(result, Is.InstanceOf<SuccessResult<MaterializedMetadata?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MaterializedMetadata.Genres))
                .Some.Property(nameof(GenreMetadata.GenreName)).EqualTo("Comedy"));
        }
    }

    [Test]
    public async Task MaterializeAsync_DeduplicatesGenresCaseInsensitively()
    {
        // Arrange
        var container = CreateContainer();

        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Provider = "Provider1",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.Genres.Add(new() { Ordinal = 0, GenreName = "drama" });

        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 1,
            Provider = "Provider2",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.Genres.Add(new() { Ordinal = 0, GenreName = "DRAMA" });

        _context.MetadataContributions.AddRange(contribution1, contribution2);
        _context.SaveChanges();

        // Act
        var result = await _repository.MaterializeAsync(container.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<MaterializedMetadata?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MaterializedMetadata.Genres))
                .Count.EqualTo(1));
            Assert.That(result, Is.InstanceOf<SuccessResult<MaterializedMetadata?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MaterializedMetadata.Genres))
                .Some.Property(nameof(GenreMetadata.GenreName)).EqualTo("drama"));
        }
    }

    [Test]
    public async Task MaterializeAsync_MergesCustomMetadata()
    {
        // Arrange
        var container = CreateContainer();

        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Provider = "Provider1",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.CustomMetadataValues.Add(new()
        {
            Key = "key1",
            Value = "value1"
        });

        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 1,
            Provider = "Provider2",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.CustomMetadataValues.Add(new()
        {
            Key = "key1",
            Value = "value1-override"
        });
        contribution2.CustomMetadataValues.Add(new()
        {
            Key = "key2",
            Value = "value2"
        });

        _context.MetadataContributions.AddRange(contribution1, contribution2);
        _context.SaveChanges();

        // Act
        var result = await _repository.MaterializeAsync(container.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<MaterializedMetadata?>>()
            .With.Property(nameof(SuccessResult<>.Value))
                .With.Property(nameof(MaterializedMetadata.CustomMetadata)).Count.EqualTo(2)
                .And.Property(nameof(MaterializedMetadata.CustomMetadata))
                    .With.ContainKey("key1").WithValue("value1")
                    .And.ContainKey("key2").WithValue("value2"));
    }

    [Test]
    public async Task QueryMaterializedAsync_ReturnsEmptyPage_WhenNoMatches()
    {
        // Arrange
        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.Equals, "NonExistent")]
            }
        };

        // Act
        var result = await _repository.QueryMaterializedAsync(criteria, null);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<PagedResult<MaterializedMetadataResult>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(PagedResult<>.TotalItems)).EqualTo(0));
    }

    [Test]
    public async Task QueryMaterializedAsync_FiltersAndPaginates()
    {
        // Arrange
        for (var i = 0; i < 5; i++)
        {
            var container = CreateContainer();
            var contribution = new MetadataContributionEntity
            {
                MetadataContainerId = container.Id,
                Ordinal = 0,
                Provider = "TestProvider",
                Title = $"Title {i}",
                DateAdded = DateTimeOffset.UtcNow
            };
            _context.MetadataContributions.Add(contribution);
        }
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.ContainsString, "Title")]
            }
        };

        // Act
        var result = await _repository.QueryMaterializedAsync(criteria, new PagingRequest(1, 2));

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<PagedResult<MaterializedMetadataResult>>>()
            .With.Property(nameof(SuccessResult<>.Value))
                .With.Property(nameof(PagedResult<>.TotalItems)).EqualTo(5)
                .And.Property(nameof(PagedResult<>.PageNumber)).EqualTo(1)
                .And.Property(nameof(PagedResult<>.PageSize)).EqualTo(2)
                .And.Property(nameof(PagedResult<>.Items)).With.Count.EqualTo(2));
    }

    [Test]
    public async Task MaterializeAsync_MergesExternalIds()
    {
        // Arrange
        var container = CreateContainer();

        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Provider = "Provider1",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.ExternalIds.Add(new()
        {
            Ordinal = 0,
            Value = "imdb-123",
            Attribution = "IMDB"
        });

        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 1,
            Provider = "Provider2",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.ExternalIds.Add(new()
        {
            Ordinal = 0,
            Value = "imdb-123",
            Attribution = "IMDB"
        });
        contribution2.ExternalIds.Add(new()
        {
            Ordinal = 1,
            Value = "tmdb-456",
            Attribution = "TMDB"
        });

        _context.MetadataContributions.AddRange(contribution1, contribution2);
        _context.SaveChanges();

        // Act
        var result = await _repository.MaterializeAsync(container.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<SuccessResult<MaterializedMetadata?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MaterializedMetadata.ExternalIds))
                .Count.EqualTo(2));
            Assert.That(result, Is.InstanceOf<SuccessResult<MaterializedMetadata?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MaterializedMetadata.ExternalIds))
                .Some.Property(nameof(ExternalIdMetadata.Value)).EqualTo("imdb-123"));
            Assert.That(result, Is.InstanceOf<SuccessResult<MaterializedMetadata?>>()
                .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MaterializedMetadata.ExternalIds))
                .Some.Property(nameof(ExternalIdMetadata.Value)).EqualTo("tmdb-456"));
        }
    }

    [Test]
    public async Task MaterializeAsync_MergesImages()
    {
        // Arrange
        var container = CreateContainer();

        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Provider = "Provider1",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.Images.Add(new()
        {
            Ordinal = 0,
            ImageType = "poster",
            Url = new("https://example.com/poster1.jpg")
        });

        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 1,
            Provider = "Provider2",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.Images.Add(new()
        {
            Ordinal = 0,
            ImageType = "poster",
            Url = new("https://example.com/poster1.jpg")
        });
        contribution2.Images.Add(new()
        {
            Ordinal = 1,
            ImageType = "backdrop",
            Url = new("https://example.com/backdrop.jpg")
        });

        _context.MetadataContributions.AddRange(contribution1, contribution2);
        _context.SaveChanges();

        // Act
        var result = await _repository.MaterializeAsync(container.Id);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<MaterializedMetadata?>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(MaterializedMetadata.Images))
                .With.Count.EqualTo(2));
    }

    [Test]
    public void GetMatchingContainerIds_ReturnsMatchingIds()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Alpha Movie",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Beta Show",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container3 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container3.Id,
            Ordinal = 0,
            Title = "Alpha Series",
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.StartsWithString, "Alpha")]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(2));
            Assert.That(ids, Does.Contain(container1.Id));
            Assert.That(ids, Does.Contain(container3.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_FiltersOnMaterializedTitle()
    {
        // Arrange
        var container = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Title = null,
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container.Id,
            Ordinal = 1,
            Title = "Second Title",
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.ContainsString, "Second")]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container.Id));
        }
    }

    [Test]
    public async Task QueryMaterializedAsync_FiltersOnMaterializedTitle()
    {
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Alpha",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = null,
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 1,
            Title = "Alpha Override",
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.ContainsString, "Alpha")]
            }
        };

        // Act
        var result = await _repository.QueryMaterializedAsync(criteria, null);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<PagedResult<MaterializedMetadataResult>>>()
            .With.Property(nameof(SuccessResult<>.Value)).Property(nameof(PagedResult<>.TotalItems)).EqualTo(2));
    }

    [Test]
    public void GetMatchingContainerIds_SupportsMultipleCriteria()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Alpha Movie",
            Language = "en",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Alpha Show",
            Language = "de",
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.StartsWithString, "Alpha")]
            },
            Language = new()
            {
                Any = [new(FieldOperator.Equals, "en")]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_FiltersOnGenreCollection_AnyOperator()
    {
        // Arrange
        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Action Movie",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.Genres.Add(new() { Ordinal = 0, GenreName = "Action" });
        contribution1.Genres.Add(new() { Ordinal = 1, GenreName = "Drama" });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Comedy Movie",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.Genres.Add(new() { Ordinal = 0, GenreName = "Comedy" });
        _context.MetadataContributions.Add(contribution2);

        var container3 = CreateContainer();
        var contribution3 = new MetadataContributionEntity
        {
            MetadataContainerId = container3.Id,
            Ordinal = 0,
            Title = "Action Comedy",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution3.Genres.Add(new() { Ordinal = 0, GenreName = "Action" });
        contribution3.Genres.Add(new() { Ordinal = 1, GenreName = "Comedy" });
        _context.MetadataContributions.Add(contribution3);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Genres = new()
            {
                Operator = CollectionOperator.Any,
                Criteria = [new()
                {
                    Genre = new()
                    {
                        Any = [new(FieldOperator.Equals, "Action")]
                    }
                }]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(2));
            Assert.That(ids, Does.Contain(container1.Id));
            Assert.That(ids, Does.Contain(container3.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_FiltersOnGenreCollection_NoneOperator()
    {
        // Arrange
        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Action Movie",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.Genres.Add(new() { Ordinal = 0, GenreName = "Action" });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Comedy Movie",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.Genres.Add(new() { Ordinal = 0, GenreName = "Comedy" });
        _context.MetadataContributions.Add(contribution2);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Genres = new()
            {
                Operator = CollectionOperator.None,
                Criteria = [new()
                {
                    Genre = new()
                    {
                        Any = [new(FieldOperator.Equals, "Action")]
                    }
                }]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container2.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_FiltersOnGenreCollection_AllOperator()
    {
        // Arrange
        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Action Movie",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.Genres.Add(new() { Ordinal = 0, GenreName = "Action" });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Action Comedy",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.Genres.Add(new() { Ordinal = 0, GenreName = "Action" });
        contribution2.Genres.Add(new() { Ordinal = 1, GenreName = "Comedy" });
        _context.MetadataContributions.Add(contribution2);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Genres = new()
            {
                Operator = CollectionOperator.All,
                Criteria =
                [
                    new GenreMetadataCriteria
                    {
                        Genre = new()
                        {
                            Any = [new(FieldOperator.Equals, "Action")]
                        }
                    },
                    new GenreMetadataCriteria
                    {
                        Genre = new()
                        {
                            Any = [new(FieldOperator.Equals, "Comedy")]
                        }
                    }
                ]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container2.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_FiltersOnExternalIds()
    {
        // Arrange
        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.ExternalIds.Add(new()
        {
            Ordinal = 0,
            Value = "tt1234567",
            Attribution = "IMDB"
        });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.ExternalIds.Add(new()
        {
            Ordinal = 0,
            Value = "12345",
            Attribution = "TMDB"
        });
        _context.MetadataContributions.Add(contribution2);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            ExternalIds = new()
            {
                Operator = CollectionOperator.Any,
                Criteria = [new()
                {
                    Attribution = new()
                    {
                        Any = [new(FieldOperator.Equals, "IMDB")]
                    }
                }]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_FiltersOnImages()
    {
        // Arrange
        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.Images.Add(new()
        {
            Ordinal = 0,
            ImageType = "poster",
            Url = new("https://example.com/poster1.jpg")
        });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.Images.Add(new()
        {
            Ordinal = 0,
            ImageType = "backdrop",
            Url = new("https://example.com/backdrop.jpg")
        });
        _context.MetadataContributions.Add(contribution2);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Images = new()
            {
                Operator = CollectionOperator.Any,
                Criteria = [new()
                {
                    ImageType = new()
                    {
                        Any = [new(FieldOperator.Equals, "poster")]
                    }
                }]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_FiltersOnAwards()
    {
        // Arrange
        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Oscar Movie",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.Awards.Add(new()
        {
            Ordinal = 0,
            AwardBody = "Academy Awards",
            Name = "Best Picture",
            Year = 2023
        });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "BAFTA Movie",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.Awards.Add(new()
        {
            Ordinal = 0,
            AwardBody = "BAFTA",
            Name = "Best Film",
            Year = 2023
        });
        _context.MetadataContributions.Add(contribution2);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Awards = new()
            {
                Operator = CollectionOperator.Any,
                Criteria = [new()
                {
                    AwardBody = new()
                    {
                        Any = [new(FieldOperator.Equals, "Academy Awards")]
                    }
                }]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_FiltersOnContentRatings()
    {
        // Arrange
        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "PG-13 Movie",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.ContentRatings.Add(new()
        {
            Ordinal = 0,
            RatingsBody = "MPAA",
            Value = "PG-13"
        });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "R Movie",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.ContentRatings.Add(new()
        {
            Ordinal = 0,
            RatingsBody = "MPAA",
            Value = "R"
        });
        _context.MetadataContributions.Add(contribution2);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            ContentRatings = new()
            {
                Operator = CollectionOperator.Any,
                Criteria = [new()
                {
                    Value = new()
                    {
                        Any = [new(FieldOperator.Equals, "PG-13")]
                    }
                }]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_CombinesScalarAndCollectionCriteria()
    {
        // Arrange
        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Alpha Action",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.Genres.Add(new() { Ordinal = 0, GenreName = "Action" });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Beta Action",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.Genres.Add(new() { Ordinal = 0, GenreName = "Action" });
        _context.MetadataContributions.Add(contribution2);

        var container3 = CreateContainer();
        var contribution3 = new MetadataContributionEntity
        {
            MetadataContainerId = container3.Id,
            Ordinal = 0,
            Title = "Alpha Comedy",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution3.Genres.Add(new() { Ordinal = 0, GenreName = "Comedy" });
        _context.MetadataContributions.Add(contribution3);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.StartsWithString, "Alpha")]
            },
            Genres = new()
            {
                Operator = CollectionOperator.Any,
                Criteria = [new()
                {
                    Genre = new()
                    {
                        Any = [new(FieldOperator.Equals, "Action")]
                    }
                }]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_GenreFromMultipleContributions()
    {
        // Arrange 
        var container = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 0,
            Title = "Movie Title",
            DateAdded = DateTimeOffset.UtcNow
        };
        _context.MetadataContributions.Add(contribution1);

        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container.Id,
            Ordinal = 1,
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.Genres.Add(new() { Ordinal = 0, GenreName = "Action" });
        _context.MetadataContributions.Add(contribution2);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Genres = new()
            {
                Operator = CollectionOperator.Any,
                Criteria = [new()
                {
                    Genre = new()
                    {
                        Any = [new(FieldOperator.Equals, "Action")]
                    }
                }]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container.Id));
        }
    }

    [Test]
    public async Task QueryMaterializedAsync_FiltersOnGenres()
    {
        // Arrange
        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Action Movie",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.Genres.Add(new() { Ordinal = 0, GenreName = "Action" });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Comedy Movie",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.Genres.Add(new() { Ordinal = 0, GenreName = "Comedy" });
        _context.MetadataContributions.Add(contribution2);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Genres = new()
            {
                Operator = CollectionOperator.Any,
                Criteria = [new()
                {
                    Genre = new()
                    {
                        Any = [new(FieldOperator.Equals, "Action")]
                    }
                }]
            }
        };

        // Act
        var result = await _repository.QueryMaterializedAsync(criteria, null);

        // Assert
        Assert.That(result, Is.InstanceOf<SuccessResult<PagedResult<MaterializedMetadataResult>>>()
            .With.Property(nameof(SuccessResult<>.Value))
                .With.Property(nameof(PagedResult<>.TotalItems)).EqualTo(1)
                .And.Property(nameof(PagedResult<>.Items))
                    .With.One.Property(nameof(MaterializedMetadataResult.Metadata))
                        .With.Property(nameof(MaterializedMetadata.Title)).EqualTo("Action Movie"));
    }

    [Test]
    public void GetMatchingContainerIds_FiltersOnReferences()
    {
        // Arrange
        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.References.Add(new()
        {
            Ordinal = 0,
            Kind = "Wikipedia",
            Url = new("https://en.wikipedia.org/wiki/Movie1")
        });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.References.Add(new()
        {
            Ordinal = 0,
            Kind = "IMDB",
            Url = new("https://imdb.com/title/tt1234567")
        });
        _context.MetadataContributions.Add(contribution2);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            References = new()
            {
                Operator = CollectionOperator.Any,
                Criteria = [new()
                {
                    Kind = new()
                    {
                        Any = [new(FieldOperator.Equals, "Wikipedia")]
                    }
                }]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_FiltersOnRelationships()
    {
        // Arrange
        var subject = new Magpie.Data.Database.Entities.Subject
        {
            Kind = "Person",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _context.Subjects.Add(subject);
        _context.SaveChanges();

        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.Relationships.Add(new()
        {
            Ordinal = 0,
            Relation = "Director",
            SubjectId = subject.Id
        });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.Relationships.Add(new()
        {
            Ordinal = 0,
            Relation = "Actor",
            SubjectId = subject.Id
        });
        _context.MetadataContributions.Add(contribution2);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Relationships = new()
            {
                Operator = CollectionOperator.Any,
                Criteria = [new()
                {
                    Relation = new()
                    {
                        Any = [new(FieldOperator.Equals, "Director")]
                    }
                }]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_FiltersOnPreviews()
    {
        // Arrange
        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.Previews.Add(new()
        {
            Ordinal = 0,
            PreviewType = "Trailer",
            Url = new("https://youtube.com/watch?v=abc123")
        });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.Previews.Add(new()
        {
            Ordinal = 0,
            PreviewType = "Clip",
            Url = new("https://youtube.com/watch?v=xyz789")
        });
        _context.MetadataContributions.Add(contribution2);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Previews = new()
            {
                Operator = CollectionOperator.Any,
                Criteria = [new()
                {
                    PreviewType = new()
                    {
                        Any = [new(FieldOperator.Equals, "Trailer")]
                    }
                }]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_FiltersOnAlternateTexts()
    {
        // Arrange
        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.AlternateTexts.Add(new()
        {
            Ordinal = 0,
            Type = "Tagline",
            Text = "An epic adventure"
        });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.AlternateTexts.Add(new()
        {
            Ordinal = 0,
            Type = "Synopsis",
            Text = "A brief summary"
        });
        _context.MetadataContributions.Add(contribution2);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            AlternateTexts = new()
            {
                Operator = CollectionOperator.Any,
                Criteria = [new()
                {
                    Type = new()
                    {
                        Any = [new(FieldOperator.Equals, "Tagline")]
                    }
                }]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_FiltersOnReferences_NoneOperator()
    {
        // Arrange
        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie With Wiki",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.References.Add(new()
        {
            Ordinal = 0,
            Kind = "Wikipedia",
            Url = new("https://en.wikipedia.org/wiki/Movie1")
        });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie Without Wiki",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.References.Add(new()
        {
            Ordinal = 0,
            Kind = "IMDB",
            Url = new("https://imdb.com/title/tt1234567")
        });
        _context.MetadataContributions.Add(contribution2);

        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            References = new()
            {
                Operator = CollectionOperator.None,
                Criteria = [new()
                {
                    Kind = new()
                    {
                        Any = [new(FieldOperator.Equals, "Wikipedia")]
                    }
                }]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container2.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_FiltersOnPreviews_AllOperator()
    {
        // Arrange
        var container1 = CreateContainer();
        var contribution1 = new MetadataContributionEntity
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie With Both",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution1.Previews.Add(new()
        {
            Ordinal = 0,
            PreviewType = "Trailer",
            Url = new("https://youtube.com/watch?v=abc123")
        });
        contribution1.Previews.Add(new()
        {
            Ordinal = 1,
            PreviewType = "Teaser",
            Url = new("https://youtube.com/watch?v=def456")
        });
        _context.MetadataContributions.Add(contribution1);

        var container2 = CreateContainer();
        var contribution2 = new MetadataContributionEntity
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie With Trailer Only",
            DateAdded = DateTimeOffset.UtcNow
        };
        contribution2.Previews.Add(new()
        {
            Ordinal = 0,
            PreviewType = "Trailer",
            Url = new("https://youtube.com/watch?v=xyz789")
        });
        _context.MetadataContributions.Add(contribution2);

        _context.SaveChanges();
        var criteria = new MetadataContributionCriteria
        {
            Previews = new()
            {
                Operator = CollectionOperator.All,
                Criteria =
                [
                    new PreviewMetadataCriteria
                    {
                        PreviewType = new()
                        {
                            Any = [new(FieldOperator.Equals, "Trailer")]
                        }
                    },
                    new PreviewMetadataCriteria
                    {
                        PreviewType = new()
                        {
                            Any = [new(FieldOperator.Equals, "Teaser")]
                        }
                    }
                ]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Has.Count.EqualTo(1));
            Assert.That(ids[0], Is.EqualTo(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_StringNotEquals()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Alpha",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Beta",
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.NotEquals, "Alpha")]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container2.Id));
            Assert.That(ids, Does.Not.Contain(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_StringIsNull()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = null,
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Has Title",
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.IsNull, null)]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container1.Id));
            Assert.That(ids, Does.Not.Contain(container2.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_StringIsNotNull()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = null,
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Has Title",
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.IsNotNull, null)]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container2.Id));
            Assert.That(ids, Does.Not.Contain(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_StringEndsWithString()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Alpha Movie",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Beta Show",
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.EndsWithString, "Movie")]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container1.Id));
            Assert.That(ids, Does.Not.Contain(container2.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_StringNotContainsString()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Alpha Movie",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Beta Show",
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.NotContainsString, "Alpha")]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container2.Id));
            Assert.That(ids, Does.Not.Contain(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_StringNotStartsWithString()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Alpha Movie",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Beta Show",
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.NotStartsWithString, "Alpha")]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container2.Id));
            Assert.That(ids, Does.Not.Contain(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_StringNotEndsWithString()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Alpha Movie",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Beta Show",
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.NotEndsWithString, "Movie")]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container2.Id));
            Assert.That(ids, Does.Not.Contain(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_StringIn()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Alpha",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Beta",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container3 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container3.Id,
            Ordinal = 0,
            Title = "Gamma",
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.In, Values: ["Alpha", "Gamma"])]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container1.Id));
            Assert.That(ids, Does.Contain(container3.Id));
            Assert.That(ids, Does.Not.Contain(container2.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_StringNotIn()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Alpha",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Beta",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container3 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container3.Id,
            Ordinal = 0,
            Title = "Gamma",
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any = [new(FieldOperator.NotIn, Values: ["Alpha", "Gamma"])]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container2.Id));
            Assert.That(ids, Does.Not.Contain(container1.Id));
            Assert.That(ids, Does.Not.Contain(container3.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_StringAnyOfMultipleCriteria()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Alpha Movie",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Beta Show",
            DateAdded = DateTimeOffset.UtcNow
        });

        var container3 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container3.Id,
            Ordinal = 0,
            Title = "Gamma Series",
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();
        var criteria = new MetadataContributionCriteria
        {
            Title = new()
            {
                Any =
                [
                    new(FieldOperator.StartsWithString, "Alpha"),
                    new(FieldOperator.EndsWithString, "Show")
                ]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container1.Id));
            Assert.That(ids, Does.Contain(container2.Id));
            Assert.That(ids, Does.Not.Contain(container3.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_DateTimeEquals()
    {
        // Arrange
        var targetDate = new DateTime(2024, 6, 15);

        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            PublishDate = targetDate,
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            PublishDate = new(2024, 7, 20),
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            PublishDate = new()
            {
                Any = [new(FieldOperator.Equals, targetDate)]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container1.Id));
            Assert.That(ids, Does.Not.Contain(container2.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_DateTimeNotEquals()
    {
        // Arrange
        var targetDate = new DateTime(2024, 6, 15);

        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            PublishDate = targetDate,
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            PublishDate = new(2024, 7, 20),
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            PublishDate = new()
            {
                Any = [new(FieldOperator.NotEquals, targetDate)]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container2.Id));
            Assert.That(ids, Does.Not.Contain(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_DateTimeIsNull()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            PublishDate = null,
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            PublishDate = new(2024, 7, 20),
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            PublishDate = new()
            {
                Any = [new(FieldOperator.IsNull, default)]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container1.Id));
            Assert.That(ids, Does.Not.Contain(container2.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_DateTimeIsNotNull()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            PublishDate = null,
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            PublishDate = new(2024, 7, 20),
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            PublishDate = new()
            {
                Any = [new(FieldOperator.IsNotNull, default)]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container2.Id));
            Assert.That(ids, Does.Not.Contain(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_DateTimeGreaterThan()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            PublishDate = new(2024, 1, 1),
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            PublishDate = new(2024, 12, 1),
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            PublishDate = new()
            {
                Any = [new(FieldOperator.GreaterThan, new(2024, 6, 1))]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container2.Id));
            Assert.That(ids, Does.Not.Contain(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_DateTimeGreaterThanOrEqual()
    {
        // Arrange
        var boundaryDate = new DateTime(2024, 6, 1);

        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            PublishDate = new(2024, 1, 1),
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            PublishDate = boundaryDate,
            DateAdded = DateTimeOffset.UtcNow
        });

        var container3 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container3.Id,
            Ordinal = 0,
            Title = "Movie 3",
            PublishDate = new(2024, 12, 1),
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            PublishDate = new()
            {
                Any = [new(FieldOperator.GreaterThanOrEqual, boundaryDate)]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container2.Id));
            Assert.That(ids, Does.Contain(container3.Id));
            Assert.That(ids, Does.Not.Contain(container1.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_DateTimeLessThan()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            PublishDate = new(2024, 1, 1),
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            PublishDate = new(2024, 12, 1),
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            PublishDate = new()
            {
                Any = [new(FieldOperator.LessThan, new(2024, 6, 1))]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container1.Id));
            Assert.That(ids, Does.Not.Contain(container2.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_DateTimeLessThanOrEqual()
    {
        // Arrange
        var boundaryDate = new DateTime(2024, 6, 1);

        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            PublishDate = new(2024, 1, 1),
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            PublishDate = boundaryDate,
            DateAdded = DateTimeOffset.UtcNow
        });

        var container3 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container3.Id,
            Ordinal = 0,
            Title = "Movie 3",
            PublishDate = new(2024, 12, 1),
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            PublishDate = new()
            {
                Any = [new(FieldOperator.LessThanOrEqual, boundaryDate)]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container1.Id));
            Assert.That(ids, Does.Contain(container2.Id));
            Assert.That(ids, Does.Not.Contain(container3.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_DateTimeBetween()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            PublishDate = new(2024, 1, 1),
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            PublishDate = new(2024, 6, 15),
            DateAdded = DateTimeOffset.UtcNow
        });

        var container3 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container3.Id,
            Ordinal = 0,
            Title = "Movie 3",
            PublishDate = new(2024, 12, 1),
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            PublishDate = new()
            {
                Any =
                [
                    new(FieldOperator.Between,Range: new(new (2024, 3, 1), new (2024, 9, 1)))
                ]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container2.Id));
            Assert.That(ids, Does.Not.Contain(container1.Id));
            Assert.That(ids, Does.Not.Contain(container3.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_DateTimeNotBetween()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            PublishDate = new(2024, 1, 1),
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            PublishDate = new(2024, 6, 15),
            DateAdded = DateTimeOffset.UtcNow
        });

        var container3 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container3.Id,
            Ordinal = 0,
            Title = "Movie 3",
            PublishDate = new(2024, 12, 1),
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            PublishDate = new()
            {
                Any =
                [
                    new(
                        FieldOperator.NotBetween,
                        Range: new(new (2024, 3, 1), new (2024, 9, 1)))
                ]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container1.Id));
            Assert.That(ids, Does.Contain(container3.Id));
            Assert.That(ids, Does.Not.Contain(container2.Id));
        }
    }

    [Test]
    public void GetMatchingContainerIds_DateTimeAnyOfMultipleCriteria()
    {
        // Arrange
        var container1 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container1.Id,
            Ordinal = 0,
            Title = "Movie 1",
            PublishDate = new(2024, 1, 1),
            DateAdded = DateTimeOffset.UtcNow
        });

        var container2 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container2.Id,
            Ordinal = 0,
            Title = "Movie 2",
            PublishDate = new(2024, 6, 15),
            DateAdded = DateTimeOffset.UtcNow
        });

        var container3 = CreateContainer();
        _context.MetadataContributions.Add(new()
        {
            MetadataContainerId = container3.Id,
            Ordinal = 0,
            Title = "Movie 3",
            PublishDate = new(2024, 12, 1),
            DateAdded = DateTimeOffset.UtcNow
        });
        _context.SaveChanges();

        var criteria = new MetadataContributionCriteria
        {
            PublishDate = new()
            {
                Any =
                [
                    new(FieldOperator.LessThan, new (2024, 2, 1)),
                    new(FieldOperator.GreaterThan, new (2024, 11, 1))
                ]
            }
        };

        // Act
        var ids = _repository.GetMatchingContainerIds(criteria).ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Does.Contain(container1.Id));
            Assert.That(ids, Does.Contain(container3.Id));
            Assert.That(ids, Does.Not.Contain(container2.Id));
        }
    }
}
