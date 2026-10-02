using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary;
using Magpie.Models.Arguments;
using Magpie.Models.Data;
using Magpie.Models.Paging;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.OpenLibrary;

[TestFixture]
public sealed class OpenLibraryMisPairedSubjectRepairTests
{
    private static readonly Guid SubjectId = new("00000000-0000-0000-0000-000000000001");

    private Mock<ISubjectService> _subjects = null!;
    private Mock<IMetadataService> _metadata = null!;
    private OpenLibraryMisPairedSubjectRepair _subject = null!;

    [SetUp]
    public void SetUp()
    {
        _subjects = new Mock<ISubjectService>();
        _subjects
            .Setup(service => service.QueryAsync(
                It.IsAny<SubjectQuery>(),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject> { Items = [], TotalItems = 0 });
        _subjects
            .Setup(service => service.DeleteAsync(SubjectId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _metadata = new Mock<IMetadataService>();
        _subject = new OpenLibraryMisPairedSubjectRepair(
            _subjects.Object,
            _metadata.Object,
            NullLogger<OpenLibraryMisPairedSubjectRepair>.Instance);
    }

    [TestCase("Bilbo Baggins", "balin", true, 1)]
    [TestCase("Balin", "balin", true, 0)]
    [TestCase("AC/DC", "ac_dc", true, 0)]
    public async Task RunAsync_DeletesTheSubjectOnlyWhenItIsMisPaired_WhenTheSubjectHasNoUserEdits(
        string name,
        string key,
        bool expectedSuccess,
        int expectedDeletes)
    {
        // Arrange
        _metadata
            .Setup(service => service.GetSubjectContributionsAsync(SubjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataContribution
                {
                    ProviderType = "openlibrary",
                    Title = name,
                    ExternalIds = { new ExternalId { Source = "openlibrary", Value = key } }
                }
            ]);

        // Act
        var result = await _subject.RunAsync(new DiagnosticFinding
        {
            DiagnosticCheckId = "openlibrary.content.mis-paired-subject",
            Title = "test",
            Detail = "test",
            AffectedEntity = "Subject:00000000-0000-0000-0000-000000000001",
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.EqualTo(expectedSuccess));
            _subjects.Verify(
                service => service.DeleteAsync(SubjectId, null, It.IsAny<CancellationToken>()),
                Times.Exactly(expectedDeletes));
        }
    }

    [Test]
    public async Task RunAsync_FailsWithoutDeleting_WhenTheMisPairedSubjectHasUserEdits()
    {
        // Arrange
        _metadata
            .Setup(service => service.GetSubjectContributionsAsync(SubjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataContribution
                {
                    ProviderType = "openlibrary",
                    Title = "Bilbo Baggins",
                    ExternalIds = { new ExternalId { Source = "openlibrary", Value = "balin" } }
                }
            ]);
        _subjects
            .Setup(service => service.QueryAsync(
                It.Is<SubjectQuery>(query => query.HasUserContributions == true),
                It.IsAny<PagingParameter>(),
                It.IsAny<SortParameter?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedList<Subject>
            {
                Items = [new() { Id = SubjectId, Kind = "person" }],
                TotalItems = 1
            });

        // Act
        var result = await _subject.RunAsync(new DiagnosticFinding
        {
            DiagnosticCheckId = "openlibrary.content.mis-paired-subject",
            Title = "test",
            Detail = "test",
            AffectedEntity = "Subject:00000000-0000-0000-0000-000000000001",
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("The subject Bilbo Baggins has user edits."));
            _subjects.Verify(
                service => service.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task RunAsync_FailsWithoutDeleting_WhenTheSubjectNoLongerExists()
    {
        // Arrange
        _metadata
            .Setup(service => service.GetSubjectContributionsAsync(SubjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MetadataContribution>?)null);

        // Act
        var result = await _subject.RunAsync(new DiagnosticFinding
        {
            DiagnosticCheckId = "openlibrary.content.mis-paired-subject",
            Title = "test",
            Detail = "test",
            AffectedEntity = "Subject:00000000-0000-0000-0000-000000000001",
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("The subject no longer exists."));
            _subjects.Verify(
                service => service.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task RunAsync_DeletesNothing_WhenTheSubjectHoldsNoOpenLibraryKey()
    {
        // Arrange
        _metadata
            .Setup(service => service.GetSubjectContributionsAsync(SubjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MetadataContribution { ProviderType = "openlibrary", Title = "Bilbo Baggins" }]);

        // Act
        var result = await _subject.RunAsync(new DiagnosticFinding
        {
            DiagnosticCheckId = "openlibrary.content.mis-paired-subject",
            Title = "test",
            Detail = "test",
            AffectedEntity = "Subject:00000000-0000-0000-0000-000000000001",
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Message, Is.EqualTo("The subject no longer holds an Open Library key."));
            _subjects.Verify(
                service => service.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Test]
    public async Task RunAsync_FailsWithoutDeleting_WhenTheUserChoseAPrimaryImage()
    {
        // Arrange
        _metadata
            .Setup(service => service.GetSubjectContributionsAsync(SubjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new MetadataContribution
                {
                    ProviderType = "openlibrary",
                    Title = "Bilbo Baggins",
                    ExternalIds = { new ExternalId { Source = "openlibrary", Value = "balin" } }
                },
                new MetadataContribution
                {
                    ProviderType = "user",
                    Images =
                    {
                        new Image
                        {
                            Type = "portrait",
                            Url = new Uri("https://example.com/bilbo.jpg"),
                            IsPrimary = true
                        }
                    }
                }
            ]);

        // Act
        var result = await _subject.RunAsync(new DiagnosticFinding
        {
            DiagnosticCheckId = "openlibrary.content.mis-paired-subject",
            Title = "test",
            Detail = "test",
            AffectedEntity = "Subject:00000000-0000-0000-0000-000000000001",
        }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("The subject Bilbo Baggins has user edits."));
            _subjects.Verify(
                service => service.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
