using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Subjects;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Subjects;

[TestFixture]
public sealed class SubjectServiceTests
{
    private Mock<ISubjectRepository> _subjectRepository = null!;
    private Mock<IMetadataContainerRepository> _metadataContainerRepository = null!;
    private Mock<ITransactionManager> _transactionManager = null!;
    private ISubjectService _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _subjectRepository = new Mock<ISubjectRepository>();
        _metadataContainerRepository = new Mock<IMetadataContainerRepository>();
        _transactionManager = new Mock<ITransactionManager>();
        _transactionManager
            .Setup(manager => manager.ExecuteWithTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                Func<CancellationToken, Task<bool>> operation,
                CancellationToken cancellationToken) => operation(cancellationToken));

        _subject = new SubjectService(
            _subjectRepository.Object,
            _metadataContainerRepository.Object,
            Mock.Of<IEventBus>(),
            _transactionManager.Object);
    }

    [Test]
    public async Task DeleteAsync_RemovesNarrowlyReadContainer_WhenSubjectExists()
    {
        // Arrange
        var subjectId = Guid.NewGuid();
        var metadataContainerId = Guid.NewGuid();
        _subjectRepository
            .Setup(repository => repository.GetByIdAsync(subjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subject
            {
                Id = subjectId,
                MetadataContainerId = metadataContainerId,
                Kind = "person"
            });
        _subjectRepository
            .Setup(repository => repository.DeleteAsync(subjectId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _metadataContainerRepository
            .Setup(repository => repository.RemoveContainersAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _subject.DeleteAsync(subjectId, null).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            _subjectRepository.Verify(
                repository => repository.GetByIdAsync(
                    subjectId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _metadataContainerRepository.Verify(
                repository => repository.RemoveContainersAsync(
                    It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == metadataContainerId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
