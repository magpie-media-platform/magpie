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
public sealed class PruneOrphanedSubjectsJobTests
{
    private static readonly JobContext Context = new(
        ExecutionId: Guid.NewGuid(),
        ScheduleTriggerId: null,
        EventTriggerId: null,
        ManualTriggerUserId: null,
        ExecutionMode: JobExecutionMode.Queue,
        Parameters: new Dictionary<string, string>(),
        Event: null);

    private Mock<ISubjectRepository> _subjectRepository = null!;
    private Mock<IMetadataContainerRepository> _metadataContainerRepository = null!;
    private Mock<ITransactionManager> _transactionManager = null!;
    private Mock<IProgressReporter> _progress = null!;
    private PruneOrphanedSubjectsJob _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _subjectRepository = new Mock<ISubjectRepository>();
        _metadataContainerRepository = new Mock<IMetadataContainerRepository>();
        _transactionManager = new Mock<ITransactionManager>();
        _progress = new Mock<IProgressReporter>();

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

        _subject = new PruneOrphanedSubjectsJob(
            _subjectRepository.Object,
            _metadataContainerRepository.Object,
            _transactionManager.Object,
            _progress.Object,
            NullLogger<PruneOrphanedSubjectsJob>.Instance);
    }

    [TestCase(1, "The subject cleanup completed. Deleted subjects: 1.")]
    [TestCase(0, "The subject cleanup completed. Deleted subjects: 0.")]
    public async Task ExecuteAsync_ReportsPrunedCount_WhenDeleteCompletes(
        int deleted,
        string expectedMessage)
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
            .Setup(repository => repository.DeleteWhereAsync(
                It.IsAny<SubjectQuery>(),
                It.IsAny<CancellationToken>()))
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
                repository => repository.DeleteWhereAsync(
                    It.IsAny<SubjectQuery>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _metadataContainerRepository.Verify(
                repository => repository.RemoveContainersAsync(
                    It.IsAny<IReadOnlyList<Guid>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _progress.Verify(
                progress => progress.Report(1, "The subject cleanup completed. Deleted subjects: 0."),
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
            .Setup(repository => repository.DeleteWhereAsync(
                It.IsAny<SubjectQuery>(),
                It.IsAny<CancellationToken>()))
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
                progress => progress.Report(1, "The subject cleanup completed. Deleted subjects: 1."),
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
            .Setup(repository => repository.DeleteWhereAsync(
                It.IsAny<SubjectQuery>(),
                It.IsAny<CancellationToken>()))
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
            .Setup(repository => repository.DeleteWhereAsync(
                It.IsAny<SubjectQuery>(),
                It.IsAny<CancellationToken>()))
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
                progress => progress.Report(1, "The subject cleanup completed. Deleted subjects: 1."),
                Times.Once);
        }
    }

    [Test]
    public async Task ExecuteAsync_KeepsRepositoryCallsBounded_WhenManyCandidatesExist()
    {
        // Arrange
        var subjects = Enumerable.Range(0, 250)
            .Select(_ => new Subject
            {
                Id = Guid.NewGuid(),
                Kind = "person",
                MetadataContainerId = Guid.NewGuid()
            })
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
            .Setup(repository => repository.DeleteWhereAsync(
                It.IsAny<SubjectQuery>(),
                It.IsAny<CancellationToken>()))
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
                repository => repository.DeleteWhereAsync(
                    It.IsAny<SubjectQuery>(),
                    It.IsAny<CancellationToken>()),
                Times.Exactly(3));
        }
    }
}
