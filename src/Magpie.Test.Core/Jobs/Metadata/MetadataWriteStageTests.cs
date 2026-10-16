using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Jobs;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Config;
using Magpie.Core.Jobs.Metadata;
using Magpie.Core.Jobs.Runtime;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Jobs;
using Magpie.Sdk.Content;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Metadata;

[TestFixture]
[NonParallelizable]
public sealed class MetadataWriteStageTests
{
    private MagpieDbContext _context = null!;
    private SqliteConnection _template = null!;
    private ContentMetadataRefreshItemJob _subject = null!;
    private Guid _itemId;
    private JobContext _jobContext = null!;
    private Guid _containerId;
    private Guid _mediaOwnerId;
    private List<SubjectRelationshipDefinition> _definitions = null!;
    private List<string> _writeCommands = null!;
    private bool _writing;
    private List<MagpieEvent> _events = null!;
    private ActivityListener _listener = null!;
    private readonly QueueWriteFailure _failure = new();

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
        _writeCommands = [];
        _writing = false;
        _events = [];
        _failure.RemainingWrites = 0;
        _failure.Cancellation = null;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "magpie.core.content",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => _writing |= activity.OperationName == "magpie.metadata.write",
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "magpie.metadata.write")
                {
                    _writing = false;
                }
            }
        };
        ActivitySource.AddActivityListener(_listener);
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _template.BackupDatabase(connection);
        _context = new MagpieDbContext(new DbContextOptionsBuilder<MagpieDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .AddInterceptors(_failure)
            .LogTo(command =>
            {
                if (_writing)
                {
                    _writeCommands.Add(command);
                }
            }, [RelationalEventId.CommandExecuted]).Options);
        _context.RootFolders.Add(new RootFolder
        {
            Name = "TV",
            Path = "D:/library",
            ContentType = "tv",
            IsDefault = true
        });
        var transactions = new TransactionManager(_context, NullLogger<TransactionManager>.Instance);
        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(bus => bus.PublishAsync(It.IsAny<MagpieEvent>(), It.IsAny<CancellationToken>()))
            .Callback((MagpieEvent value, CancellationToken _) => _events.Add(value))
            .Returns(Task.CompletedTask);
        var events = eventBus.Object;
        var scopes = Mock.Of<IJobExecutionScopeFactory>();
        var audit = new AuditRecordRepository(_context, events, NullLogger<AuditRecordRepository>.Instance,
            transactions, scopes, TimeProvider.System);
        var subjects = new SubjectRepository(_context, audit, transactions, events,
            NullLogger<SubjectRepository>.Instance, TimeProvider.System);
        var containers = new MetadataContainerRepository(_context, audit, subjects, transactions,
            NullLogger<MetadataContainerRepository>.Instance, TimeProvider.System);
        var levels = new ContentHierarchyLevelDefinition[]
        {
            new() { Slug = "series", DisplayName = "Series", CustomMetadataFields = [] },
            new() { Slug = "episode", ParentSlug = "series", DisplayName = "Episode", CustomMetadataFields = [] }
        };
        var types = new Mock<IContentTypeRegistry>();
        types.Setup(registry => registry.GetDefinitionFor("tv")).Returns(new ContentTypeDefinition
        {
            Slug = "tv",
            DisplayName = "TV",
            LevelDefinitions = levels
        });
        types.Setup(registry => registry.GetChildLevels("tv", "series")).Returns([levels[1]]);
        var content = new ContentRepository(_context, transactions, events, TimeProvider.System,
            NullLogger<ContentRepository>.Instance, types.Object, Mock.Of<IFileSystem>(), audit);
        var hierarchy = new ContentHierarchyItemRepository(_context, audit, transactions, containers,
            types.Object, events, NullLogger<ContentHierarchyItemRepository>.Instance, TimeProvider.System, content);
        var executions = new JobExecutionRepository(
            _context,
            audit,
            transactions,
            events,
            NullLogger<JobExecutionRepository>.Instance,
            TimeProvider.System,
            new JobRegistry([], new JobRuntimeOptions(), NullLogger<JobRegistry>.Instance));
        var registry = new Mock<IJobRegistry>();
        registry.Setup(value => value.ListJobSpecs()).Returns([ContentMetadataRefreshItemJob.Spec]);
        var jobs = new JobManager(Mock.Of<IJobRuntime>(), registry.Object, executions,
            Mock.Of<IScheduleTriggerRepository>(), Mock.Of<IEventTriggerRepository>(),
            Mock.Of<IJobConcurrencyScheduler>(), scopes, TimeProvider.System,
            NullLogger<JobManager>.Instance, transactions);
        var kinds = new Mock<ISubjectKindRegistry>();
        kinds.SetupGet(value => value.Definitions).Returns(
        [
            new SubjectKindDefinition
            {
                Slug = "person", CustomMetadataFields = [],
                FallbackIdentityFields = MetadataField.Title | MetadataField.BirthDate
            }
        ]);
        var item = new ContentHierarchyItem
        {
            ContentType = "tv",
            Level = "series",
            Slug = "series",
            Name = "Series",
            Path = "D:/library/series",
            PendingMetadataRefresh = true,
            MetadataContainer = new MetadataContainer()
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
        _containerId = item.MetadataContainerId;
        var existingChild = new ContentHierarchyItem
        {
            ContentType = "tv",
            Level = "episode",
            Slug = "episode-0",
            Name = "Episode 0",
            ParentId = item.Id,
            AlignmentKey = "0",
            MetadataContainer = new MetadataContainer(),
            ContentItem = new ContentItem
            {
                MediaFiles =
                {
                    new MediaFile
                    {
                        FileKind = "video", FilePath = "D:/library/series/episode-0.mkv",
                        PathKey = "D:/library/series/episode-0.mkv"
                    }
                }
            }
        };
        _context.ContentHierarchyItems.Add(existingChild);
        foreach (var index in Enumerable.Range(1, 1000))
        {
            var stored = new Subject { Kind = "person", MetadataContainer = new MetadataContainer() };
            foreach (var provider in Enumerable.Range(0, 2))
            {
                stored.MetadataContainer.Contributions.Add(new MetadataContribution
                {
                    ProviderType = "stored-" + provider.ToString(CultureInfo.InvariantCulture),
                    Title = "Person " + index.ToString(CultureInfo.InvariantCulture),
                    BirthDate = new DateTime(1970, 1, 1),
                    ExternalIds =
                    {
                        new ExternalId
                        {
                            Source = "Person-ID", Value = index.ToString("D5", CultureInfo.InvariantCulture)
                        }
                    },
                    Images =
                    {
                        new Image { Type = "portrait", Url = new Uri("https://example.test/portrait/1") },
                        new Image { Type = "portrait", Url = new Uri("https://example.test/portrait/2") },
                        new Image { Type = "portrait", Url = new Uri("https://example.test/portrait/3") },
                        new Image { Type = "portrait", Url = new Uri("https://example.test/portrait/4") }
                    },
                    Awards = { new Award { Body = "Body", Name = "Award" } },
                    References =
                    {
                        new Reference { Kind = "homepage", Source = "source", Url = new Uri("https://example.test/") }
                    }
                });
            }

            _context.Subjects.Add(stored);
        }

        _context.SaveChanges();
        _mediaOwnerId = existingChild.ContentItem!.Id;
        _definitions = Enumerable.Range(1, 200).Select(index => new SubjectRelationshipDefinition
        {
            SubjectKind = "person",
            SubjectName = "Person " + index.ToString(CultureInfo.InvariantCulture),
            SubjectExternalIds = new Dictionary<string, string>
            {
                ["person"] = index.ToString(CultureInfo.InvariantCulture)
            },
            RelationshipKind = "cast",
            RelationshipQualifier = "original",
            RelationshipOrdinal = index,
            InitialSubjectContribution = new MetadataContribution
            {
                ProviderType = "provider",
                Title = "Person " + index.ToString(CultureInfo.InvariantCulture),
                BirthDate = new DateTime(1970, 1, 1)
            }
        }).ToList();
        var providers = new List<IContentHierarchyMetadataProvider>();
        foreach (var providerType in new[] { "provider", "other-provider" })
        {
            var provider = new Mock<IContentHierarchyMetadataRelationshipProvider>();
            provider.SetupGet(value => value.ProviderType).Returns(providerType);
            provider.SetupGet(value => value.SupportedContentTypes).Returns(["tv"]);
            provider.Setup(value => value.TryGetMetadataAsync(
                    It.IsAny<MetadataFetchRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new ContentHierarchyMetadataResult
                {
                    Contribution = new MetadataContribution
                    {
                        ProviderType = providerType,
                        Title = "Series",
                        Overview = "Verified metadata",
                        Images = { new Image { Type = "poster", Url = new Uri("https://example.test/poster") } }
                    },
                    Children = Enumerable.Range(0, 12).Select(index => new ChildHierarchyDefinition
                    {
                        Level = "episode",
                        Slug = "episode-" + index.ToString(CultureInfo.InvariantCulture),
                        Name = "Episode " + index.ToString(CultureInfo.InvariantCulture),
                        AlignmentKey = index.ToString(CultureInfo.InvariantCulture),
                        ExternalIds = new Dictionary<string, string>
                        {
                            ["episode"] = index.ToString(CultureInfo.InvariantCulture)
                        }
                    }).ToArray()
                });
            provider.Setup(value => value.GetRelationshipsAsync(
                    It.IsAny<MetadataRelationshipRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _definitions.Select(definition =>
                {
                    var result = definition with { InitialSubjectContribution = null };
                    if (definition.InitialSubjectContribution is { } seed)
                    {
                        result.InitialSubjectContribution = new MetadataContribution
                        {
                            ProviderType = seed.ProviderType,
                            Title = seed.Title,
                            BirthDate = seed.BirthDate
                        };
                        foreach (var id in seed.ExternalIds)
                        {
                            result.InitialSubjectContribution.ExternalIds.Add(new ExternalId
                            {
                                Source = id.Source,
                                Value = id.Value
                            });
                        }
                    }

                    return result;
                }).ToArray());
            providers.Add(provider.Object);
        }

        _subject = new ContentMetadataRefreshItemJob(providers, hierarchy, containers, subjects, events,
            types.Object, transactions, jobs, Mock.Of<ILibraryImportCandidateRepository>(), kinds.Object,
            TimeProvider.System, NullLogger<ContentMetadataRefreshItemJob>.Instance, Mock.Of<IProgressReporter>());
        _context.ChangeTracker.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        _listener.Dispose();
        _context.Dispose();
    }

    [Test]
    public async Task ExecuteAsync_PreservesPersistedResults_WhenLargeMetadataIsRepeatedAndRelationshipsChange()
    {
        // Arrange
        var phases = new[]
        {
            (Name: "first", Audits: 63), (Name: "unchanged", Audits: 64), (Name: "changed", Audits: 65)
        };

        // Act
        foreach (var (phase, audits) in phases)
        {
            _context.ChangeTracker.Clear();
            _writeCommands.Clear();
            await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);
            var writeSelects = _writeCommands.Count(command => command.Contains("SELECT", StringComparison.Ordinal));

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(writeSelects, Is.LessThan(1000));
                Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(1000));
                Assert.That(await _context.ContentHierarchyItems.CountAsync().ConfigureAwait(false), Is.EqualTo(13));
                Assert.That(await _context.MetadataContributions.CountAsync(contribution =>
                    contribution.MetadataContainerId == _containerId).ConfigureAwait(false), Is.EqualTo(2));
                Assert.That(await _context.MetadataContributions
                    .Where(contribution => contribution.MetadataContainerId == _containerId)
                    .Select(contribution => contribution.Overview).ToArrayAsync().ConfigureAwait(false),
                    Is.All.EqualTo("Verified metadata"));
                Assert.That(await _context.MediaFiles.Select(file => file.ContentItemId).SingleAsync()
                    .ConfigureAwait(false), Is.EqualTo(_mediaOwnerId));
                Assert.That(await _context.ContentHierarchyItems.Where(item => item.Id == _itemId)
                    .Select(item => item.Path).SingleAsync().ConfigureAwait(false), Is.EqualTo("D:/library/series"));
                Assert.That(await _context.Relationships.CountAsync().ConfigureAwait(false), Is.EqualTo(400));
                Assert.That(await _context.Relationships.Select(relationship => relationship.RelatedMetadataContainerId)
                    .Distinct().CountAsync().ConfigureAwait(false), Is.EqualTo(200));
                Assert.That(await _context.ExternalIds.CountAsync(id => id.Source == "Person-ID").ConfigureAwait(false),
                    Is.EqualTo(2000));
                Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.EqualTo(audits));
                Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(12));
                Assert.That(await _context.MetadataContributions.CountAsync().ConfigureAwait(false), Is.EqualTo(2026));
            }

            if (phase == "first")
            {
                await _context.Images.Where(image => image.MetadataContributionId == _context.MetadataContributions
                    .Where(contribution => contribution.MetadataContainerId == _containerId &&
                        contribution.ProviderType == "provider")
                    .Select(contribution => contribution.Id).First())
                    .ExecuteUpdateAsync(update => update.SetProperty(image => image.IsPrimary, true))
                    .ConfigureAwait(false);
            }

            if (phase == "unchanged")
            {
                Assert.That(await _context.Images.CountAsync(image => image.IsPrimary).ConfigureAwait(false),
                    Is.EqualTo(1));
                _definitions.RemoveAt(0);
                _definitions.Add(new SubjectRelationshipDefinition
                {
                    SubjectKind = "person",
                    SubjectName = "Person 201",
                    RelationshipKind = "cast",
                    RelationshipQualifier = "changed",
                    SubjectExternalIds = new Dictionary<string, string> { ["person"] = "201" }
                });
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Relationships.CountAsync(relationship => relationship.Qualifier == "changed")
                .ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await _context.Relationships.Where(relationship => relationship.RelatedMetadataContainerId ==
                _context.MetadataContributions.Where(contribution => contribution.Title == "Person 1")
                    .Select(contribution => contribution.MetadataContainerId).First())
                .CountAsync().ConfigureAwait(false), Is.Zero);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExecuteAsync_RollsBackAndRetriesWithoutDuplicates_WhenAQueueWriteFails(bool cancel)
    {
        // Arrange
        _definitions.Add(new SubjectRelationshipDefinition
        {
            SubjectKind = "person",
            SubjectName = "New person",
            RelationshipKind = "cast",
            SubjectExternalIds = new Dictionary<string, string> { ["person"] = "1001" },
            InitialSubjectContribution = new MetadataContribution
            {
                ProviderType = "provider",
                Title = "New person",
                ExternalIds = { new ExternalId { Source = "person", Value = "1001" } }
            }
        });
        using var cancellation = new CancellationTokenSource();
        _failure.Cancellation = cancel ? cancellation : null;
        _failure.RemainingWrites = 2;

        // Act
        var failure = Assert.CatchAsync<Exception>(() => _subject.ExecuteAsync(_jobContext, cancellation.Token));
        _context.ChangeTracker.Clear();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure, cancel ? Is.InstanceOf<OperationCanceledException>() :
                Is.TypeOf<InvalidOperationException>());
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(1000));
            Assert.That(await _context.MetadataContributions.CountAsync().ConfigureAwait(false), Is.EqualTo(2000));
            Assert.That(await _context.Relationships.CountAsync().ConfigureAwait(false), Is.Zero);
            Assert.That(await _context.ContentHierarchyItems.CountAsync().ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.Zero);
            Assert.That(await _context.AuditRecords.CountAsync().ConfigureAwait(false), Is.Zero);
            Assert.That(_events.Select(value => value.Name), Does.Not.Contain("metadata.refreshed"));
            Assert.That(_events.Select(value => value.Name), Does.Not.Contain("subject.created"));
            Assert.That(_events.Select(value => value.Name), Does.Not.Contain("job.execution.created"));
            Assert.That(await _context.MediaFiles.Select(file => file.ContentItemId).SingleAsync()
                .ConfigureAwait(false), Is.EqualTo(_mediaOwnerId));
        }

        // Act
        _failure.RemainingWrites = 0;
        _failure.Cancellation = null;
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);
        _context.ChangeTracker.Clear();
        await _subject.ExecuteAsync(_jobContext).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _context.Subjects.CountAsync().ConfigureAwait(false), Is.EqualTo(1001));
            Assert.That(await _context.MetadataContributions.CountAsync().ConfigureAwait(false), Is.EqualTo(2027));
            Assert.That(await _context.Relationships.CountAsync().ConfigureAwait(false), Is.EqualTo(402));
            Assert.That(await _context.ContentHierarchyItems.CountAsync().ConfigureAwait(false), Is.EqualTo(13));
            Assert.That(await _context.JobExecutions.CountAsync().ConfigureAwait(false), Is.EqualTo(13));
            Assert.That(_events.Count(value => value.Name == "subject.created"), Is.EqualTo(1));
            Assert.That(_events.Count(value => value.Name == "metadata.refreshed"), Is.EqualTo(2));
            Assert.That(await _context.MediaFiles.Select(file => file.ContentItemId).SingleAsync()
                .ConfigureAwait(false), Is.EqualTo(_mediaOwnerId));
        }
    }

    private sealed class QueueWriteFailure : SaveChangesInterceptor
    {
        internal int RemainingWrites { get; set; }

        internal CancellationTokenSource? Cancellation { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (RemainingWrites > 0 && eventData.Context!.ChangeTracker.Entries<JobExecution>()
                .Any(entry => entry.State == EntityState.Added) && --RemainingWrites == 0)
            {
                if (Cancellation is not null)
                {
                    await Cancellation.CancelAsync().ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("The fixture rejected the queue write.");
            }

            return result;
        }
    }
}
