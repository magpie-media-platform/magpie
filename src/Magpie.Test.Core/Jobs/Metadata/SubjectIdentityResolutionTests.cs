using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Jobs.Metadata;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Content;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Metadata;

[TestFixture]
public sealed class SubjectIdentityResolutionTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private ContentMetadataRefreshItemJob _subject = null!;
    private List<SubjectRelationshipDefinition> _definitions = null!;
    private SubjectRelationshipDefinition _incoming = null!;
    private Subject _first = null!;
    private Subject _second = null!;
    private Guid _itemId;
    private JobContext _jobContext = null!;
    private Mock<ILogger<ContentMetadataRefreshItemJob>> _logger = null!;
    private List<string> _queries = null!;
    private ISubjectRepository _subjectRepository = null!;

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
        _queries = [];
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true).LogTo(
                _queries.Add,
                [RelationalEventId.CommandExecuted]).Options);
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var audit = new AuditRecordRepository(_context, Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance, transactions, Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System);
        var subjects = new SubjectRepository(_context, audit, transactions, Mock.Of<IEventBus>(),
            NullLogger<SubjectRepository>.Instance, TimeProvider.System);
        _subjectRepository = subjects;
        var containers = new MetadataContainerRepository(_context, audit, subjects, transactions,
            NullLogger<MetadataContainerRepository>.Instance, TimeProvider.System);
        var item = new ContentHierarchyItem
        {
            ContentType = "movie",
            Level = "movie",
            Slug = "movie",
            Name = "Movie",
            MetadataContainer = new MetadataContainer { Id = Guid.NewGuid() }
        };
        _context.ContentHierarchyItems.Add(item);
        _context.SaveChanges();
        _itemId = item.Id;
        _jobContext = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["contentHierarchyItemId"] = _itemId.ToString("D") },
            Event: null);
        var content = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        content.Setup(repository => repository.GetLineageAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        content.Setup(repository => repository.GetByIdAsync(_itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        content.Setup(repository => repository.QueryAsync(It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(), It.IsAny<SortParameter?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var contentTypes = new Mock<IContentTypeRegistry>();
        contentTypes.Setup(registry => registry.GetDefinitionFor("movie"))
            .Returns(new ContentTypeDefinition { Slug = "movie", DisplayName = "Movie", LevelDefinitions = [] });
        var kinds = new Mock<ISubjectKindRegistry>();
        kinds.SetupGet(registry => registry.Definitions).Returns(
        [
            new SubjectKindDefinition
            {
                Slug = "person", CustomMetadataFields = [],
                FallbackIdentityFields = MetadataField.Title | MetadataField.BirthDate
            }
        ]);
        _incoming = new SubjectRelationshipDefinition
        {
            SubjectKind = "person",
            SubjectName = "Jane",
            RelationshipKind = "cast",
            RelationshipQualifier = "target",
            SubjectExternalIds = new Dictionary<string, string> { ["x"] = "1", ["y"] = "2" },
            InitialSubjectContribution = new MetadataContribution
            {
                ProviderType = "incoming",
                Title = "Jane",
                BirthDate = new DateTime(1970, 1, 1)
            }
        };
        _definitions = [_incoming];
        var provider = new Mock<IContentHierarchyMetadataRelationshipProvider>();
        provider.SetupGet(value => value.ProviderType).Returns("provider");
        provider.SetupGet(value => value.SupportedContentTypes).Returns(["movie"]);
        provider.Setup(value => value.TryGetMetadataAsync(
                It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = "provider", Title = "Movie" }
            });
        provider.Setup(value => value.GetRelationshipsAsync(
                It.IsAny<MetadataRelationshipRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_definitions);
        _logger = new Mock<ILogger<ContentMetadataRefreshItemJob>>();
        _subject = new ContentMetadataRefreshItemJob([provider.Object], content.Object, containers, subjects,
            Mock.Of<IEventBus>(), contentTypes.Object, transactions, Mock.Of<IJobManager>(),
            Mock.Of<ILibraryImportCandidateRepository>(), kinds.Object, TimeProvider.System, _logger.Object,
            Mock.Of<IProgressReporter>());
        _first = new Subject
        {
            Kind = "person",
            MetadataContainer = new MetadataContainer
            {
                Id = new Guid("11111111-1111-1111-1111-111111111111"),
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "first", Title = "Jane", BirthDate = new DateTime(1970, 1, 1),
                        ExternalIds = { new ExternalId { Source = "x", Value = "1" } }
                    }
                }
            }
        };
        _second = new Subject
        {
            Kind = "person",
            MetadataContainer = new MetadataContainer
            {
                Id = new Guid("22222222-2222-2222-2222-222222222222"),
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "second", Title = "Jane", BirthDate = new DateTime(1970, 1, 1),
                        ExternalIds = { new ExternalId { Source = "y", Value = "2" } }
                    }
                }
            }
        };
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [TestCase("x", "001", "x", "1")]
    [TestCase(" x ", "1", "x", "1")]
    [TestCase("X", "1", "x", "1")]
    [TestCase("x-id", "001", "x", "1")]
    [TestCase("x", "{00112233-4455-6677-8899-aabbccddeeff}", "X ID", "00112233445566778899AABBCCDDEEFF")]
    [TestCase("x", " Text ", "X", "text")]
    public async Task ExecuteAsync_ReportsAmbiguity_WhenStoredIdentifierFormattingIsEquivalent(
        string storedSource, string storedValue, string incomingSource, string incomingValue)
    {
        // Arrange
        _first.MetadataContainer!.Contributions.Single().ExternalIds.Single().Source = storedSource;
        _first.MetadataContainer.Contributions.Single().ExternalIds.Single().Value = storedValue;
        _incoming.SubjectExternalIds = new Dictionary<string, string>
        {
            [incomingSource] = incomingValue,
            ["y"] = "2"
        };
        _context.Subjects.AddRange(_first, _second);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Relationships.CountAsync().ConfigureAwait(false), Is.Zero);
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            _logger.Verify(logger => logger.Log(LogLevel.Warning, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("ambiguous", StringComparison.Ordinal)),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_UsesOneIdentityProjection_WhenSeveralCreditsHaveTheSameKind()
    {
        // Arrange
        _context.Subjects.AddRange(_first, _second);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _definitions.Add(_incoming with { RelationshipQualifier = "another" });
        _queries.Clear();

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_queries.Count(query =>
                query.Contains("Subject identity candidates", StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(await _context.Relationships.CountAsync().ConfigureAwait(false), Is.Zero);
        }
    }

    [Test]
    public async Task ExecuteAsync_PreservesVerifiedIdentifierText_WhenUniqueNormalizedCandidateMatches()
    {
        // Arrange
        _first.MetadataContainer!.Contributions.Single().ExternalIds.Single().Source = "X-ID";
        _first.MetadataContainer.Contributions.Single().ExternalIds.Single().Value = "001";
        _context.Subjects.Add(_first);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Relationships.Select(relationship => relationship.RelatedMetadataContainerId)
                .SingleAsync().ConfigureAwait(false), Is.EqualTo(new Guid("11111111-1111-1111-1111-111111111111")));
            Assert.That(await _context.ExternalIds.Select(id => id.Source).SingleAsync().ConfigureAwait(false),
                Is.EqualTo("X-ID"));
            Assert.That(await _context.ExternalIds.Select(id => id.Value).SingleAsync().ConfigureAwait(false),
                Is.EqualTo("001"));
        }
    }

    [Test]
    public async Task ExecuteAsync_DoesNotExpandStoredFallbackDiscovery_WhenAnotherCreditLoadsTheCandidate()
    {
        // Arrange
        _definitions.Insert(0, _incoming with
        {
            SubjectExternalIds = new Dictionary<string, string>(),
            RelationshipQualifier = "without-id"
        });
        _incoming.SubjectExternalIds = new Dictionary<string, string> { ["x"] = "1" };
        _context.Subjects.Add(_first);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Relationships.Where(relationship => relationship.Qualifier == "without-id")
                .Select(relationship => relationship.RelatedMetadataContainerId).SingleAsync().ConfigureAwait(false),
                Is.Not.EqualTo(new Guid("11111111-1111-1111-1111-111111111111")));
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
        }
    }

    [TestCase("wrong", new[] { "22222222-2222-2222-2222-222222222222" })]
    [TestCase("2", new string[0])]
    public async Task ExecuteAsync_ExaminesAllStoredCandidates_WhenIdentityEvidenceCrossesAPageBoundary(
        string firstValue, string[] expectedContainers)
    {
        // Arrange
        _first.MetadataContainer!.Contributions.Single().Title = "A first";
        _first.MetadataContainer.Contributions.Single().ExternalIds.Add(
            new ExternalId { Source = "y", Value = firstValue });
        _second.MetadataContainer!.Contributions.Single().Title = "Z last";
        _second.MetadataContainer.Contributions.Single().ExternalIds.Clear();
        _second.MetadataContainer.Contributions.Single().ExternalIds.Add(new ExternalId { Source = "x", Value = "1" });
        _context.Subjects.AddRange(_first, _second);
        _context.Subjects.AddRange(Enumerable.Range(0, 499).Select(index => new Subject
        {
            Kind = "person",
            MetadataContainer = new MetadataContainer
            {
                Contributions =
                {
                    new MetadataContribution
                    {
                        ProviderType = "other", Title = "B " + index.ToString("D3", CultureInfo.InvariantCulture),
                        ExternalIds =
                        {
                            new ExternalId { Source = "x", Value = "1" },
                            new ExternalId { Source = "y", Value = "wrong" }
                        }
                    }
                }
            }
        }));
        await _context.SaveChangesAsync().ConfigureAwait(false);
        var firstPage = await _subjectRepository.QueryAsync(new SubjectQuery
        {
            Kind = "person",
            ExternalId = new()
            {
                Source = new() { And = [new() { Value = "x" }] },
                Value = new() { And = [new() { Value = "1" }] }
            }
        }, new() { PageNumber = 1, PageSize = 500 }, null).ConfigureAwait(false);
        _queries.Clear();

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstPage.TotalItems, Is.EqualTo(501));
            Assert.That(firstPage.Items.Select(item => item.Id), Does.Not.Contain(_second.Id));
            Assert.That(await _context.Relationships.Select(item => item.RelatedMetadataContainerId.ToString())
                .ToArrayAsync().ConfigureAwait(false), Is.EqualTo(expectedContainers));
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(501));
            Assert.That(_queries.Count(query =>
                query.Contains("Subject identity candidates", StringComparison.Ordinal)),
                Is.EqualTo(1));
        }
    }

    [TestCase("A", "Z")]
    [TestCase("Z", "A")]
    public async Task ExecuteAsync_SelectsUniqueCompatibleSubject_WhenAnotherCandidateConflicts(
        string firstTitle,
        string secondTitle)
    {
        // Arrange
        _first.MetadataContainer!.Contributions.Single().Title = firstTitle;
        _second.MetadataContainer!.Contributions.Single().Title = secondTitle;
        _second.MetadataContainer.Contributions.Single().ExternalIds.Add(new ExternalId { Source = "x", Value = "1" });
        _first.MetadataContainer.Contributions.Add(new MetadataContribution
        {
            ProviderType = "other",
            ExternalIds = { new ExternalId { Source = "y", Value = "wrong" } }
        });
        _context.Subjects.AddRange(_first, _second);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Relationships.Select(r => r.RelatedMetadataContainerId).SingleAsync()
                .ConfigureAwait(false), Is.EqualTo(new Guid("22222222-2222-2222-2222-222222222222")));
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await _context.ExternalIds.CountAsync(id => id.Value == "wrong").ConfigureAwait(false),
                Is.EqualTo(1));
        }
    }

    [TestCase("x", "1", "y", "2", "A", "Z")]
    [TestCase("y", "2", "x", "1", "Z", "A")]
    public async Task ExecuteAsync_ReportsAmbiguityWithoutAssociation_WhenStoredMatchesHaveDisjointIds(
        string firstSource,
        string firstValue,
        string secondSource,
        string secondValue,
        string firstTitle,
        string secondTitle)
    {
        // Arrange
        _incoming.SubjectExternalIds = new Dictionary<string, string>
        {
            [firstSource] = firstValue,
            [secondSource] = secondValue
        };
        _first.MetadataContainer!.Contributions.Single().Title = firstTitle;
        _second.MetadataContainer!.Contributions.Single().Title = secondTitle;
        _context.Subjects.AddRange(_first, _second);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Relationships.CountAsync().ConfigureAwait(false), Is.Zero);
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            _logger.Verify(logger => logger.Log(LogLevel.Warning, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("ambiguous", StringComparison.Ordinal)),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_LeavesAssociationUnresolved_WhenEveryExternalCandidateConflicts()
    {
        // Arrange
        _first.MetadataContainer!.Contributions.Single().ExternalIds.Add(
            new ExternalId { Source = "y", Value = "wrong" });
        _context.Subjects.Add(_first);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Relationships.CountAsync().ConfigureAwait(false), Is.Zero);
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ExecuteAsync_ReusesStoredSubject_WhenIdentifierFormattingMustRemainIntactForLookup()
    {
        // Arrange
        _first.MetadataContainer!.Contributions.Single().ExternalIds.Single().Source = "provider-x";
        _first.MetadataContainer.Contributions.Single().ExternalIds.Single().Value = "001";
        _incoming.SubjectExternalIds = new Dictionary<string, string> { ["provider-x"] = "001" };
        _context.Subjects.Add(_first);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await _context.Relationships.Select(r => r.RelatedMetadataContainerId).SingleAsync()
                .ConfigureAwait(false), Is.EqualTo(new Guid("11111111-1111-1111-1111-111111111111")));
        }
    }

    [Test]
    public async Task ExecuteAsync_LeavesAssociationUnresolved_WhenOneIdentifierMatchesTwoStoredSubjects()
    {
        // Arrange
        _second.MetadataContainer!.Contributions.Single().ExternalIds.Clear();
        _second.MetadataContainer.Contributions.Single().ExternalIds.Add(new ExternalId { Source = "x", Value = "1" });
        _context.Subjects.AddRange(_first, _second);
        await _context.SaveChangesAsync().ConfigureAwait(false);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Relationships.CountAsync().ConfigureAwait(false), Is.Zero);
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_LeavesAssociationUnresolved_WhenInRunCandidatesHaveDisjointIds(bool reverse)
    {
        // Arrange
        var first = _incoming with
        {
            RelationshipQualifier = "first",
            SubjectExternalIds = new Dictionary<string, string> { ["x"] = "1" },
            InitialSubjectContribution = null
        };
        var second = _incoming with
        {
            RelationshipQualifier = "second",
            SubjectExternalIds = new Dictionary<string, string> { ["y"] = "2" },
            InitialSubjectContribution = null
        };
        _definitions.InsertRange(0, reverse ? [second, first] : [first, second]);

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Relationships.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await _context.Relationships.CountAsync(r => r.Qualifier == "target").ConfigureAwait(false),
                Is.Zero);
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task ExecuteAsync_RejectsPartialMatch_WhenInRunSubjectHasAConflictingSharedIdentifier()
    {
        // Arrange
        _definitions.Insert(0, _incoming with
        {
            RelationshipQualifier = "first",
            SubjectExternalIds = new Dictionary<string, string> { ["x"] = "1", ["y"] = "wrong" },
            InitialSubjectContribution = null
        });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Relationships.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await _context.Relationships.CountAsync(r => r.Qualifier == "target").ConfigureAwait(false),
                Is.Zero);
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ExecuteAsync_LeavesAssociationUnresolved_WhenInRunMatchCompetesWithStoredMatch()
    {
        // Arrange
        _context.Subjects.Add(_first);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _definitions.Insert(0, _incoming with
        {
            RelationshipQualifier = "first",
            SubjectExternalIds = new Dictionary<string, string> { ["y"] = "2" },
            InitialSubjectContribution = null
        });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await _context.Relationships.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await _context.Relationships.CountAsync(r => r.Qualifier == "target").ConfigureAwait(false),
                Is.Zero);
        }
    }

    [Test]
    public async Task ExecuteAsync_RejectsFallbackCandidate_WhenEarlierLookupDidNotUseItsConflictingStoredId()
    {
        // Arrange
        _first.MetadataContainer!.Contributions.Single().ExternalIds.Add(
            new ExternalId { Source = "y", Value = "wrong" });
        _context.Subjects.Add(_first);
        await _context.SaveChangesAsync().ConfigureAwait(false);
        _definitions.Insert(0, _incoming with
        {
            RelationshipQualifier = "first",
            SubjectExternalIds = new Dictionary<string, string> { ["x"] = "1" },
            InitialSubjectContribution = null
        });
        _incoming.SubjectExternalIds = new Dictionary<string, string> { ["y"] = "2" };

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await _context.Relationships.Where(r => r.Qualifier == "target")
                .Select(r => r.RelatedMetadataContainerId).SingleAsync().ConfigureAwait(false),
                Is.Not.EqualTo(new Guid("11111111-1111-1111-1111-111111111111")));
            Assert.That(await _context.ExternalIds.CountAsync(id => id.Value == "wrong").ConfigureAwait(false),
                Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ExecuteAsync_LeavesAssociationUnresolved_WhenSeedAndIncomingIdsConflictAfterNormalization()
    {
        // Arrange
        _incoming.InitialSubjectContribution!.ExternalIds.Add(new ExternalId { Source = " X ", Value = "wrong" });

        // Act
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.Zero);
            Assert.That(await _context.Relationships.CountAsync().ConfigureAwait(false), Is.Zero);
        }
    }
}
