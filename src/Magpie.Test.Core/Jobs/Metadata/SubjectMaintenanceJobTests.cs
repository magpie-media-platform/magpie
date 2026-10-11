using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs;
using Magpie.Core.Jobs.Metadata;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Paging;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Metadata;

[TestFixture]
public sealed class SubjectMaintenanceJobTests
{
    private static readonly JobContext Context = new(
        ExecutionId: Guid.NewGuid(),
        ScheduleTriggerId: null,
        EventTriggerId: null,
        ManualTriggerUserId: null,
        ExecutionMode: JobExecutionMode.Queue,
        Parameters: new Dictionary<string, string>(),
        Event: null);

    private static readonly IReadOnlyList<IReadOnlyList<Subject>> DuplicateSubjects =
    [
        [
            new Subject
            {
                Id = new Guid("33471724-a502-4ea6-a033-1b3f55947630"),
                Kind = "person",
                CreatedAt = new DateTime(2026, 9, 16, 20, 10, 11, DateTimeKind.Utc),
                MetadataContainerId = new Guid("aaaaaaaa-0000-0000-0000-0000000000a2"),
                MetadataContainer = new MetadataContainer
                {
                    Id = new Guid("aaaaaaaa-0000-0000-0000-0000000000a2"),
                    Contributions =
                    {
                        new MetadataContribution
                        {
                            ProviderType = "tvmaze",
                            ExternalIds = { new ExternalId { Source = "tvmaze", Value = "64989" } }
                        }
                    }
                }
            },
            new Subject
            {
                Id = new Guid("1c884415-9443-425c-b6b3-799209391842"),
                Kind = "person",
                CreatedAt = new DateTime(2026, 8, 25, 9, 0, 0, DateTimeKind.Utc),
                MetadataContainerId = new Guid("aaaaaaaa-0000-0000-0000-0000000000a1"),
                MetadataContainer = new MetadataContainer
                {
                    Id = new Guid("aaaaaaaa-0000-0000-0000-0000000000a1"),
                    Contributions =
                    {
                        new MetadataContribution
                        {
                            ProviderType = "wikidata",
                            ExternalIds = { new ExternalId { Source = "tvmaze", Value = "64989" } }
                        }
                    }
                }
            }
        ]
    ];

    private Mock<ISubjectRepository> _subjectRepository = null!;
    private Mock<IMetadataContainerRepository> _metadataContainerRepository = null!;
    private Mock<IContentFilterRepository> _contentFilterRepository = null!;
    private Mock<ISubjectFilterRepository> _subjectFilterRepository = null!;
    private Mock<ITransactionManager> _transactionManager = null!;
    private Mock<IProgressReporter> _progress = null!;
    private SubjectMaintenanceJob _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _subjectRepository = new Mock<ISubjectRepository>();
        _metadataContainerRepository = new Mock<IMetadataContainerRepository>();
        _contentFilterRepository = new Mock<IContentFilterRepository>();
        _subjectFilterRepository = new Mock<ISubjectFilterRepository>();
        _transactionManager = new Mock<ITransactionManager>();
        _progress = new Mock<IProgressReporter>();

