using System;
using System.Linq;

using Magpie.Models;
using Magpie.Sdk.Events;

using NUnit.Framework;

namespace Magpie.Test.Core.Events;

[TestFixture]
public sealed class ContentEventImpactTests
{
    private static readonly Guid HierarchyItemId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ItemId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ContainerId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ParentId = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid RootId = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OldParentId = new("66666666-6666-6666-6666-666666666666");
    private static readonly Guid OldRootId = new("77777777-7777-7777-7777-777777777777");
    private static readonly Guid AddedContainerId = new("88888888-8888-8888-8888-888888888888");
    private static readonly Guid RemovedContainerId = new("99999999-9999-9999-9999-999999999999");

    [Test]
    public void ToContext_WritesEveryRoutingIdentity_WhenImpactIsFullyPopulated()
    {
        // Arrange
        var impact = new ContentEventImpact
        {
            HierarchyItemId = HierarchyItemId,
            ItemId = ItemId,
            MetadataContainerId = ContainerId,
            ParentId = ParentId,
            AncestorIds = [ParentId, RootId],
            RootId = RootId,
            ContentType = "movies",
            Level = "movie",
            TopLevel = false,
            RootFolderId = OldRootId,
            QualityTierListId = OldParentId,
            Monitoring = true,
            Fulfillment = "Missing",
            WishlistId = AddedContainerId
        };

        // Act
        var context = impact.ToContext();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context["content.hierarchyItemId"], Is.EqualTo("11111111-1111-1111-1111-111111111111"));
            Assert.That(context["content.itemId"], Is.EqualTo("22222222-2222-2222-2222-222222222222"));
            Assert.That(
                context["content.impact.metadataContainerId"],
                Is.EqualTo("33333333-3333-3333-3333-333333333333"));
            Assert.That(context["content.impact.parentId"], Is.EqualTo("44444444-4444-4444-4444-444444444444"));
            Assert.That(
                context["content.impact.ancestorIds"],
                Is.EqualTo("44444444-4444-4444-4444-444444444444,55555555-5555-5555-5555-555555555555"));
            Assert.That(context["content.impact.rootId"], Is.EqualTo("55555555-5555-5555-5555-555555555555"));
            Assert.That(context["content.impact.contentType"], Is.EqualTo("movies"));
            Assert.That(context["content.impact.level"], Is.EqualTo("movie"));
            Assert.That(context["content.impact.topLevel"], Is.EqualTo("false"));
            Assert.That(context["content.impact.rootFolderId"], Is.EqualTo("77777777-7777-7777-7777-777777777777"));
            Assert.That(
                context["content.impact.qualityTierListId"],
                Is.EqualTo("66666666-6666-6666-6666-666666666666"));
            Assert.That(context["content.impact.monitoring"], Is.EqualTo("true"));
            Assert.That(context["content.impact.fulfillment"], Is.EqualTo("Missing"));
            Assert.That(context["content.impact.wishlistId"], Is.EqualTo("88888888-8888-8888-8888-888888888888"));
        }
    }

    [Test]
    public void ToContext_WritesBothScopes_WhenTheChangeMovesTheItem()
    {
        // Arrange
        var impact = new ContentEventImpact
        {
            HierarchyItemId = HierarchyItemId,
            ParentId = ParentId,
            AncestorIds = [ParentId, RootId],
            RootId = RootId,
            Monitoring = true,
            Previous = new()
            {
                ParentId = OldParentId,
                AncestorIds = [OldParentId, OldRootId],
                RootId = OldRootId,
                TopLevel = false,
                Monitoring = false
            }
        };

        // Act
        var context = impact.ToContext();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context["content.impact.parentId"], Is.EqualTo("44444444-4444-4444-4444-444444444444"));
            Assert.That(
                context["content.impact.previous.parentId"],
                Is.EqualTo("66666666-6666-6666-6666-666666666666"));
            Assert.That(
                context["content.impact.previous.ancestorIds"],
                Is.EqualTo("66666666-6666-6666-6666-666666666666,77777777-7777-7777-7777-777777777777"));
            Assert.That(context["content.impact.previous.rootId"], Is.EqualTo("77777777-7777-7777-7777-777777777777"));
            Assert.That(context["content.impact.monitoring"], Is.EqualTo("true"));
            Assert.That(context["content.impact.previous.monitoring"], Is.EqualTo("false"));
        }
    }

    [Test]
    public void ToContext_SeparatesRelationshipDirections_WhenEndpointsChange()
    {
        // Arrange
        var impact = new ContentEventImpact
        {
            HierarchyItemId = HierarchyItemId,
            MetadataSourceContainerId = ContainerId,
            MetadataAddedContainerIds = [AddedContainerId],
            MetadataRemovedContainerIds = [RemovedContainerId]
        };

        // Act
        var context = impact.ToContext();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                context["content.impact.metadata.sourceContainerId"],
                Is.EqualTo("33333333-3333-3333-3333-333333333333"));
            Assert.That(
                context["content.impact.metadata.addedContainerIds"],
                Is.EqualTo("88888888-8888-8888-8888-888888888888"));
            Assert.That(
                context["content.impact.metadata.removedContainerIds"],
                Is.EqualTo("99999999-9999-9999-9999-999999999999"));
        }
    }

    [Test]
    public void ToContext_WritesEverySubtreeIdWithoutTruncation_WhenScopeExceedsThePreviousLimit()
    {
        // Arrange
        var ids = Enumerable.Range(0, 250).Select(_ => Guid.NewGuid()).ToArray();
        var impact = new ContentEventImpact { HierarchyItemId = HierarchyItemId, SubtreeIds = ids };

        // Act
        var context = impact.ToContext();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context["content.impact.subtreeIds"].Split(','), Has.Length.EqualTo(250));
            Assert.That(context.ContainsKey("content.impact.subtreeTruncated"), Is.False);
        }
    }

    [Test]
    public void ToContext_WritesEveryBatchIdWithoutTruncation_WhenScopeExceedsThePreviousLimit()
    {
        // Arrange
        var ids = Enumerable.Range(0, 250).Select(_ => Guid.NewGuid()).ToArray();
        var impact = new ContentEventImpact { HierarchyItemId = HierarchyItemId, BatchIds = ids };

        // Act
        var context = impact.ToContext();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context["content.impact.batchIds"].Split(','), Has.Length.EqualTo(250));
            Assert.That(context.ContainsKey("content.impact.batchTruncated"), Is.False);
        }
    }

    [Test]
    public void ToContext_WritesEveryAncestorId_WhenTheChainExceedsThePreviousLimit()
    {
        // Arrange
        var ids = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();
        var impact = new ContentEventImpact
        {
            HierarchyItemId = HierarchyItemId,
            AncestorIds = ids,
            Previous = new() { AncestorIds = ids }
        };

        // Act
        var context = impact.ToContext();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context["content.impact.ancestorIds"].Split(','), Has.Length.EqualTo(100));
            Assert.That(context["content.impact.previous.ancestorIds"].Split(','), Has.Length.EqualTo(100));
        }
    }

    [Test]
    public void ToContext_WritesEveryRelationshipId_WhenTheListsExceedThePreviousLimit()
    {
        // Arrange
        var ids = Enumerable.Range(0, 125).Select(_ => Guid.NewGuid()).ToArray();
        var impact = new ContentEventImpact
        {
            HierarchyItemId = HierarchyItemId,
            MetadataAddedContainerIds = ids,
            MetadataRemovedContainerIds = ids
        };

        // Act
        var context = impact.ToContext();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context["content.impact.metadata.addedContainerIds"].Split(','), Has.Length.EqualTo(125));
            Assert.That(context["content.impact.metadata.removedContainerIds"].Split(','), Has.Length.EqualTo(125));
        }
    }

    [Test]
    public void ToContext_WritesDistinctNonemptyIdsInFirstSeenOrder_WhenListsContainDuplicates()
    {
        // Arrange
        var impact = new ContentEventImpact
        {
            HierarchyItemId = HierarchyItemId,
            AncestorIds = [ParentId, Guid.Empty, RootId, ParentId],
            SubtreeIds = [RootId, ParentId, Guid.Empty, RootId],
            BatchIds = [AddedContainerId, RemovedContainerId, AddedContainerId],
            MetadataAddedContainerIds = [ContainerId, Guid.Empty, AddedContainerId, ContainerId],
            MetadataRemovedContainerIds = [RemovedContainerId, ContainerId, RemovedContainerId],
            Previous = new() { AncestorIds = [OldParentId, Guid.Empty, OldRootId, OldParentId] }
        };

        // Act
        var context = impact.ToContext();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                context["content.impact.ancestorIds"],
                Is.EqualTo("44444444-4444-4444-4444-444444444444,55555555-5555-5555-5555-555555555555"));
            Assert.That(
                context["content.impact.subtreeIds"],
                Is.EqualTo("55555555-5555-5555-5555-555555555555,44444444-4444-4444-4444-444444444444"));
            Assert.That(
                context["content.impact.batchIds"],
                Is.EqualTo("88888888-8888-8888-8888-888888888888,99999999-9999-9999-9999-999999999999"));
            Assert.That(
                context["content.impact.metadata.addedContainerIds"],
                Is.EqualTo("33333333-3333-3333-3333-333333333333,88888888-8888-8888-8888-888888888888"));
            Assert.That(
                context["content.impact.metadata.removedContainerIds"],
                Is.EqualTo("99999999-9999-9999-9999-999999999999,33333333-3333-3333-3333-333333333333"));
            Assert.That(
                context["content.impact.previous.ancestorIds"],
                Is.EqualTo("66666666-6666-6666-6666-666666666666,77777777-7777-7777-7777-777777777777"));
        }
    }

    [Test]
    public void ToContext_OmitsListKeys_WhenListsContainOnlyEmptyIds()
    {
        // Arrange
        var impact = new ContentEventImpact
        {
            HierarchyItemId = HierarchyItemId,
            AncestorIds = [Guid.Empty],
            SubtreeIds = [Guid.Empty],
            BatchIds = [Guid.Empty],
            MetadataAddedContainerIds = [Guid.Empty],
            MetadataRemovedContainerIds = [Guid.Empty],
            Previous = new() { AncestorIds = [Guid.Empty] }
        };

        // Act
        var context = impact.ToContext();

        // Assert
        Assert.That(context.Keys, Is.EquivalentTo(["content.hierarchyItemId"]));
    }

    [Test]
    public void ToContext_OmitsUnsetValues_WhenOnlyThePrimaryIdentityIsKnown()
    {
        // Arrange
        var impact = new ContentEventImpact { HierarchyItemId = HierarchyItemId };

        // Act
        var context = impact.ToContext();

        // Assert
        Assert.That(context.Keys, Is.EquivalentTo(["content.hierarchyItemId"]));
    }

    [Test]
    public void ToTriggerContext_RoundTripsEveryImpactKey_WhenTheEventCrossesTheTriggerBoundary()
    {
        // Arrange
        var ids = Enumerable.Range(0, 250).Select(_ => Guid.NewGuid()).ToArray();
        var impact = new ContentEventImpact
        {
            HierarchyItemId = HierarchyItemId,
            ItemId = ItemId,
            ParentId = ParentId,
            AncestorIds = ids,
            RootId = RootId,
            SubtreeIds = ids,
            BatchIds = ids,
            MetadataSourceContainerId = ContainerId,
            MetadataAddedContainerIds = ids,
            MetadataRemovedContainerIds = ids,
            Previous = new() { ParentId = OldParentId, AncestorIds = ids, RootId = OldRootId }
        };
        var published = new MagpieEvent(
            Guid.NewGuid(),
            "content.hierarchyItem.updated",
            impact.ToContext(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            "node-1");

        // Act
        var relayed = published.ToTriggerContext().ToMagpieEvent();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relayed, Is.Not.Null);
            Assert.That(relayed!.Context, Is.EquivalentTo(published.Context));
            Assert.That(
                relayed.Context["content.impact.ancestorIds"].Split(','),
                Is.EqualTo(ids.Select(id => id.ToString("D"))));
            Assert.That(
                relayed.Context["content.impact.subtreeIds"].Split(','),
                Is.EqualTo(ids.Select(id => id.ToString("D"))));
            Assert.That(
                relayed.Context["content.impact.batchIds"].Split(','),
                Is.EqualTo(ids.Select(id => id.ToString("D"))));
            Assert.That(
                relayed.Context["content.impact.metadata.addedContainerIds"].Split(','),
                Is.EqualTo(ids.Select(id => id.ToString("D"))));
            Assert.That(
                relayed.Context["content.impact.metadata.removedContainerIds"].Split(','),
                Is.EqualTo(ids.Select(id => id.ToString("D"))));
            Assert.That(
                relayed.Context["content.impact.previous.ancestorIds"].Split(','),
                Is.EqualTo(ids.Select(id => id.ToString("D"))));
        }
    }
}
