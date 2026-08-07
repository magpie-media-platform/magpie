using System;
using System.Collections.Generic;

using Magpie.Abstractions.Releases;
using Magpie.Abstractions.Repositories;
using Magpie.Core.Releases;
using Magpie.Models.Data;
using Magpie.Models.Releases;
using Magpie.Sdk.Content;
using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Core.Releases;

[TestFixture]
public sealed class ReleaseFactConfirmedIdentityTests
{
    private IReleaseEvaluationService _subject = null!;
    private Mock<IReleaseMatchContentTypeDefinitionProvider> _provider = null!;
    private ReleaseEvaluationContext _context = null!;
    private Release _release = null!;

    [SetUp]
    public void SetUp()
    {
        _provider = new Mock<IReleaseMatchContentTypeDefinitionProvider>();
        _provider
            .Setup(value => value.IsMatch(
                It.IsAny<ContentTarget>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<ContentFact>>>(),
                It.IsAny<Release>(),
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(ReleaseMatchResult.Indeterminate);
        var registry = new Mock<IContentTypeRegistry>();
        registry.Setup(value => value.FindProviderFor("tv")).Returns(_provider.Object);
        _subject = new ReleaseEvaluationService(
            Mock.Of<IContentHierarchyItemRepository>(),
            Mock.Of<IMaterializedMetadataRepository>(),
            Mock.Of<IReleaseRepository>(),
            Mock.Of<IDownloadRepository>(),
            Mock.Of<IQualityTierListRepository>(),
            registry.Object,
            [],
            TimeProvider.System,
            NullLogger<ReleaseEvaluationService>.Instance);
        var show = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "show",
            Slug = "the-show",
            Name = "The Show",
        };
        var episode = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ParentId = show.Id,
            ContentType = "tv",
            Level = "episode",
            Slug = "rollercoaster",
            Name = "Rollercoaster",
            Ordinal = 1,
        };
        var showContribution = new MetadataContribution { ProviderType = "test" };
        showContribution.ExternalIds.Add(new ExternalId { Source = "imdb", Value = "tt-show" });
        var showMetadata = new MetadataContainer();
        showMetadata.Contributions.Add(showContribution);
        var episodeContribution = new MetadataContribution { ProviderType = "test" };
        episodeContribution.ExternalIds.Add(new ExternalId { Source = "imdb", Value = "tt-episode" });
        var episodeMetadata = new MetadataContainer();
        episodeMetadata.Contributions.Add(episodeContribution);
        _context = new ReleaseEvaluationContext
        {
            ContentHierarchyItemId = episode.Id,
            Target = new ContentTarget
            {
                Item = episode,
                Lineage = [episode, show],
                Metadata = new Dictionary<Guid, MaterializedMetadata>
                {
                    [episode.Id] = new() { ExternalIds = episodeContribution.ExternalIds },
                    [show.Id] = new() { ExternalIds = showContribution.ExternalIds },
                },
            },
            ContentFacts =
            [
                [
                    new ContentFact
                    {
                        Facets = new Dictionary<string, string> { ["imdbid"] = "tt-episode" },
                    },
                ],
                [
                    new ContentFact
                    {
                        Facets = new Dictionary<string, string> { ["imdbid"] = "tt-show" },
                    },
                ],
            ],
        };
        _release = new Release { Protocol = "torrent", Title = string.Empty };
    }

    [TestCase("tt-episode", ReleaseMatchResult.Identity)]
    [TestCase("tt-show", ReleaseMatchResult.Indeterminate)]
    [TestCase("tt-someone-elses-problem", ReleaseMatchResult.Indeterminate)]
    public void AssessMatch_Result_WhenStandaloneIdentifierFactIsEvaluated(
        string identifier,
        ReleaseMatchResult expected)
    {
        // Arrange
        IReadOnlyList<ContentFact> releaseFacts =
        [new ContentFact { Facets = new Dictionary<string, string> { ["imdbid"] = identifier }, },];

        // Act
        var result = _subject.AssessMatch(_release, releaseFacts, _context);

        // Assert
        Assert.That(result.MatchResult, Is.EqualTo(expected));
    }

    [Test]
    public void AssessMatch_ReturnsIndeterminate_WhenTargetIdentifierIsPartOfConjunction()
    {
        // Arrange
        IReadOnlyList<ContentFact> releaseFacts =
        [
            new ContentFact
            {
                Facets = new Dictionary<string, string> { ["imdbid"] = "tt-episode" },
            },
            new ContentFact
            {
                Facets = new Dictionary<string, string>
                {
                    ["season"] = "1",
                    ["episode"] = "1",
                },
            },
        ];

        // Act
        var result = _subject.AssessMatch(_release, releaseFacts, _context);

        // Assert
        Assert.That(result.MatchResult, Is.EqualTo(ReleaseMatchResult.Indeterminate));
    }

    [TestCase("Rollercoaster", ReleaseMatchResult.NoMatch, ReleaseMatchResult.Confident)]
    [TestCase("Someone Else's Rollercoaster", ReleaseMatchResult.NoMatch, ReleaseMatchResult.NoMatch)]
    [TestCase("Someone Else's Rollercoaster", ReleaseMatchResult.Confident, ReleaseMatchResult.Confident)]
    public void AssessMatch_Result_WhenGenericAndProviderMatchesAreCombined(
        string releaseTitle,
        ReleaseMatchResult providerMatch,
        ReleaseMatchResult expected)
    {
        // Arrange
        var release = _release with { Title = releaseTitle };
        var context = _context with
        {
            ContentFacts = [[new ContentFact { Value = "Rollercoaster" }]],
        };
        _provider
            .Setup(value => value.IsMatch(
                context.Target!,
                context.ContentFacts,
                release,
                It.IsAny<IReadOnlyList<ContentFact>>()))
            .Returns(providerMatch);

        // Act
        var result = _subject.AssessMatch(release, [], context);

        // Assert
        Assert.That(result.MatchResult, Is.EqualTo(expected));
    }
}
