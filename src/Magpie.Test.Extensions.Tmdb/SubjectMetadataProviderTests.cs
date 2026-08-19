using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tmdb;
using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Extensions.Tmdb.QueryEngine;
using Magpie.Models.Data;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tmdb;

[TestFixture]
public class SubjectMetadataProviderTests
{
    private Mock<ITmdbClient> _client = null!;
    private ISubjectMetadataProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _client = new Mock<ITmdbClient>();
        var resolver = new Mock<ISettingsResolver>();
        resolver.Setup(subject => subject.AcquireContextAsync(
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestContext { ApiKey = "key", Language = "en-US" });
        var cache = new Mock<IQueryCoordinator>();
        cache.Setup(subject => subject.GetImageBaseUrlAsync(
                It.IsAny<RequestContext>(),
                It.IsAny<ITmdbClient>(),
                It.IsAny<ILogger>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://image.tmdb.org/t/p/");
        _provider = new SubjectMetadataProvider(
            resolver.Object,
            _client.Object,
            cache.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<SubjectMetadataProvider>.Instance);
    }

    [Test]
    public async Task TryGetMetadataAsync_ReturnsNull_WhenSubjectKindIsUnsupported()
    {
        // Arrange
        var subject = new Subject { Id = new Guid("11111111-1111-1111-1111-111111111111"), Kind = "character" };

        // Act
        var contribution = await _provider.TryGetMetadataAsync(
            subject, [], "en-US", default).ConfigureAwait(false);

        // Assert
        Assert.That(contribution, Is.Null);
        _client.VerifyNoOtherCalls();
    }

    [TestCase(null)]
    [TestCase("abc")]
    public async Task TryGetMetadataAsync_ReturnsNull_WhenCompanyIdIsMissingOrNonNumeric(string? tmdbId)
    {
        // Arrange
        var subject = new Subject { Id = new Guid("22222222-2222-2222-2222-222222222222"), Kind = "studio" };
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        if (tmdbId is not null)
        {
            existing.ExternalIds.Add(new ExternalId { Source = "tmdb", Value = tmdbId });
        }

        // Act
        var contribution = await _provider.TryGetMetadataAsync(
            subject, [existing], "en-US", default).ConfigureAwait(false);

        // Assert
        Assert.That(contribution, Is.Null);
        _client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task TryGetMetadataAsync_MapsCompanyDetail_WhenStudioSubjectCarriesCompanyId()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<CompanyDetailDto>(
                new RequestContext { ApiKey = "key", Language = "en-US" },
                "3/company/1",
                null,
                ResponseProfile.Detail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompanyDetailDto
            {
                Id = 1,
                Name = "Lucasfilm Ltd.",
                Description = "A film studio.",
                Headquarters = "San Francisco, California",
                Homepage = "https://www.lucasfilm.com",
                OriginCountry = "US",
                LogoPath = "/logo.png",
                ParentCompany = new ParentCompanyDto { Id = 2, Name = "The Walt Disney Company" }
            });
        var subject = new Subject { Id = new Guid("33333333-3333-3333-3333-333333333333"), Kind = "studio" };
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId { Source = "tmdb", Value = "1" });

        // Act
        var contribution = await _provider.TryGetMetadataAsync(
            subject, [existing], "en-US", default).ConfigureAwait(false);

        // Assert
        Assert.That(contribution, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contribution!.Source, Is.EqualTo("tmdb:company:1"));
            Assert.That(contribution.Title, Is.EqualTo("Lucasfilm Ltd."));
            Assert.That(contribution.Overview, Is.EqualTo("A film studio."));
            Assert.That(contribution.ExternalIds.Single().Source, Is.EqualTo("tmdb"));
            Assert.That(
                contribution.References.Single(reference => reference.Kind == "official").Url.ToString(),
                Is.EqualTo("https://www.lucasfilm.com/"));
            Assert.That(contribution.Images.Single().Type, Is.EqualTo("logo"));
            Assert.That(
                contribution.CustomMetadata.ToDictionary(value => value.Key, value => value.Value),
                Is.EquivalentTo(new Dictionary<string, string?>
                {
                    ["originCountry"] = "US",
                    ["headquarters"] = "San Francisco, California",
                    ["parentCompany"] = "The Walt Disney Company",
                    ["parentCompanyId"] = "2"
                }));
        }
    }