        _subjectRepository
            .Setup(repository => repository.FindDuplicatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _contentFilterRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _subjectFilterRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _subjectRepository
            .Setup(repository => repository.QueryAsync(
                It.IsAny<SubjectQuery>(),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject>());
        _transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<int>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));
        _transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));

        _subject = new SubjectMaintenanceJob(
            _subjectRepository.Object,
            _metadataContainerRepository.Object,
            _contentFilterRepository.Object,
            _subjectFilterRepository.Object,
            _transactionManager.Object,
            _progress.Object,
            NullLogger<SubjectMaintenanceJob>.Instance);
    }

    [Test]
    public async Task ExecuteAsync_MergesIntoTheOldestSubject_WhenSubjectsAreDuplicates()
    {
        // Arrange
        _subjectRepository
            .Setup(repository => repository.FindDuplicatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DuplicateSubjects);
        _subjectFilterRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubjectFilter
                {
                    Id = new Guid("cccccccc-0000-0000-0000-000000000002"),
                    Name = "Ken Burns",
                    Criteria = new SubjectFilterCriteria
                    {
                        Metadata = new()
                        {
                            And =
                            [
                                new MetadataCriteria
                                {
                                    Relationships = new()
                                    {
                                        Some =
                                        [
                                            [
                                                new RelationshipCriteria
                                                {
                                                    RelatedMetadataContainerId = new()
                                                    {
                                                        And =
                                                        [
                                                            new()
                                                            {
                                                                Operator = CriteriaValueOperator.Equals,
                                                                Value = new Guid("aaaaaaaa-0000-0000-0000-0000000000a2")
                                                            }
                                                        ]
                                                    }
                                                }
                                            ]
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                }
            ]);

        // Act
        await _subject.ExecuteAsync(Context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectRepository.Verify(
                repository => repository.MergeAsync(
                    new Guid("1c884415-9443-425c-b6b3-799209391842"),
                    It.Is<IReadOnlyCollection<Guid>>(ids =>
                        ids.Single() == new Guid("33471724-a502-4ea6-a033-1b3f55947630")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _subjectFilterRepository.Verify(
                repository => repository.UpdateAsync(
                    It.Is<SubjectFilter>(filter =>
                        filter.Criteria.Metadata.And.Single().Relationships.Some.Single().Single()
                            .RelatedMetadataContainerId.And.Single().Value ==
                        new Guid("aaaaaaaa-0000-0000-0000-0000000000a1")),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _subjectRepository.Verify(
                repository => repository.DeleteWhereAsync(
                    It.Is<SubjectQuery>(query =>
                        query.HasContentRelationships == null &&
                        query.Ids!.Single() == new Guid("33471724-a502-4ea6-a033-1b3f55947630")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _metadataContainerRepository.Verify(
                repository => repository.RemoveContainersAsync(
                    It.Is<IReadOnlyList<Guid>>(ids => ids.Single() == new Guid("aaaaaaaa-0000-0000-0000-0000000000a2")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _progress.Verify(
                progress => progress.Report(
                    1,
                    "The subject maintenance completed. Merged subjects: 1. Deleted subjects: 0."),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsTextThatLooksLikeAMergedContainerId_WhenFilterReferencesTheContainer()
    {
        // Arrange
        _subjectRepository
            .Setup(repository => repository.FindDuplicatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DuplicateSubjects);
        _subjectFilterRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new SubjectFilter
                {
                    Id = new Guid("cccccccc-0000-0000-0000-000000000003"),
                    Name = "Lookalike",
                    Criteria = new SubjectFilterCriteria
                    {
                        Metadata = new()
                        {
                            And =
                            [
                                new MetadataCriteria
                                {
                                    ContainerIds = [new Guid("aaaaaaaa-0000-0000-0000-0000000000a2")],
                                    Title = new()
                                    {
                                        And =
                                        [
                                            new()
                                            {
                                                Operator = CriteriaValueOperator.Equals,
                                                Value = "aaaaaaaa-0000-0000-0000-0000000000a2"
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                }
            ]);

        // Act
        await _subject.ExecuteAsync(Context).ConfigureAwait(false);

        // Assert
        _subjectFilterRepository.Verify(
            repository => repository.UpdateAsync(
                It.Is<SubjectFilter>(filter =>
                    filter.Criteria.Metadata.And.Single().ContainerIds.Single() ==
                        new Guid("aaaaaaaa-0000-0000-0000-0000000000a1") &&
                    filter.Criteria.Metadata.And.Single().Title.And.Single().Value ==
                        "aaaaaaaa-0000-0000-0000-0000000000a2"),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_RedirectsContainerIdsInTheSubtree_WhenContentFilterReferencesMergedContainer()
    {
        // Arrange
        _subjectRepository
            .Setup(repository => repository.FindDuplicatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DuplicateSubjects);
        _contentFilterRepository
            .Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ContentFilter
                {
                    Id = new Guid("cccccccc-0000-0000-0000-000000000004"),
                    Name = "Subtree",
                    Criteria = new ContentFilterCriteria
                    {
                        ContentHierarchyItem = new()
                        {
                            And =
                            [
                                new ContentHierarchyItemCriteria
                                {
                                    Subtree = new()
                                    {
                                        Some =
                                        [
                                            [
                                                new ContentHierarchyItemCriteria
                                                {
                                                    Metadata = new()
                                                    {
                                                        And =
                                                        [
                                                            new MetadataCriteria
                                                            {
                                                                ContainerIds =
                                                                [
                                                                    new Guid("aaaaaaaa-0000-0000-0000-0000000000a2")
                                                                ]
                                                            }
                                                        ]
                                                    }
                                                }
                                            ]
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                }
            ]);

        // Act
        await _subject.ExecuteAsync(Context).ConfigureAwait(false);

        // Assert
        _contentFilterRepository.Verify(
            repository => repository.UpdateAsync(
                It.Is<ContentFilter>(filter =>
                    filter.Criteria.ContentHierarchyItem.And.Single().Subtree.Some.Single().Single()
                        .Metadata.And.Single().ContainerIds.Single() ==
                    new Guid("aaaaaaaa-0000-0000-0000-0000000000a1")),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCase(1, "The subject maintenance completed. Merged subjects: 0. Deleted subjects: 1.")]
    [TestCase(0, "The subject maintenance completed. Merged subjects: 0. Deleted subjects: 0.")]
    public async Task ExecuteAsync_ReportsPrunedCount_WhenDeleteCompletes(int deleted, string expectedMessage)
    {
        // Arrange
        var subjectId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        _subjectRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasContentRelationships == false &&
                    query.HasUserContributions == false && query.HasTags == false && query.IsUserCreated == false),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject>
            {
                Items = [new() { Id = subjectId, Kind = "person", MetadataContainerId = containerId }]
            });
        _subjectRepository
            .Setup(repository => repository.DeleteWhereAsync(It.IsAny<SubjectQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(deleted);

        // Act
        await _subject.ExecuteAsync(Context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectRepository.Verify(
                repository => repository.DeleteWhereAsync(
                    It.Is<SubjectQuery>(query => query.Ids!.Single() == subjectId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _progress.Verify(progress => progress.Report(1, expectedMessage), Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_SkipsDelete_WhenCandidateNoLongerPrunable()
    {
        // Arrange
        var subjectId = Guid.NewGuid();
        _subjectRepository
            .SetupSequence(repository => repository.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasContentRelationships == false &&
                    query.HasUserContributions == false && query.HasTags == false && query.IsUserCreated == false),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject> { Items = [new() { Id = subjectId, Kind = "person" }] })
            .ReturnsAsync(new PagedList<Subject>());

        // Act
        await _subject.ExecuteAsync(Context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectRepository.Verify(
                repository => repository.DeleteWhereAsync(It.IsAny<SubjectQuery>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _metadataContainerRepository.Verify(
                repository => repository.RemoveContainersAsync(
                    It.IsAny<IReadOnlyList<Guid>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _progress.Verify(
                progress => progress.Report(
                    1,
                    "The subject maintenance completed. Merged subjects: 0. Deleted subjects: 0."),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_DeletesEveryStillPrunableSubjectAndItsContainer_WhenOneCandidateRecovered()
    {
        // Arrange
        var prunableId = Guid.NewGuid();
        var prunableContainerId = Guid.NewGuid();
        var recoveredId = Guid.NewGuid();
        _subjectRepository
            .SetupSequence(repository => repository.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasContentRelationships == false &&
                    query.HasUserContributions == false && query.HasTags == false && query.IsUserCreated == false),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject>
            {
                Items =
                [
                    new() { Id = prunableId, Kind = "person", MetadataContainerId = prunableContainerId },
                    new() { Id = recoveredId, Kind = "person", MetadataContainerId = Guid.NewGuid() }
                ]
            })
            .ReturnsAsync(new PagedList<Subject>
            {
                Items = [new() { Id = prunableId, Kind = "person", MetadataContainerId = prunableContainerId }]
            });
        _subjectRepository
            .Setup(repository => repository.DeleteWhereAsync(It.IsAny<SubjectQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _subject.ExecuteAsync(Context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectRepository.Verify(
                repository => repository.DeleteWhereAsync(
                    It.Is<SubjectQuery>(query => query.Ids!.Single() == prunableId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _metadataContainerRepository.Verify(
                repository => repository.RemoveContainersAsync(
                    It.Is<IReadOnlyList<Guid>>(ids => ids.Single() == prunableContainerId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _progress.Verify(
                progress => progress.Report(
                    1,
                    "The subject maintenance completed. Merged subjects: 0. Deleted subjects: 1."),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_DeletesWithTheOrphanPredicate_WhenABatchIsStillPrunable()
    {
        // Arrange
        var subjectId = Guid.NewGuid();
        _subjectRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasContentRelationships == false &&
                    query.HasUserContributions == false && query.HasTags == false && query.IsUserCreated == false),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject>
            {
                Items = [new() { Id = subjectId, Kind = "person", MetadataContainerId = Guid.NewGuid() }]
            });
        _subjectRepository
            .Setup(repository => repository.DeleteWhereAsync(It.IsAny<SubjectQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _subject.ExecuteAsync(Context).ConfigureAwait(false);

        // Assert
        _subjectRepository.Verify(
            repository => repository.DeleteWhereAsync(
                It.Is<SubjectQuery>(query =>
                    query.HasContentRelationships == false &&
                    query.HasUserContributions == false &&
                    query.HasTags == false &&
                    query.IsUserCreated == false &&
                    query.Ids!.Single() == subjectId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_KeepsTheContainerOfTheSurvivor_WhenTheDeleteRemovedFewerRows()
    {
        // Arrange
        var deletedId = Guid.NewGuid();
        var deletedContainerId = Guid.NewGuid();
        var survivorId = Guid.NewGuid();
        var survivorContainerId = Guid.NewGuid();
        _subjectRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasContentRelationships == false &&
                    query.HasUserContributions == false && query.HasTags == false && query.IsUserCreated == false),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject>
            {
                Items =
                [
                    new() { Id = deletedId, Kind = "person", MetadataContainerId = deletedContainerId },
                    new() { Id = survivorId, Kind = "person", MetadataContainerId = survivorContainerId }
                ]
            });
        _subjectRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasContentRelationships == null && query.Ids != null),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject>
            {
                Items = [new() { Id = survivorId, Kind = "person", MetadataContainerId = survivorContainerId }]
            });
        _subjectRepository
            .Setup(repository => repository.DeleteWhereAsync(It.IsAny<SubjectQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _subject.ExecuteAsync(Context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _metadataContainerRepository.Verify(
                repository => repository.RemoveContainersAsync(
                    It.Is<IReadOnlyList<Guid>>(ids => ids.Single() == deletedContainerId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _progress.Verify(
                progress => progress.Report(
                    1,
                    "The subject maintenance completed. Merged subjects: 0. Deleted subjects: 1."),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsRepositoryCallsBounded_WhenManyCandidatesExist()
    {
        // Arrange
        var subjects = Enumerable.Range(0, 250)
            .Select(_ => new Subject { Id = Guid.NewGuid(), Kind = "person", MetadataContainerId = Guid.NewGuid() })
            .ToList();
        _subjectRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasContentRelationships == false &&
                    query.HasUserContributions == false && query.HasTags == false && query.IsUserCreated == false),
                It.IsAny<PagingParameter>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject> { Items = subjects });
        _subjectRepository
            .Setup(repository => repository.DeleteWhereAsync(It.IsAny<SubjectQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(250);

        // Act
        await _subject.ExecuteAsync(Context).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectRepository.Verify(
                repository => repository.QueryAsync(
                    It.IsAny<SubjectQuery>(),
                    It.IsAny<PagingParameter>(),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Exactly(4));
            _subjectRepository.Verify(
                repository => repository.QueryAsync(
                    It.Is<SubjectQuery>(query => query.HasContentRelationships == true),
                    It.Is<PagingParameter>(paging => paging.PageSize == 1),
                    null,
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _subjectRepository.Verify(
                repository => repository.DeleteWhereAsync(It.IsAny<SubjectQuery>(), It.IsAny<CancellationToken>()),
                Times.Exactly(3));
        }
    }
}
