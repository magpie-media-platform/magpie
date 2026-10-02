using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Books;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Extensions;
using Magpie.Sdk.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Books;

[TestFixture]
public class BooksExtensionTests
{
    private static readonly Guid ExpectedExtensionId = new("8ed67ec9-fc9a-4d09-a5f1-5d27701975aa");

    [Test]
    public void Extension_ReturnsStableIdentity_WhenRead()
    {
        // Arrange
        // Act
        var extension = new BooksExtension();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(extension.Id, Is.EqualTo(ExpectedExtensionId));
            Assert.That(extension.Name, Is.EqualTo("Books"));
        }
    }

    [TestCase("Sample Size Determination", false, false, false, false, false, false)]
    [TestCase("A Preview of Modern Physics", false, false, false, false, false, false)]
    [TestCase("The Sample Size Determination (epub)", false, false, false, false, true, false)]
    [TestCase("A Preview of Modern Physics (epub)", false, false, false, false, true, false)]
    [TestCase("Charlottes.Web.1952.epub", false, false, false, false, true, false)]
    [TestCase("Book Title (2020) (epub)", false, false, false, false, true, false)]
    [TestCase("Retail Therapy (2020) (epub)", false, false, false, false, true, false)]
    [TestCase("The Retail Therapy Guide (epub)", false, false, false, false, true, false)]
    [TestCase("The Converted (2020) (epub)", false, false, false, false, true, false)]
    [TestCase("The Scan (2020) (pdf)", false, false, false, false, false, true)]
    [TestCase("The Scanned Image Handbook (pdf)", false, false, false, false, false, true)]
    [TestCase("Book Title (2020) [RETAIL] (epub)", false, true, false, false, true, false)]
    [TestCase("Book Title (2020) [WEB] (epub)", false, false, true, false, true, false)]
    [TestCase("Book Title (2020) [CONVERTED] (epub)", false, false, true, false, true, false)]
    [TestCase("Book Title (2020) [SCAN] (pdf)", false, false, false, true, false, true)]
    [TestCase("Book Title (2020) [SAMPLE] (epub)", true, false, false, false, true, false)]
    [TestCase("Book Title (2020) [PREVIEW] (epub)", true, false, false, false, true, false)]
    [TestCase("Book.Title.2020.RETAIL.epub", false, true, false, false, true, false)]
    [TestCase("Book Title 2020 RETAIL epub", false, true, false, false, true, false)]
    [TestCase("Book.Title.2020.WEB.epub", false, false, true, false, true, false)]
    [TestCase("Book Title 2020 WEB epub", false, false, true, false, true, false)]
    [TestCase("Book.Title.2020.CONVERTED.epub", false, false, true, false, true, false)]
    [TestCase("Book.Title.2020.SCAN.pdf", false, false, false, true, false, true)]
    [TestCase("Book Title 2020 SCANNED pdf", false, false, false, true, false, true)]
    [TestCase("Book.Title.2020.SAMPLE.epub", true, false, false, false, true, false)]
    [TestCase("Book Title 2020 PREVIEW epub", true, false, false, false, true, false)]
    public async Task OnExtensionInstallAsync_SeedsExpectedEbookPatterns_WhenListsAreMissing(
        string title,
        bool expectedExcluded,
        bool expectedRetailSource,
        bool expectedWebSource,
        bool expectedScanSource,
        bool expectedReflowableFormat,
        bool expectedPdfFormat)
    {
        // Arrange
        var created = new List<QualityTierList>();
        var qualityService = new Mock<IQualityService>();
        var tierService = qualityService;
        qualityService
            .Setup(service => service.GetTierListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QualityTierList?)null);
        tierService
            .Setup(service => service.GetTierAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QualityTier?)null);
        qualityService
            .Setup(service => service.CreateTierListAsync(
                It.IsAny<QualityTierList>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((QualityTierList list, Guid? _, CancellationToken _) => created.Add(list))
            .ReturnsAsync((QualityTierList list, Guid? _, CancellationToken _) =>
                new QualityTierListCreateResult { TierList = list });
        using var provider = new ServiceCollection()
            .AddSingleton(qualityService.Object)
            .BuildServiceProvider();
        var context = new ExtensionLifecycleContext("test", provider, NullLoggerFactory.Instance);
        IExtensionLifecycle subject = new BooksExtension();

        // Act
        await subject.OnExtensionInstallAsync(context).ConfigureAwait(false);

        var retail = created.Single(list => list.Id == new Guid("5afb7256-c6e5-4550-b97f-7980a2835a32"));
        var compatibility = created.Single(list => list.Id == new Guid("5df23dff-c7c2-46de-a0a1-c8e3c5e73019"));
        var archive = created.Single(list => list.Id == new Guid("22076a35-1223-466b-a8d1-4b30c620ca2e"));
        var retailReflowable = retail.QualityTierRanks.Single(rank =>
            rank.QualityTierId == new Guid("6a60b3e3-9c22-49f7-88dd-cbdc1ccca338"));
        var webConverted = retail.QualityTierRanks.Single(rank =>
            rank.QualityTierId == new Guid("c3089392-ebd8-4d44-a24c-b453b790cef6"));
        var reflowable = retail.QualityTierRanks.Single(rank =>
            rank.QualityTierId == new Guid("14297965-aeb4-43fb-8d58-d2574708408c"));
        var pdf = retail.QualityTierRanks.Single(rank =>
            rank.QualityTierId == new Guid("8d599785-24bb-44b6-bd28-166e8998cda0"));
        var scannedPdf = archive.QualityTierRanks.Single(rank =>
            rank.QualityTierId == new Guid("2d5516c9-36ed-4ffb-8559-d8ad4e7b7178"));
        var criteriaItems = created
            .SelectMany(list => list.QualityTierRanks
                .Select(rank => rank.QualityTier!.Criteria)
                .Concat(list.Rules.Select(rule => rule.Criteria)))
            .SelectMany(criteria => criteria.And)
            .SelectMany(criteria => criteria.Items)
            .ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Has.Count.EqualTo(3));
            Assert.That(retail.Name, Is.EqualTo("E-book - Retail and Reflowable"));
            Assert.That(compatibility.Name, Is.EqualTo("E-book - Broad Compatibility"));
            Assert.That(archive.Name, Is.EqualTo("E-book - Archive and Scans"));
            Assert.That(
                retail.MimeTypes,
                Is.EqualTo(
                [
                    "application/epub+zip",
                    "application/pdf",
                    "application/vnd.amazon.ebook",
                    "application/x-mobipocket-ebook"
                ]));
            Assert.That(compatibility.MimeTypes, Is.EqualTo(retail.MimeTypes));
            Assert.That(archive.MimeTypes, Is.EqualTo(retail.MimeTypes));
            Assert.That(
                retail.QualityTierRanks.OrderBy(rank => rank.Ordinal).Select(rank => rank.QualityTier!.Name),
                Is.EqualTo(
                    ["Retail EPUB/AZW3", "Retail MOBI", "Web/Converted EPUB", "EPUB/AZW3/MOBI", "PDF"]));
            Assert.That(retail.QualityTierRanks.Single(rank => rank.IsCutoff).QualityTierId,
                Is.EqualTo(new Guid("c3089392-ebd8-4d44-a24c-b453b790cef6")));
            Assert.That(
                created.Select(list => Regex.IsMatch(
                    title,
                    list.Rules.Single().Criteria.And.Single().Items["Title"].Value!,
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(100))),
                Is.All.EqualTo(expectedExcluded));
            Assert.That(
                Regex.IsMatch(
                    title,
                    retailReflowable.QualityTier!.Criteria.And.ElementAt(0).Items["Title"].Value!,
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(100)),
                Is.EqualTo(expectedRetailSource));
            Assert.That(
                Regex.IsMatch(
                    title,
                    webConverted.QualityTier!.Criteria.And.ElementAt(0).Items["Title"].Value!,
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(100)),
                Is.EqualTo(expectedWebSource));
            Assert.That(
                Regex.IsMatch(
                    title,
                    scannedPdf.QualityTier!.Criteria.And.ElementAt(1).Items["Title"].Value!,
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(100)),
                Is.EqualTo(expectedScanSource));
            Assert.That(
                Regex.IsMatch(
                    title,
                    reflowable.QualityTier!.Criteria.And.Single().Items["Title"].Value!,
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(100)),
                Is.EqualTo(expectedReflowableFormat));
            Assert.That(
                Regex.IsMatch(
                    title,
                    pdf.QualityTier!.Criteria.And.Single().Items["Title"].Value!,
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(100)),
                Is.EqualTo(expectedPdfFormat));
            Assert.That(
                compatibility.QualityTierRanks.OrderBy(rank => rank.Ordinal).Select(rank => rank.QualityTier!.Name),
                Is.EqualTo(["EPUB", "AZW/AZW3", "MOBI", "PDF"]));
            Assert.That(compatibility.QualityTierRanks.Single(rank => rank.IsCutoff).Ordinal, Is.EqualTo(0));
            Assert.That(
                archive.QualityTierRanks.OrderBy(rank => rank.Ordinal).Select(rank => rank.QualityTier!.Name),
                Is.EqualTo(["Scanned PDF", "OCR PDF", "PDF"]));
            Assert.That(archive.QualityTierRanks.Single(rank => rank.IsCutoff).Ordinal, Is.EqualTo(1));
            Assert.That(retail.Rules.Single().Kind, Is.EqualTo(RuleKind.Exclude));
            Assert.That(compatibility.Rules.Single().Kind, Is.EqualTo(RuleKind.Exclude));
            Assert.That(archive.Rules.Single().Kind, Is.EqualTo(RuleKind.Exclude));
            Assert.That(criteriaItems, Is.Not.Empty);
            Assert.That(criteriaItems.Select(item => item.Key), Is.All.EqualTo("Title"));
            Assert.That(
                criteriaItems.Select(item => item.Value.Operator),
                Is.All.EqualTo(CriteriaValueOperator.Regex));
        }
    }

    [Test]
    public async Task OnExtensionInstallAsync_DoesNotCreateDuplicates_WhenRunTwice()
    {
        // Arrange
        var qualityService = new Mock<IQualityService>();
        var tierService = qualityService;
        qualityService
            .SetupSequence(service => service.GetTierListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QualityTierList?)null)
            .ReturnsAsync((QualityTierList?)null)
            .ReturnsAsync((QualityTierList?)null)
            .ReturnsAsync(new QualityTierList { Name = "Edited retail", CreatedAt = new DateTime(2025, 1, 1) })
            .ReturnsAsync(new QualityTierList { Name = "Edited compatibility", CreatedAt = new DateTime(2025, 1, 1) })
            .ReturnsAsync(new QualityTierList { Name = "Edited archive", CreatedAt = new DateTime(2025, 1, 1) });
        tierService
            .Setup(service => service.GetTierAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QualityTier?)null);
        qualityService
            .Setup(service => service.CreateTierListAsync(
                It.IsAny<QualityTierList>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((QualityTierList list, Guid? _, CancellationToken _) =>
                new QualityTierListCreateResult { TierList = list });
        using var provider = new ServiceCollection()
            .AddSingleton(qualityService.Object)
            .BuildServiceProvider();
        var context = new ExtensionLifecycleContext("test", provider, NullLoggerFactory.Instance);
        IExtensionLifecycle subject = new BooksExtension();

        // Act
        await subject.OnExtensionInstallAsync(context).ConfigureAwait(false);
        await subject.OnExtensionInstallAsync(context).ConfigureAwait(false);

        // Assert
        qualityService.Verify(
            service => service.CreateTierListAsync(
                It.IsAny<QualityTierList>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Test]
    public async Task OnExtensionInstallAsync_ReusesStableTier_WhenTierAlreadyExists()
    {
        // Arrange
        var existingTierId = new Guid("6a60b3e3-9c22-49f7-88dd-cbdc1ccca338");
        var created = new List<QualityTierList>();
        var qualityService = new Mock<IQualityService>();
        var tierService = qualityService;
        qualityService
            .Setup(service => service.GetTierListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QualityTierList?)null);
        tierService
            .Setup(service => service.GetTierAsync(existingTierId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QualityTier
            {
                Id = existingTierId,
                Name = "User-edited tier",
                Criteria = new QualityTierCriteria(),
                CreatedAt = new DateTime(2025, 1, 1)
            });
        tierService
            .Setup(service => service.GetTierAsync(
                It.Is<Guid>(id => id != existingTierId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((QualityTier?)null);
        qualityService
            .Setup(service => service.CreateTierListAsync(
                It.IsAny<QualityTierList>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback((QualityTierList list, Guid? _, CancellationToken _) => created.Add(list))
            .ReturnsAsync((QualityTierList list, Guid? _, CancellationToken _) =>
                new QualityTierListCreateResult { TierList = list });
        using var provider = new ServiceCollection()
            .AddSingleton(qualityService.Object)
            .BuildServiceProvider();
        var context = new ExtensionLifecycleContext("test", provider, NullLoggerFactory.Instance);
        IExtensionLifecycle subject = new BooksExtension();

        // Act
        await subject.OnExtensionInstallAsync(context).ConfigureAwait(false);

        var retail = created.Single(list => list.Id == new Guid("5afb7256-c6e5-4550-b97f-7980a2835a32"));
        var rank = retail.QualityTierRanks.Single(value => value.QualityTierId == existingTierId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Has.Count.EqualTo(3));
            Assert.That(rank.QualityTier, Is.Null);
        }
    }
}
