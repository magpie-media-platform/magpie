using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Diagnostics;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Diagnostics;

[TestFixture]
public sealed class ContentDefaultsDiagnosticTests
{
    [Test]
    public async Task RunAsync_ReturnsActionableFindings_WhenLevelDefaultsAreUnresolvedOrStale()
    {
        // Arrange
        var bookLevel = new ContentHierarchyLevelDefinition
        {
            Slug = "book",
            DisplayName = "Book",
            CustomMetadataFields = [],
        };
        var audiobookLevel = new ContentHierarchyLevelDefinition
        {
            Slug = "audiobook",
            DisplayName = "Audiobook",
            CustomMetadataFields = [],
        };
        var definition = new ContentTypeDefinition
        {
            Slug = "publication",
            DisplayName = "Publication",
            LevelDefinitions = [bookLevel, audiobookLevel],
        };
        var contentTypes = new Mock<IContentTypeRegistry>(MockBehavior.Strict);
        contentTypes.SetupGet(registry => registry.Definitions).Returns([definition]);
        contentTypes.Setup(registry => registry.GetDefinitionFor("publication")).Returns(definition);
        var rootFolders = new Mock<IRootFolderRepository>(MockBehavior.Strict);
        rootFolders.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RootFolder
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Name = "Publications",
                    Path = "/publications",
                    ContentType = "publication",
                    Defaults = new()
                    {
                        ["removed"] = new RootFolderDefault
                    {
                            QualityTierListId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                        },
                        ["book"] = new RootFolderDefault
                    {
                            QualityTierListId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                        },
                    },
                },
            ]);
        var qualityService = new Mock<IQualityService>(MockBehavior.Strict);
        qualityService.Setup(service => service.ListTierListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var subject = new ContentDefaultsDiagnostic(
            contentTypes.Object,
            rootFolders.Object,
            qualityService.Object);

        // Act
        var findings = await subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(findings, Has.Count.EqualTo(3));
            Assert.That(findings.Select(finding => finding.Title), Has.One.Contains("stale quality default"));
            Assert.That(findings.Select(finding => finding.Title), Has.One.Contains("missing quality tier list"));
            Assert.That(findings.Select(finding => finding.Title), Has.One.Contains("has no quality default"));
        }
    }

    [Test]
    public async Task RunAsync_ReturnsNoFinding_WhenDefaultsAreValid()
    {
        // Arrange
        var tierListId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var definition = new ContentTypeDefinition
        {
            Slug = "publication",
            DisplayName = "Publication",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "book",
                    DisplayName = "Book",
                    CustomMetadataFields = [],
                },
            ],
        };
        var contentTypes = new Mock<IContentTypeRegistry>(MockBehavior.Strict);
        contentTypes.SetupGet(registry => registry.Definitions).Returns([definition]);
        contentTypes.Setup(registry => registry.GetDefinitionFor("publication")).Returns(definition);
        var rootFolders = new Mock<IRootFolderRepository>(MockBehavior.Strict);
        rootFolders.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RootFolder
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Name = "Publications",
                    Path = "/publications",
                    ContentType = "publication",
                    Defaults = new()
                    {
                        ["book"] = new()
                        {
                            QualityTierListId = tierListId
                        }
                    }
                },
            ]);
        var qualityService = new Mock<IQualityService>(MockBehavior.Strict);
        qualityService.Setup(service => service.ListTierListsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new QualityTierList { Id = tierListId, Name = "Books" }]);
        var subject = new ContentDefaultsDiagnostic(
            contentTypes.Object,
            rootFolders.Object,
            qualityService.Object);

        // Act
        var findings = await subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(findings, Is.Empty);
    }
}
