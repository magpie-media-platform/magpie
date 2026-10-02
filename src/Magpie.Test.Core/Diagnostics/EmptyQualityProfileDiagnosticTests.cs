using System;
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
public sealed class EmptyQualityProfileDiagnosticTests
{
    private Mock<IQualityTierListRepository> _tierLists = null!;
    private IDiagnosticCheck _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _tierLists = new Mock<IQualityTierListRepository>(MockBehavior.Strict);
        _subject = new EmptyQualityProfileDiagnostic(_tierLists.Object);
    }

    [Test]
    public async Task RunAsync_ProducesWarning_WhenProfileHasNoTiers()
    {
        // Arrange
        var list = new QualityTierList
        {
            Id = new Guid("00000000-0000-0000-0000-0000000000a1"),
            Name = "Empty profile",
            CreatedAt = DateTime.UtcNow,
        };
        _tierLists.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([list]);

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(findings, Has.Count.EqualTo(1));
            Assert.That(findings[0].Severity, Is.EqualTo(DiagnosticFindingSeverity.Warning));
            Assert.That(findings[0].DiagnosticCheckId, Is.EqualTo(EmptyQualityProfileDiagnostic.Id));
            Assert.That(findings[0].RepairId, Is.Null);
            Assert.That(
                findings[0].AffectedEntity,
                Is.EqualTo("QualityTierList:00000000-0000-0000-0000-0000000000a1"));
        }
    }

    [Test]
    public async Task RunAsync_ProducesNoFinding_WhenProfileHasRankJoinedTiers()
    {
        // Arrange
        var list = new QualityTierList
        {
            Id = Guid.NewGuid(),
            Name = "Healthy profile",
            CreatedAt = DateTime.UtcNow,
        };
        var firstTier = new QualityTier
        {
            Id = Guid.NewGuid(),
            Name = "Tier 0",
            Criteria = new QualityTierCriteria(),
            CreatedAt = DateTime.UtcNow,
        };
        var secondTier = new QualityTier
        {
            Id = Guid.NewGuid(),
            Name = "Tier 1",
            Criteria = new QualityTierCriteria(),
            CreatedAt = DateTime.UtcNow,
        };
        list.QualityTierRanks.Add(new QualityTierListRank
        {
            QualityTierListId = list.Id,
            QualityTierId = firstTier.Id,
            Ordinal = 0,
            CreatedAt = DateTime.UtcNow,
            QualityTier = firstTier,
        });
        list.QualityTierRanks.Add(new QualityTierListRank
        {
            QualityTierListId = list.Id,
            QualityTierId = secondTier.Id,
            Ordinal = 1,
            CreatedAt = DateTime.UtcNow,
            QualityTier = secondTier,
        });
        _tierLists.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([list]);

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(findings, Is.Empty);
    }

    [Test]
    public async Task RunAsync_ProducesWarning_WhenProfileHasOnlyListRules()
    {
        // Arrange
        var list = new QualityTierList
        {
            Id = new Guid("00000000-0000-0000-0000-0000000000a2"),
            Name = "Rules-only profile",
            CreatedAt = DateTime.UtcNow,
        };
        list.Rules.Add(new QualityTierRule
        {
            Id = Guid.NewGuid(),
            Name = "1080p",
            Kind = RuleKind.Accept,
            Criteria = new QualityTierCriteria(),
        });
        _tierLists.Setup(repository => repository.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([list]);

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(findings, Has.Count.EqualTo(1));
            Assert.That(
                findings[0].AffectedEntity,
                Is.EqualTo("QualityTierList:00000000-0000-0000-0000-0000000000a2"));
        }
    }
}
