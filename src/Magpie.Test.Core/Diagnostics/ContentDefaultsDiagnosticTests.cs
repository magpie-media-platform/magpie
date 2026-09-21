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
                            QualityTierListId = Guid.NewGuid(),
                        },
                        ["book"] = new RootFolderDefault
                    {
                            QualityTierListId = Guid.NewGuid(),
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
        var tierListId = Guid.NewGuid();
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

    [TestCase(false, "Content type catalog has no default root folder")]
    [TestCase(true, "Content type catalog has multiple default root folders")]
    public async Task RunAsync_IdentifiesDefaultCount_WhenThereIsNoUniqueDefault(bool isDefault, string expectedTitle)
    {
        // Arrange
        var repository = new Mock<IRootFolderRepository>();
        repository.Setup(value => value.ListAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new() { Id = Guid.NewGuid(), Name = "First", Path = "/first", ContentType = "catalog", IsDefault =
                isDefault },
            new() { Id = Guid.NewGuid(), Name = "Second", Path = "/second", ContentType = "catalog", IsDefault =
                isDefault },
        ]);
        var registry = new Mock<IContentTypeRegistry>();
        registry.SetupGet(value => value.Definitions).Returns([]);
        var quality = new Mock<IQualityService>();
        quality.Setup(value => value.ListTierListsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var subject = new ContentDefaultsDiagnostic(registry.Object, repository.Object, quality.Object);

        // Act
        var findings = await subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(findings.Single(value => value.AffectedEntity == "ContentType:catalog").Title,
            Is.EqualTo(expectedTitle));
    }
}
