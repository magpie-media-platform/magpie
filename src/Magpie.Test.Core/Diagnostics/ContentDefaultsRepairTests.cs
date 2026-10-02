using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core;
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
        var rootFolderId = new Guid("00000000-0000-0000-0000-0000000000b1");
        var finding = new DiagnosticFinding
        {
            DiagnosticCheckId = ContentDefaultsDiagnostic.Id,
            Title = "Missing default",
            Detail = "Missing default",
            AffectedEntity = "RootFolder:00000000-0000-0000-0000-0000000000b1"
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
                It.Is<RootFolderPatch>(patch =>
                    patch.Defaults != null && patch.Defaults.Value.ContainsKey("publication")),
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

    [Test]
    public async Task RunAsync_MergesRepairedLevels_WhenOtherDefaultsAreValid()
    {
        // Arrange
        var root = new RootFolder
        {
            Id = new Guid("00000000-0000-0000-0000-0000000000b2"),
            Name = "Catalog",
            Path = "/catalog",
            ContentType = "catalog",
            Defaults = new()
            {
                ["entry"] = new() { QualityTierListId = Guid.Parse("87f52e52-adac-4845-b37c-3d7e24369835") },
                ["part"] = new() { QualityTierListId = Guid.Parse("f81d3f26-3473-498c-b090-c01b8447db65") },
                ["removed"] = new() { QualityTierListId = Guid.Parse("87f52e52-adac-4845-b37c-3d7e24369835") },
            },
        };
        var definition = new ContentTypeDefinition
        {
            Slug = "catalog",
            DisplayName = "Catalog",
            LevelDefinitions =
            [
                new() { Slug = "entry", DisplayName = "Entry", CustomMetadataFields = [] },
                new() { Slug = "part", ParentSlug = "entry", DisplayName = "Part", CustomMetadataFields = [] },
                new() { Slug = "other", DisplayName = "Other", CustomMetadataFields = [] },
            ],
        };
        var registry = new Mock<IContentTypeRegistry>();
        registry.Setup(value => value.GetDefinitionFor("catalog")).Returns(definition);
        RootFolderPatch? applied = null;
        var repository = new Mock<IRootFolderRepository>();
        repository.Setup(value => value.GetByIdAsync(root.Id, It.IsAny<CancellationToken>())).ReturnsAsync(root);
        repository
            .Setup(value => value.PatchAsync(
                It.IsAny<RootFolderPatch>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((RootFolderPatch patch, Guid? _, CancellationToken _) => applied = patch)
            .ReturnsAsync(true);
        var quality = new Mock<IQualityService>();
        quality.Setup(value => value.ListTierListsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new() { Id = Guid.Parse("87f52e52-adac-4845-b37c-3d7e24369835"), Name = "Preferred" },
            new()
            {
                Id = Guid.Parse("a9820e75-230c-431b-a6ad-c938bb3feb51"), Name = "Any",
                QualityTierRanks = [new() { QualityTierId = Guid.NewGuid() }],
            },
        ]);
        var service = new RootFolderService(repository.Object, Mock.Of<IFileSystem>(), registry.Object, quality.Object);
        var subject = new ContentDefaultsRepair(service, quality.Object, registry.Object);
        var finding = new DiagnosticFinding
        {
            DiagnosticCheckId = ContentDefaultsDiagnostic.Id,
            Title = "Default",
            Detail = "Default",
            AffectedEntity = "RootFolder:00000000-0000-0000-0000-0000000000b2",
        };

        // Act
        var result = await subject.RunAsync(finding).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True);
            Assert.That(applied!.Defaults!.Value["entry"].QualityTierListId.ToString(),
                Is.EqualTo("87f52e52-adac-4845-b37c-3d7e24369835"));
            Assert.That(applied.Defaults.Value["part"].QualityTierListId.ToString(),
                Is.EqualTo("a9820e75-230c-431b-a6ad-c938bb3feb51"));
            Assert.That(applied.Defaults.Value["other"].QualityTierListId.ToString(),
                Is.EqualTo("a9820e75-230c-431b-a6ad-c938bb3feb51"));
            Assert.That(applied.Defaults.Value.ContainsKey("removed"), Is.False);
        }
    }
}
