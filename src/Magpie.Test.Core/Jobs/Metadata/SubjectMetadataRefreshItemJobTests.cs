using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Jobs.Metadata;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Content;
using Magpie.Sdk.Jobs;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Jobs.Metadata;

[TestFixture]
public sealed class SubjectMetadataRefreshItemJobTests
{
    private static readonly Guid SubjectId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ContainerId = new("33333333-3333-3333-3333-333333333333");

    private readonly IProgressReporter _progressReporter = Mock.Of<IProgressReporter>();

    private Mock<ISubjectRepository> _subjectRepository = null!;
    private Mock<IMetadataContainerRepository> _containerRepository = null!;
    private SubjectMetadataRefreshItemJob _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _subjectRepository = new Mock<ISubjectRepository>();
        _subjectRepository
            .Setup(repository => repository.GetByIdAsync(SubjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subject
            {
                Id = SubjectId,
                Kind = "person",
                MetadataContainerId = ContainerId,
                MetadataContainer = new MetadataContainer { Id = ContainerId }
            });

        _containerRepository = new Mock<IMetadataContainerRepository>();

        var transactionManager = new Mock<ITransactionManager>();
        transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
                operation(cancellationToken));

        var provider = new Mock<ISubjectMetadataProvider>();
        provider.Setup(value => value.SupportedSubjectKinds).Returns(["person"]);
        provider
            .Setup(value => value.TryGetMetadataAsync(
                It.IsAny<Subject>(),
                It.IsAny<IEnumerable<MetadataContribution>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataContribution?)null);

        _subject = new SubjectMetadataRefreshItemJob(
            [provider.Object],
            _subjectRepository.Object,
            _containerRepository.Object,
            transactionManager.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<SubjectMetadataRefreshItemJob>.Instance,
            _progressReporter);
    }

    [Test]
    public async Task RefreshAsync_StampsTheRefreshTimeWithoutRewritingTheSubject_WhenTheRefreshCompletes()
    {
        // Arrange
        // Act
        await _subject.RefreshAsync(SubjectId, null, null, _progressReporter).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            _subjectRepository.Verify(
                repository => repository.PatchAsync(
                    It.Is<SubjectRowPatch>(patch =>
                        patch.Id == SubjectId &&
                        patch.DateLastRefreshed!.Value == new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc)),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _subjectRepository.Verify(
                repository => repository.UpdateAsync(
                    It.IsAny<Subject>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            _containerRepository.Verify(
                repository => repository.PatchAsync(
                    It.Is<MetadataContainerPatch>(patch =>
                        patch.ContainerId == ContainerId &&
                        patch.DateLastRefreshed!.Value == new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc)),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
