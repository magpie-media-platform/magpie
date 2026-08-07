using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Core.Diagnostics;
using Magpie.Models;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Diagnostics;

[TestFixture]
public sealed class ContentDefaultsRepairTests
{
    [Test]
    public async Task RunAsync_RecreatesAndAssignsAny_WhenNoTierListExists()
    {
        // Arrange
        var rootFolderId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var finding = new DiagnosticFinding
        {
            DiagnosticCheckId = ContentDefaultsDiagnostic.Id,
            Title = "Missing default",
            Detail = "Missing default",
            AffectedEntity = $"RootFolder:{rootFolderId}"
        };
        var rootFolders = new Mock<IRootFolderService>(MockBehavior.Strict);
        rootFolders.Setup(service => service.GetAsync(rootFolderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RootFolder
            {
                Id = rootFolderId,
                Name = "Publications",
                Path = "/publications",
                ContentType = "publication"
            });
        rootFolders.Setup(service => service.PatchAsync(
                rootFolderId,
                It.Is<RootFolderPatch>(patch => patch.Defaults != null && patch.Defaults.ContainsKey("publication")),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var tierLists = new Mock<IQualityService>(MockBehavior.Strict);
        tierLists.Setup(service => service.ListTierListsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var tiers = tierLists;
        tiers.Setup(service => service.ListTiersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        tiers.Setup(service => service.CreateTierAsync(
                It.Is<QualityTier>(tier => tier.Name == "Any"),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityTier { Id = Guid.NewGuid(), Name = "Any", Criteria = new() });
        tierLists.Setup(service => service.CreateTierListAsync(
                It.Is<QualityTierList>(list => list.Name == "Any" && list.QualityTierRanks.Count == 1),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((QualityTierList list, Guid? _, CancellationToken _) =>
                new QualityTierListCreateResult { TierList = list });
        var contentTypes = new Mock<IContentTypeRegistry>();
        contentTypes.Setup(registry => registry.GetDefinitionFor("publication")).Returns(new ContentTypeDefinition
        {
            Slug = "publication",
            DisplayName = "Publication",
            LevelDefinitions =
            [
                new() { Slug = "publication", DisplayName = "Publication", CustomMetadataFields = [] }
            ]
        });
        var subject = new ContentDefaultsRepair(
            rootFolders.Object,
            tierLists.Object,
            contentTypes.Object);

        // Act
        var result = await subject.RunAsync(finding).ConfigureAwait(false);

        // Assert
        Assert.That(result.Success, Is.True);
    }
}
