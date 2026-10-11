using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core;
using Magpie.Core.Caching;
using Magpie.Core.Config;
using Magpie.Core.Content;
using Magpie.Core.Jobs.Metadata;
using Magpie.Core.Releases;
using Magpie.Core.Repositories;
using Magpie.Data;
using Magpie.Data.SQLite;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;
using Magpie.Models.MetadataSearch;
using Magpie.Models.Paging;
using Magpie.Models.Releases;
using Magpie.Sdk.Caching;
using Magpie.Sdk.Content;
using Magpie.Sdk.Integrations;
using Magpie.Sdk.Jobs;
using Magpie.Sdk.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Configuration;

[TestFixture]
public sealed class SettingsServiceConcurrencyTests
{
    private const string GroupName = "provider";

    private static readonly Guid FirstExtensionId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondExtensionId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ItemId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SubjectId = new("77777777-7777-7777-7777-777777777777");
    private static readonly Guid ContainerId = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid FirstIndexerId = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid SecondIndexerId = new("66666666-6666-6666-6666-666666666666");

    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private ISettingsService _settings = null!;
    private TaskCompletionSource<InterceptionResult<DbDataReader>> _release = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<MagpieDbContext>();
        MagpieDbContextFactory.ConfigureOptions(options, "Data Source=:memory:", [], []);
        using (var context = new MagpieDbContext(options.UseSqlite(_connection).Options))
        {
            context.Database.EnsureCreated();
            context.Extensions.AddRange(
                new Extension
                {
                    Id = FirstExtensionId,
                    PackageId = "First",
                    Version = "1.0.0",
                    Name = "First",
                    FilePath = "first.dll",
                    AssemblyName = "First"
                },
                new Extension
                {
                    Id = SecondExtensionId,
                    PackageId = "Second",
                    Version = "1.0.0",
                    Name = "Second",
                    FilePath = "second.dll",
                    AssemblyName = "Second"
                });
            var firstGroup = new SettingsGroup { Name = GroupName, ExtensionId = FirstExtensionId };
            firstGroup.Settings.Add(new Setting { Key = "contact", Value = "first@example.test" });
            var secondGroup = new SettingsGroup { Name = GroupName, ExtensionId = SecondExtensionId };
            secondGroup.Settings.Add(new Setting { Key = "contact", Value = "second@example.test" });
            context.SettingsGroups.AddRange(firstGroup, secondGroup);
            context.SaveChanges();
        }

        _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = new Mock<DbCommandInterceptor> { CallBase = true };
        commands
            .SetupSequence(interceptor => interceptor.ReaderExecutingAsync(
                It.IsAny<DbCommand>(),
                It.IsAny<CommandEventData>(),
                It.IsAny<InterceptionResult<DbDataReader>>(),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<InterceptionResult<DbDataReader>>(_release.Task));
        _services = new ServiceCollection()
            .AddDbContext<MagpieDbContext>(builder =>
            {
                MagpieDbContextFactory.ConfigureOptions(builder, "Data Source=:memory:", [], []);
                builder.UseSqlite(_connection).AddInterceptors(commands.Object);
            })
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(TimeProvider.System)
            .AddSingleton(Mock.Of<IEventBus>())
            .AddSingleton(Mock.Of<IAuditRecordRepository>())
            .AddSingleton(Mock.Of<IContentHierarchyItemRepository>())
            .AddScoped<ITransactionManager, TransactionManager>()
            .AddScoped<IExtensionRepository, ExtensionRepository>()
            .AddScoped<ISettingsRepository, SettingsRepository>()
            .AddScoped<ISettingsService, SettingsService>()
            .BuildServiceProvider();
        _scope = _services.CreateAsyncScope();
        _settings = _scope.ServiceProvider.GetRequiredService<ISettingsService>();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync().ConfigureAwait(false);
        await _services.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task GetExtensionGroupAsync_ReturnsEachGroup_WhenTwoReadsOverlap()
    {
        // Arrange
        var first = _settings.GetExtensionGroupAsync(FirstExtensionId, GroupName);
        var second = _settings.GetExtensionGroupAsync(SecondExtensionId, GroupName);

        // Act
        await Task.WhenAny(first, second).ConfigureAwait(false);
        _release.SetResult(default);
        var groups = await Task.WhenAll(first, second).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(groups[0]!.Group.Settings.Single().Value, Is.EqualTo("first@example.test"));
            Assert.That(groups[1]!.Group.Settings.Single().Value, Is.EqualTo("second@example.test"));
        }
    }

