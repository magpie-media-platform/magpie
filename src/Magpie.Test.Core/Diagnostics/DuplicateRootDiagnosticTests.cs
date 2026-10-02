using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Abstractions.Repositories;
using Magpie.Core.Diagnostics;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Diagnostics;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Diagnostics;

[TestFixture]
public sealed class DuplicateRootDiagnosticTests
{
    private static readonly Guid TvmazeShowId = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid WikidataShowId = new("00000000-0000-0000-0000-00000000000b");

    private Mock<IContentHierarchyItemRepository> _items = null!;
    private DuplicateRootDiagnostic _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _items = new Mock<IContentHierarchyItemRepository>();
        _items
            .Setup(repository => repository.FindDuplicateRootsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DuplicateRoots
                {
                    Source = "imdb",
                    Value = "tt1",
                    Items = new Dictionary<Guid, string>
                    {
                        [TvmazeShowId] = "Breaking Bad",
                        [WikidataShowId] = "Breaking Bad (Wikidata)"
                    }
                },
                new DuplicateRoots
                {
                    Source = "tvdb",
                    Value = "81189",
                    Items = new Dictionary<Guid, string>
                    {
                        [TvmazeShowId] = "Breaking Bad",
                        [WikidataShowId] = "Breaking Bad (Wikidata)"
                    }
                }
            ]);
        _subject = new DuplicateRootDiagnostic(_items.Object);
    }

    [Test]
    public async Task RunAsync_ReportsOneFindingPerItem_WhenItemsAreDuplicates()
    {
        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        var tvmaze = findings.Single(finding =>
            finding.AffectedEntity == "ContentHierarchyItem:00000000-0000-0000-0000-00000000000a");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                findings.Select(finding => finding.AffectedEntity),
                Is.EquivalentTo(
                [
                    "ContentHierarchyItem:00000000-0000-0000-0000-00000000000a",
                    "ContentHierarchyItem:00000000-0000-0000-0000-00000000000b"
                ]));
            Assert.That(findings, Has.All.Matches<DiagnosticFinding>(finding =>
                finding.Severity == DiagnosticFindingSeverity.Warning && finding.RepairId == null));
            Assert.That(tvmaze.Title, Is.EqualTo("Breaking Bad is a duplicate"));
            Assert.That(
                tvmaze.Detail,
                Is.EqualTo("Breaking Bad and Breaking Bad (Wikidata) share imdb:tt1, tvdb:81189."));
        }
    }

    [Test]
    public async Task RunAsync_ReportsNothing_WhenNoItemsAreDuplicates()
    {
        // Arrange
        _items
            .Setup(repository => repository.FindDuplicateRootsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(findings, Is.Empty);
    }
}
