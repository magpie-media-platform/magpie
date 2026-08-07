using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary;
using Magpie.Extensions.OpenLibrary.Mapping;
using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Models.Data;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.OpenLibrary;

[TestFixture]
public sealed class AuthorSubjectMetadataProviderTests
{
    [Test]
    public async Task TryGetMetadataAsync_HydratedContribution_WhenTheSubjectCarriesAnAuthorOlid()
    {
        // Arrange
        var searchEngine = new Mock<ISearchEngine>(MockBehavior.Strict);
        searchEngine
            .Setup(value => value.GetAuthorAsync("OL1A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthorDto
            {
                Name = "Ursula K. Le Guin",
                Bio = JsonSerializer.SerializeToElement("Writer"),
                BirthDate = "October 21, 1929",
                Photos = [42]
            });
        var provider = new AuthorSubjectMetadataProvider(
            searchEngine.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<AuthorSubjectMetadataProvider>.Instance);
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "OL1A"
        });

        // Act
        var contribution = await provider.TryGetMetadataAsync(
            new Subject { Id = Guid.NewGuid(), Kind = Constants.PersonSubjectKind },
            [existing],
            string.Empty).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contribution?.Title, Is.EqualTo("Ursula K. Le Guin"));
            Assert.That(contribution?.Overview, Is.EqualTo("Writer"));
            Assert.That(contribution?.BirthDate, Is.EqualTo(new DateTime(1929, 10, 21, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(contribution?.ExternalIds.Single().Value, Is.EqualTo("OL1A"));
            Assert.That(
                contribution?.Images.Single().Url.AbsoluteUri,
                Is.EqualTo("https://covers.openlibrary.org/a/id/42-L.jpg?default=false"));
        }

        searchEngine.Verify(value => value.GetAuthorAsync("OL1A", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task TryGetMetadataAsync_NullWithoutRemoteCall_WhenTheSubjectKindIsNotPerson()
    {
        // Arrange
        var searchEngine = new Mock<ISearchEngine>(MockBehavior.Strict);
        var provider = new AuthorSubjectMetadataProvider(
            searchEngine.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<AuthorSubjectMetadataProvider>.Instance);

        // Act
        var contribution = await provider.TryGetMetadataAsync(
            new Subject { Id = Guid.NewGuid(), Kind = "place" },
            [],
            string.Empty).ConfigureAwait(false);

        // Assert
        Assert.That(contribution, Is.Null);
    }
}