    [Test]
    public async Task SearchCandidatesAsync_ReturnsTheMatchOfEveryProvider_WhenProviderSettingsReadsOverlap()
    {
        // Arrange
        var first = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        first.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        first.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        first
            .Setup(provider => provider.ContinueSearchAsync(
                It.IsAny<string>(),
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                string _,
                ContentHierarchyItemCriteria _,
                int _,
                string _,
                object? _,
                CancellationToken token) =>
            {
                try
                {
                    await _settings.GetExtensionGroupAsync(FirstExtensionId, GroupName, token).ConfigureAwait(false);
                }
                finally
                {
                    _release.TrySetResult(default);
                }

                return new MetadataSearchBatch
                {
                    Page = new MetadataSearchPage
                    {
                        Supported = true,
                        Matches =
                        [
                            new MetadataSearchCandidate
                            {
                                ProviderType = "provider-a",
                                ExternalIdSource = "source-a",
                                ExternalId = "item-1",
                                ContentType = "type-a",
                                Level = "type-a",
                                Title = "Item 1"
                            }
                        ],
                        PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
                    }
                };
            });
        var second = new Mock<IContentHierarchyMetadataAdvancedSearcher>();
        second.SetupGet(provider => provider.ProviderType).Returns("provider-b");
        second.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        second
            .Setup(provider => provider.ContinueSearchAsync(
                It.IsAny<string>(),
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                string _,
                ContentHierarchyItemCriteria _,
                int _,
                string _,
                object? _,
                CancellationToken token) =>
            {
                try
                {
                    await _settings.GetExtensionGroupAsync(SecondExtensionId, GroupName, token).ConfigureAwait(false);
                }
                finally
                {
                    _release.TrySetResult(default);
                }

                return new MetadataSearchBatch
                {
                    Page = new MetadataSearchPage
                    {
                        Supported = true,
                        Matches =
                        [
                            new MetadataSearchCandidate
                            {
                                ProviderType = "provider-b",
                                ExternalIdSource = "source-b",
                                ExternalId = "item-2",
                                ContentType = "type-a",
                                Level = "type-a",
                                Title = "Item 2"
                            }
                        ],
                        PageInfo = new ProviderSearchPageInfo { RequestedPage = 1, HasMore = false }
                    }
                };
            });
        var hierarchyItems = new Mock<IContentHierarchyItemRepository>();
        hierarchyItems
            .Setup(repository => repository.FindByExternalIdentitiesAsync(
                It.IsAny<IReadOnlyCollection<(string, string, string, string)>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<(string, string, string, string), Guid>());
        var definition = new ContentTypeDefinition
        {
            Slug = "type-a",
            DisplayName = "Type A",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "type-a",
                    DisplayName = "Type A",
                    CustomMetadataFields = []
                }
            ]
        };
        var registry = new ContentTypeRegistry(
            [Mock.Of<IContentTypeDefinitionProvider>(provider => provider.Definition == definition)],
            NullLogger<ContentTypeRegistry>.Instance);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var cacheStore = new MemoryCacheStore(timeProvider);
        using var cacheFactory = new MemoryCacheFactory(cacheStore);
        var subject = new MetadataSearchService(
            [],
            [first.Object, second.Object],
            registry,
            hierarchyItems.Object,
            timeProvider,
            NullLogger<MetadataSearchService>.Instance,
            cacheFactory);
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new CriteriaSet<ContentHierarchyItemCriteria>
            {
                And =
                [
                    new ContentHierarchyItemCriteria
                    {
                        ContentType = new CriteriaSet<PropertyCriterion<string>>
                        {
                            And = [new PropertyCriterion<string> { Value = "type-a" }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = await subject.SearchCandidatesAsync(criteria, "type-a", page: 1).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Matches.Select(match => match.Candidate.Title),
                Is.EquivalentTo(["Item 1", "Item 2"]));
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public async Task ExecuteAsync_SavesTheContentContributionOfEveryProvider_WhenProviderSettingsReadsOverlap()
    {
        // Arrange
        var first = new Mock<IContentHierarchyMetadataProvider>();
        first.SetupGet(provider => provider.ProviderType).Returns("provider-a");
        first.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        first
            .Setup(provider => provider.TryGetMetadataAsync(
                It.IsAny<MetadataFetchRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (MetadataFetchRequest _, CancellationToken token) =>
            {
                try
                {
                    await _settings.GetExtensionGroupAsync(FirstExtensionId, GroupName, token).ConfigureAwait(false);
                }
                finally
                {
                    _release.TrySetResult(default);
                }

                return new ContentHierarchyMetadataResult
                {
                    Contribution = new MetadataContribution { ProviderType = "provider-a", Title = "Item 1" }
                };
            });
        var second = new Mock<IContentHierarchyMetadataProvider>();
        second.SetupGet(provider => provider.ProviderType).Returns("provider-b");
        second.SetupGet(provider => provider.SupportedContentTypes).Returns(["type-a"]);
        second
            .Setup(provider => provider.TryGetMetadataAsync(
                It.IsAny<MetadataFetchRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (MetadataFetchRequest _, CancellationToken token) =>
            {
                try
                {
                    await _settings.GetExtensionGroupAsync(SecondExtensionId, GroupName, token).ConfigureAwait(false);
                }
                finally
                {
                    _release.TrySetResult(default);
                }

                return new ContentHierarchyMetadataResult
                {
                    Contribution = new MetadataContribution { ProviderType = "provider-b", Title = "Item 1" }
                };
            });
        var items = new Mock<IContentHierarchyItemRepository> { CallBase = true };
        items
            .Setup(repository => repository.GetByIdAsync(ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = ItemId,
                MetadataContainerId = ContainerId,
                ContentType = "type-a",
                Level = "type-a",
                Slug = "item-1",
                Name = "Item 1",
                MetadataContainer = new MetadataContainer { Id = ContainerId }
            });
        items
            .Setup(repository => repository.GetLineageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        items
            .Setup(repository => repository.QueryAsync(
                It.IsAny<ContentHierarchyItemCriteria>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<ContentHierarchyItem>());
        var containers = new Mock<IMetadataContainerRepository>();
        var registry = new Mock<IContentTypeRegistry>();
        registry
            .Setup(value => value.GetDefinitionFor("type-a"))
            .Returns(new ContentTypeDefinition { Slug = "type-a", DisplayName = "Type A", LevelDefinitions = [] });
        var transactions = new Mock<ITransactionManager>();
        transactions
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken token) => operation(token));
        var subject = new ContentMetadataRefreshItemJob(
            [first.Object, second.Object],
            items.Object,
            containers.Object,
            Mock.Of<ISubjectRepository>(),
            Mock.Of<IEventBus>(),
            registry.Object,
            transactions.Object,
            Mock.Of<IJobManager>(),
            Mock.Of<ILibraryImportCandidateRepository>(),
            Mock.Of<ISubjectKindRegistry>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataRefreshItemJob>.Instance,
            Mock.Of<IProgressReporter>());

        // Act
        await subject.ExecuteAsync(new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["contentHierarchyItemId"] = ItemId.ToString("D") },
            Event: null)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            containers.Verify(
                repository => repository.AddContributionAsync(
                    ContainerId,
                    It.Is<MetadataContribution>(contribution => contribution.ProviderType == "provider-a"),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            containers.Verify(
                repository => repository.AddContributionAsync(
                    ContainerId,
                    It.Is<MetadataContribution>(contribution => contribution.ProviderType == "provider-b"),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_SavesTheSubjectContributionOfEveryProvider_WhenProviderSettingsReadsOverlap()
    {
        // Arrange
        var first = new Mock<ISubjectMetadataProvider>();
        first.SetupGet(provider => provider.SupportedSubjectKinds).Returns(["person"]);
        first
            .Setup(provider => provider.TryGetMetadataAsync(
                It.IsAny<Subject>(),
                It.IsAny<IEnumerable<MetadataContribution>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Subject _, IEnumerable<MetadataContribution> _, string _, CancellationToken token) =>
            {
                try
                {
                    await _settings.GetExtensionGroupAsync(FirstExtensionId, GroupName, token).ConfigureAwait(false);
                }
                finally
                {
                    _release.TrySetResult(default);
                }

                return new MetadataContribution { ProviderType = "provider-a", Title = "Person 1" };
            });
        var second = new Mock<ISubjectMetadataProvider>();
        second.SetupGet(provider => provider.SupportedSubjectKinds).Returns(["person"]);
        second
            .Setup(provider => provider.TryGetMetadataAsync(
                It.IsAny<Subject>(),
                It.IsAny<IEnumerable<MetadataContribution>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Subject _, IEnumerable<MetadataContribution> _, string _, CancellationToken token) =>
            {
                try
                {
                    await _settings.GetExtensionGroupAsync(SecondExtensionId, GroupName, token).ConfigureAwait(false);
                }
                finally
                {
                    _release.TrySetResult(default);
                }

                return new MetadataContribution { ProviderType = "provider-b", Title = "Person 1" };
            });
        var subjects = new Mock<ISubjectRepository>();
        subjects
            .Setup(repository => repository.GetByIdAsync(SubjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subject
            {
                Id = SubjectId,
                Kind = "person",
                MetadataContainerId = ContainerId,
                MetadataContainer = new MetadataContainer { Id = ContainerId }
            });
        var containers = new Mock<IMetadataContainerRepository>();
        var transactions = new Mock<ITransactionManager>();
        transactions
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken token) => operation(token));
        var subject = new SubjectMetadataRefreshItemJob(
            [first.Object, second.Object],
            subjects.Object,
            containers.Object,
            transactions.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<SubjectMetadataRefreshItemJob>.Instance,
            Mock.Of<IProgressReporter>());

        // Act
        await subject.ExecuteAsync(new JobContext(
            ExecutionId: Guid.NewGuid(),
            ScheduleTriggerId: null,
            EventTriggerId: null,
            ManualTriggerUserId: null,
            ExecutionMode: JobExecutionMode.Queue,
            Parameters: new Dictionary<string, string> { ["subjectId"] = SubjectId.ToString("D") },
            Event: null)).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            containers.Verify(
                repository => repository.AddContributionAsync(
                    ContainerId,
                    It.Is<MetadataContribution>(contribution => contribution.ProviderType == "provider-a"),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            containers.Verify(
                repository => repository.AddContributionAsync(
                    ContainerId,
                    It.Is<MetadataContribution>(contribution => contribution.ProviderType == "provider-b"),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_CompletesEveryConnection_WhenTrackerSessionSettingsReadsOverlap()
    {
        // Arrange
        var indexers = new Mock<IIndexerRepository>();
        indexers
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Indexer { Id = FirstIndexerId, ProviderType = "cardigann", Name = "Tracker A" },
                new Indexer { Id = SecondIndexerId, ProviderType = "cardigann", Name = "Tracker B" }
            ]);
        var connections = new Mock<IIndexerConnectionRepository>();
        connections
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = FirstIndexerId,
                    Name = "Tracker A",
                    Enabled = true
                },
                new IndexerConnection
                {
                    Id = Guid.NewGuid(),
                    IndexerId = SecondIndexerId,
                    Name = "Tracker B",
                    Enabled = true
                }
            ]);
        var integration = new Mock<IIndexerIntegration>();
        integration.SetupGet(value => value.ProviderType).Returns("cardigann");
        integration.SetupGet(value => value.SupportedContentTypes).Returns([]);
        integration
            .SetupGet(value => value.SearchFacets)
            .Returns([new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "q" }]);
        integration.SetupGet(value => value.SupportsKeywordSearch).Returns(true);
        integration
            .Setup(value => value.SearchAsync(
                It.IsAny<ReleaseSearchRequest>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (ReleaseSearchRequest _, IReadOnlyDictionary<string, string> _, CancellationToken token) =>
            {
                try
                {
                    await _settings.GetExtensionGroupAsync(FirstExtensionId, GroupName, token).ConfigureAwait(false);
                }
                finally
                {
                    _release.TrySetResult(default);
                }

                return [];
            });
        var subject = new ReleaseSearchQueryExecutor(
            indexers.Object,
            connections.Object,
            Mock.Of<IEventBus>(),
            [integration.Object],
            [],
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ReleaseSearchQueryExecutor>.Instance,
            Mock.Of<IReleaseEvaluationService>(),
            Mock.Of<ICacheFactory>(),
            [],
            new SearchOptions());

        // Act
        var phases = await subject.ExecuteAsync(
            [new ReleaseSearchPhase { Request = new ReleaseSearchRequest { Title = "Dune" }, ReleaseFacts = [] }],
            [],
            new HashSet<Guid> { FirstIndexerId, SecondIndexerId }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(phases.Single().ConnectionsAttempted, Is.EqualTo(2));
            Assert.That(phases.Single().ConnectionsSucceeded, Is.EqualTo(2));
        }
    }
}
