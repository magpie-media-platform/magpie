using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Content;
using Magpie.Models.Data;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Content;

[TestFixture]
public sealed class ContentMetadataServiceRelationshipTests
{
    [Test]
    public async Task GetRelatedMetadataAsync_ReturnsMaterializedMetadata_WhenRelationshipKindMatches()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var containerId = Guid.NewGuid();
        var relatedContainerId = Guid.NewGuid();
        var materializedMetadataRepository = new Mock<IMaterializedMetadataRepository>();
        var contentHierarchyItemRepository = new Mock<IContentHierarchyItemRepository>();
        contentHierarchyItemRepository
            .Setup(repository => repository.GetByIdAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentHierarchyItem
            {
                Id = itemId,
                MetadataContainerId = containerId,
                ContentType = "scene",
                Level = "scene",
                Slug = "court-ordered-smoothie",
                Name = "Court Ordered Smoothie",
            });
        materializedMetadataRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<MetadataCriteria>(criteria => criteria.ContainerIds.Count == 1 &&
                    criteria.ContainerIds.Contains(containerId)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MaterializedMetadata>
            {
                Items =
                [
                    new MaterializedMetadata
                    {
                        ContainerId = containerId,
                        Relationships =
                        [
                            new Relationship
                            {
                                Kind = "performer",
                                RelatedMetadataContainerId = relatedContainerId,
                            },
                        ],
                    },
                ],
            });
        materializedMetadataRepository
            .Setup(repository => repository.QueryAsync(
                It.Is<MetadataCriteria>(criteria => criteria.ContainerIds.Count == 1 &&
                    criteria.ContainerIds.Contains(relatedContainerId)),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<MaterializedMetadata>
            {
                Items =
                [
                    new MaterializedMetadata
                    {
                        ContainerId = relatedContainerId,
                        Title = "Court Ordered Smoothie",
                    },
                ],
            });
        var subject = new MetadataService(
            materializedMetadataRepository.Object,
            Mock.Of<IMetadataContainerRepository>(),
            Mock.Of<ISubjectRepository>(),
            contentHierarchyItemRepository.Object,
            Mock.Of<IEventBus>(),
            Mock.Of<ITransactionManager>());

        // Act
        var result = await subject
            .GetRelatedMetadataAsync(itemId, ["performer"])
            .ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].ContainerId, Is.EqualTo(relatedContainerId));
            Assert.That(result[0].Title, Is.EqualTo("Court Ordered Smoothie"));
        }
    }
}