    [Test]
    public async Task TryGetMetadataAsync_MapsPersonDetail_WhenPersonSubjectCarriesPersonId()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<PersonDetailDto>(
                new RequestContext { ApiKey = "key", Language = "en-US" },
                "3/person/819",
                It.IsAny<IReadOnlyList<(string Key, string Value)>?>(),
                ResponseProfile.Detail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonDetailDto
            {
                Id = 819,
                Name = "Edward Norton",
                Biography = "An American actor.",
                Birthday = "1969-08-18",
                PlaceOfBirth = "Boston, Massachusetts",
                Gender = 2,
                KnownForDepartment = "Acting",
                ProfilePath = "/profile.jpg",
                AlsoKnownAs = ["Ed Norton"],
                ExternalIds = new ExternalIdsDto { ImdbId = "nm0001570", WikidataId = "Q170572" }
            });
        var subject = new Subject { Id = new Guid("55555555-5555-5555-5555-555555555555"), Kind = "person" };
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId { Source = "tmdb", Value = "819" });

        // Act
        var contribution = await _provider.TryGetMetadataAsync(
            subject, [existing], "en-US", default).ConfigureAwait(false);

        // Assert
        Assert.That(contribution, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contribution!.Source, Is.EqualTo("tmdb:person:819"));
            Assert.That(contribution.Title, Is.EqualTo("Edward Norton"));
            Assert.That(contribution.Overview, Is.EqualTo("An American actor."));
            Assert.That(contribution.BirthDate, Is.EqualTo(new DateTime(1969, 8, 18)));
            Assert.That(contribution.DeathDate, Is.Null);
            Assert.That(
                contribution.ExternalIds.Select(externalId => externalId.Source),
                Is.EqualTo(["tmdb", "imdb", "wikidata"]));
            Assert.That(contribution.Images.Single().Type, Is.EqualTo("profile"));
            Assert.That(
                contribution.Images.Single().Url.ToString(),
                Is.EqualTo("https://image.tmdb.org/t/p/w185/profile.jpg"));
            Assert.That(contribution.AlternateTexts.Single().Kind, Is.EqualTo("name"));
            Assert.That(contribution.AlternateTexts.Single().Text, Is.EqualTo("Ed Norton"));
            Assert.That(
                contribution.CustomMetadata.ToDictionary(value => value.Key, value => value.Value),
                Is.EquivalentTo(new Dictionary<string, string?>
                {
                    ["gender"] = "male",
                    ["placeOfBirth"] = "Boston, Massachusetts",
                    ["knownForDepartment"] = "Acting"
                }));
        }
    }

    [Test]
    public async Task TryGetMetadataAsync_UsesNetworkEndpoint_WhenNetworkSubjectCarriesNetworkId()
    {
        // Arrange
        _client.Setup(client => client.GetAsync<CompanyDetailDto>(
                new RequestContext { ApiKey = "key", Language = "en-US" },
                "3/network/49",
                null,
                ResponseProfile.Detail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompanyDetailDto
            {
                Id = 49,
                Name = "HBO",
                Headquarters = "New York City, New York",
                Homepage = "https://www.hbo.com",
                OriginCountry = "US",
                LogoPath = "/hbo.png"
            });
        var subject = new Subject { Id = new Guid("44444444-4444-4444-4444-444444444444"), Kind = "network" };
        var existing = new MetadataContribution { ProviderType = Constants.ProviderType };
        existing.ExternalIds.Add(new ExternalId { Source = "tmdb", Value = "49" });

        // Act
        var contribution = await _provider.TryGetMetadataAsync(
            subject, [existing], "en-US", default).ConfigureAwait(false);

        // Assert
        Assert.That(contribution, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contribution!.Source, Is.EqualTo("tmdb:network:49"));
            Assert.That(contribution.Title, Is.EqualTo("HBO"));
            Assert.That(contribution.ExternalIds.Single().Source, Is.EqualTo("tmdb"));
            Assert.That(
                contribution.CustomMetadata.Select(value => value.Key),
                Is.EquivalentTo(["originCountry", "headquarters"]));
        }
    }
}
