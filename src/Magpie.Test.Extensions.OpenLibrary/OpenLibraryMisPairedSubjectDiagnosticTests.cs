using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;
using Magpie.Sdk.Diagnostics;
using Magpie.Sdk.Services;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.OpenLibrary;

[TestFixture]
public sealed class OpenLibraryMisPairedSubjectDiagnosticTests
{
    private static readonly Guid BilboId = new("00000000-0000-0000-0000-000000000001");

    private static readonly Guid BalinId = new("00000000-0000-0000-0000-000000000002");

    private static readonly Guid AuthorId = new("00000000-0000-0000-0000-000000000003");

    private static readonly Guid PlaceId = new("00000000-0000-0000-0000-000000000004");

    private static readonly Guid SlugBandId = new("00000000-0000-0000-0000-000000000005");

    private static readonly Guid KeyedBandId = new("00000000-0000-0000-0000-000000000006");

    private static readonly Guid PortraitId = new("00000000-0000-0000-0000-000000000007");

    private Mock<ISubjectService> _subjects = null!;
    private OpenLibraryMisPairedSubjectDiagnostic _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _subjects = new Mock<ISubjectService>();
        _subjects
            .Setup(service => service.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasUserContributions == null && query.HasTags == null),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject>
            {
                Items =
                [
                    new() { Id = BilboId, Kind = "person" },
                    new() { Id = BalinId, Kind = "person" },
                    new() { Id = AuthorId, Kind = "person" },
                    new() { Id = PlaceId, Kind = "place" },
                    new() { Id = SlugBandId, Kind = "person" },
                    new() { Id = KeyedBandId, Kind = "person" },
                    new() { Id = PortraitId, Kind = "person" },
                ],
                TotalItems = 7
            });
        _subjects
            .Setup(service => service.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasUserContributions == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject>
            {
                Items = [new() { Id = PlaceId, Kind = "place" }],
                TotalItems = 1
            });
        _subjects
            .Setup(service => service.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasTags == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject>
            {
                Items = [new() { Id = SlugBandId, Kind = "person" }],
                TotalItems = 1
            });
        var metadata = new Mock<IMetadataService>();
        metadata
            .Setup(service => service.GetSubjectContributionsAsync(BilboId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataContribution
                {
                    ProviderType = "openlibrary",
                    Title = "Bilbo Baggins",
                    ExternalIds = { new ExternalId { Source = "openlibrary", Value = "balin" } }
                }
            ]);
        metadata
            .Setup(service => service.GetSubjectContributionsAsync(BalinId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataContribution
                {
                    ProviderType = "openlibrary",
                    Title = "Balin",
                    ExternalIds = { new ExternalId { Source = "openlibrary", Value = "balin" } }
                }
            ]);
        metadata
            .Setup(service => service.GetSubjectContributionsAsync(AuthorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataContribution
                {
                    ProviderType = "openlibrary",
                    Title = "J. R. R. Tolkien",
                    ExternalIds = { new ExternalId { Source = "openlibrary", Value = "OL26320A" } }
                }
            ]);
        metadata
            .Setup(service => service.GetSubjectContributionsAsync(PlaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataContribution
                {
                    ProviderType = "openlibrary",
                    Title = "Middle-earth",
                    ExternalIds = { new ExternalId { Source = "openlibrary", Value = "mordor" } }
                }
            ]);
        metadata
            .Setup(service => service.GetSubjectContributionsAsync(SlugBandId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataContribution
                {
                    ProviderType = "openlibrary",
                    Title = "AC/DC",
                    ExternalIds = { new ExternalId { Source = "openlibrary", Value = "ac_dc" } }
                }
            ]);
        metadata
            .Setup(service => service.GetSubjectContributionsAsync(KeyedBandId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataContribution
                {
                    ProviderType = "openlibrary",
                    Title = "AC/DC",
                    ExternalIds = { new ExternalId { Source = "openlibrary", Value = "acdc" } }
                }
            ]);
        metadata
            .Setup(service => service.GetSubjectContributionsAsync(PortraitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataContribution
                {
                    ProviderType = "openlibrary",
                    Title = "Thorin Oakenshield",
                    ExternalIds = { new ExternalId { Source = "openlibrary", Value = "dwalin" } }
                },
                new MetadataContribution
                {
                    ProviderType = "user",
                    Images =
                    {
                        new Image
                        {
                            Type = "portrait",
                            Url = new Uri("https://example.com/thorin.jpg"),
                            IsPrimary = true
                        }
                    }
                }
            ]);
        _subject = new OpenLibraryMisPairedSubjectDiagnostic(_subjects.Object, metadata.Object);
    }

    [Test]
    public async Task RunAsync_ListsMisPairedSubjectsAndEditedDuplicates_WhenStoredKeysAreChecked()
    {
        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(
            findings.Select(finding => (finding.Title, finding.AffectedEntity, finding.RepairId)),
            Is.EquivalentTo(
            [
                (
                    "Bilbo Baggins has a mis-paired Open Library key",
                    "Subject:00000000-0000-0000-0000-000000000001",
                    "openlibrary.content.delete-mis-paired-subject"),
                (
                    "Middle-earth has a mis-paired Open Library key",
                    "Subject:00000000-0000-0000-0000-000000000004",
                    (string?)null),
                (
                    "AC/DC is a duplicate subject",
                    "Subject:00000000-0000-0000-0000-000000000005",
                    (string?)null),
                (
                    "Thorin Oakenshield has a mis-paired Open Library key",
                    "Subject:00000000-0000-0000-0000-000000000007",
                    (string?)null),
            ]));
    }

    [Test]
    public async Task RunAsync_StatesThatOneOtherSubjectHasTheSameName_WhenAnEditedDuplicateHasOneOther()
    {
        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(
            findings.Single(finding => finding.Title == "AC/DC is a duplicate subject").Detail,
            Is.EqualTo("The person subject AC/DC (ac_dc) has user edits, and AC/DC (acdc) has the same name."));
    }

    [Test]
    public async Task RunAsync_QueriesOnlyOpenLibraryPersonPlaceAndTimeSubjects_WhenSubjectsAreListed()
    {
        // Act
        await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        var queries = _subjects.Invocations.Select(invocation => (SubjectQuery)invocation.Arguments[0]).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(queries, Is.Not.Empty);
            Assert.That(queries.Select(query => query.Kinds), Is.All.EquivalentTo(new[] { "person", "place", "time" }));
            Assert.That(
                queries.Select(query => query.ExternalId!.Source.And!.Single().Value),
                Is.All.EqualTo("openlibrary"));
        }
    }

    [Test]
    public async Task RunAsync_ListsAMisPairedSubjectFromTheSecondPage_WhenTheSubjectsSpanTwoPages()
    {
        // Arrange
        _subjects
            .Setup(service => service.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasUserContributions == null && query.HasTags == null),
                It.Is<PagingParameter>(paging => paging.PageNumber == 1),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject>
            {
                Items = [new() { Id = BalinId, Kind = "person" }],
                TotalItems = 501
            });
        _subjects
            .Setup(service => service.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasUserContributions == null && query.HasTags == null),
                It.Is<PagingParameter>(paging => paging.PageNumber == 2),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject>
            {
                Items = [new() { Id = BilboId, Kind = "person" }],
                TotalItems = 501
            });

        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(
            findings.Select(finding => finding.AffectedEntity),
            Is.EquivalentTo(["Subject:00000000-0000-0000-0000-000000000001"]));
    }

    [Test]
    public async Task RunAsync_StatesTheKeyAndTheUserEdits_WhenAnEditedSubjectIsMisPaired()
    {
        // Act
        var findings = await _subject.RunAsync(DiagnosticContext.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(
            findings.Single(finding => finding.AffectedEntity == "Subject:00000000-0000-0000-0000-000000000004")
                .Detail,
            Is.EqualTo("The place subject Middle-earth holds the Open Library key mordor, which does not match its " +
                "name. The subject has user edits."));
    }
}
