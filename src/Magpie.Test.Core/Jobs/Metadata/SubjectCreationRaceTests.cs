using System;
using System.Collections.Generic;
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
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Content;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Metadata;

[TestFixture]
public sealed class SubjectCreationRaceTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private ContentMetadataRefreshItemJob _subject = null!;

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
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var audit = new AuditRecordRepository(
            _context,
            Mock.Of<IEventBus>(),
            NullLogger<AuditRecordRepository>.Instance,
            transactions,
            Mock.Of<IJobExecutionScopeFactory>(),
            TimeProvider.System);
        var subjects = new SubjectRepository(
            _context,
            audit,
            transactions,
            Mock.Of<IEventBus>(),
            NullLogger<SubjectRepository>.Instance,
            TimeProvider.System);
        var staleSubjects = new Mock<ISubjectRepository>();
        staleSubjects
            .Setup(repository => repository.ListIdsByExternalIdentityAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyCollection<ExternalId>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        staleSubjects
            .Setup(repository => repository.ListIdentityDetailsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyCollection<Guid> ids, CancellationToken token) =>
                subjects.ListIdentityDetailsAsync(ids, token));
        staleSubjects
            .Setup(repository => repository.TryCreateAsync(
                It.IsAny<Subject>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Subject model, Guid? userId, CancellationToken token) =>
                subjects.TryCreateAsync(model, userId, token));
        var containers = new MetadataContainerRepository(
            _context,
            audit,
            subjects,
            transactions,
            NullLogger<MetadataContainerRepository>.Instance,
            TimeProvider.System);
        var snowfall = new ContentHierarchyItem
        {
            Id = new Guid("00000000-0000-0000-0000-000000000001"),
            ContentType = "movie",
            Level = "movie",
            Slug = "snowfall",
            Name = "Snowfall",
            MetadataContainer = new MetadataContainer { Id = new Guid("00000000-0000-0000-0000-0000000000a1") }
        };
        var bigMouth = new ContentHierarchyItem
        {
            Id = new Guid("00000000-0000-0000-0000-000000000002"),
            ContentType = "movie",
            Level = "movie",
            Slug = "big-mouth",
            Name = "Big Mouth",
            MetadataContainer = new MetadataContainer { Id = new Guid("00000000-0000-0000-0000-0000000000a2") }
        };
        _context.ContentHierarchyItems.AddRange(snowfall, bigMouth);
        _context.SaveChanges();
        var content = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        content.Setup(repository => repository.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        content.Setup(repository => repository.GetByIdAsync(snowfall.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snowfall);
        content.Setup(repository => repository.GetByIdAsync(bigMouth.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bigMouth);
        content.Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var contentTypes = new Mock<IContentTypeRegistry>();
        contentTypes.Setup(registry => registry.GetDefinitionFor("movie"))
            .Returns(new ContentTypeDefinition { Slug = "movie", DisplayName = "Movie", LevelDefinitions = [] });
        var kinds = new Mock<ISubjectKindRegistry>();
        kinds.SetupGet(registry => registry.Definitions)
            .Returns([new SubjectKindDefinition { Slug = "person", CustomMetadataFields = [] }]);
        var provider = new Mock<IContentHierarchyMetadataRelationshipProvider>();
        provider.SetupGet(value => value.ProviderType).Returns("tmdb");
        provider.SetupGet(value => value.SupportedContentTypes).Returns(["movie"]);
        provider.Setup(value => value.TryGetMetadataAsync(
                It.IsAny<MetadataFetchRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ContentHierarchyMetadataResult
            {
                Contribution = new MetadataContribution { ProviderType = "tmdb", Title = "Title" }
            });
        provider.Setup(value => value.GetRelationshipsAsync(
                It.IsAny<MetadataRelationshipRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            [
                new SubjectRelationshipDefinition
                {
                    SubjectKind = "person",
                    SubjectName = "Isaiah John",
                    RelationshipKind = "cast",
                    SubjectExternalIds = new Dictionary<string, string> { ["tmdb"] = "1855369" },
                    InitialSubjectContribution = new MetadataContribution
                    {
                        ProviderType = "tmdb",
                        Title = "Isaiah John",
                        ExternalIds = { new ExternalId { Source = "tmdb", Value = "1855369" } }
                    }
                }
            ]);
        _subject = new ContentMetadataRefreshItemJob(
            [provider.Object],
            content.Object,
            containers,
            staleSubjects.Object,
            Mock.Of<IEventBus>(),
            contentTypes.Object,
            transactions,
            Mock.Of<IJobManager>(),
            Mock.Of<ILibraryImportCandidateRepository>(),
            kinds.Object,
            TimeProvider.System,
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            Mock.Of<IProgressReporter>());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
    }

    [Test]
    public async Task ExecuteAsync_ReusesOneSubject_WhenAStaleLookupMissesTheSameProviderIdentity()
    {
        // Arrange
        var snowfall = new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "00000000-0000-0000-0000-000000000001"
            },
            Event: null);
        var bigMouth = snowfall with
        {
            Parameters = new Dictionary<string, string>
            {
                ["contentHierarchyItemId"] = "00000000-0000-0000-0000-000000000002"
            }
        };

        // Act
        await _subject.ExecuteAsync(snowfall).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        await _subject.ExecuteAsync(bigMouth).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(1));
            Assert.That(await _context.Relationships.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(
                await _context.Relationships.Select(relationship => relationship.RelatedMetadataContainerId)
                    .Distinct()
                    .CountAsync()
                    .ConfigureAwait(false),
                Is.EqualTo(1));
        }
    }
}
