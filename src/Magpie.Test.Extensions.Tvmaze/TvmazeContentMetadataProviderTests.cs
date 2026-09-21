using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Tvmaze;
using Magpie.Extensions.Tvmaze.Mapping;
using Magpie.Models;
using Magpie.Models.Data;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tvmaze;

[TestFixture]
public class TvmazeContentMetadataProviderTests
{
    [Test]
    public void ProviderType_ReturnsTvmaze_WhenProviderHandlesTelevision()
    {
        // Arrange
        var subject = new ContentMetadataProvider(
            Mock.Of<IClient>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);

        // Act
        var providerType = subject.ProviderType;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(providerType, Is.EqualTo("tvmaze"));
            Assert.That(subject.SupportedContentTypes, Is.EquivalentTo(["tv"]));
        }
    }

    [Test]
    public async Task TryGetMetadataAsync_FreshEpisodeContribution_WhenItemIsEpisode()
    {
        // Arrange
        var utcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = new MetadataContribution
        {
            Id = Guid.NewGuid(),
            MetadataContainerId = Guid.NewGuid(),
            Ordinal = 3,
            ProviderType = Constants.ProviderType,
            Source = "tvmaze:episode:4952",
            Title = "Stale title",
            Overview = "Stale overview",
            PublishDate = new DateTime(2011, 4, 17, 0, 0, 0, DateTimeKind.Utc),
            Duration = TimeSpan.FromMinutes(60),
            SequenceNumber = 1,
            DateLastRefreshed = utcNow.AddDays(-1)
        };
        existing.ExternalIds.Add(new ExternalId
        {
            Id = Guid.NewGuid(),
            MetadataContributionId = existing.Id,
            Source = Constants.ProviderType,
            Value = "4952",
            Ordinal = 0
        });
        existing.Images.Add(new Image
        {
            Id = Guid.NewGuid(),
            MetadataContributionId = existing.Id,
            Type = "still",
            MimeType = "image/jpeg",
            Url = new Uri("https://static.tvmaze.com/uploads/stale-episode.jpg"),
            Source = Constants.ProviderType,
            Language = "en",
            Width = 1280,
            Height = 720,
            IsPrimary = true,
            Ordinal = 2
        });
        var item = new ContentHierarchyItem
        {
            Id = Guid.NewGuid(),
            ContentType = "tv",
            Level = "episode",
            Slug = "s01e01",
            Name = "Winter Is Coming"
        };
        var client = new Mock<IClient>();
        client
            .Setup(value => value.GetEpisodeAsync(4952, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EpisodeDto
            {
                Id = 4952,
                Name = "Winter Is Coming",
                Season = 1,
                Number = 2,
                AirDate = "2011-04-24",
                Runtime = 55,
                Summary = "<p>Fresh episode overview.</p>",
                Image = new ImageReferenceDto
                {
                    Original = "https://static.tvmaze.com/uploads/fresh-episode.jpg"
                }
            });
        var subject = new ContentMetadataProvider(
            client.Object,
            new FakeTimeProvider(new DateTimeOffset(utcNow, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
        var request = new MetadataFetchRequest
        {
            Item = item,
            ExistingMetadata = [existing],
            DefaultLanguagePreference = "en"
        };

        // Act
        var result = await subject.TryGetMetadataAsync(request, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Contribution, Is.Not.SameAs(existing));
            Assert.That(result.Contribution!.Id, Is.EqualTo(Guid.Empty));
            Assert.That(result.Contribution.Source, Is.EqualTo("tvmaze:episode:4952"));
            Assert.That(result.Contribution.Title, Is.EqualTo("Winter Is Coming"));
            Assert.That(result.Contribution.Overview, Is.EqualTo("Fresh episode overview."));
            Assert.That(
                result.Contribution.PublishDate,
                Is.EqualTo(new DateTime(2011, 4, 24, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(result.Contribution.Duration, Is.EqualTo(TimeSpan.FromMinutes(55)));
            Assert.That(result.Contribution.SequenceNumber, Is.EqualTo(2));
            Assert.That(result.Contribution.DateLastRefreshed, Is.EqualTo(utcNow));
            Assert.That(result.Contribution.ExternalIds.Single(), Is.Not.SameAs(existing.ExternalIds.Single()));
            Assert.That(result.Contribution.ExternalIds.Single().Value, Is.EqualTo("4952"));
            Assert.That(result.Contribution.Images.Single(), Is.Not.SameAs(existing.Images.Single()));
            Assert.That(result.Contribution.Images.Single().Type, Is.EqualTo("still"));
            Assert.That(
                result.Contribution.Images.Single().Url.ToString(),
                Is.EqualTo("https://static.tvmaze.com/uploads/fresh-episode.jpg"));
        }

        client.Verify(
            value => value.GetEpisodeAsync(4952, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task TryGetMetadataAsync_NullWithoutClientCall_WhenEpisodeIdIsUnknown()
    {
        // Arrange
        var client = new Mock<IClient>();
        var subject = new ContentMetadataProvider(
            client.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "tv",
                Level = "episode",
                Slug = "s01e01",
                Name = "Winter Is Coming"
            },
            ExistingMetadata = [new MetadataContribution { ProviderType = Constants.ProviderType }],
            DefaultLanguagePreference = "en"
        };

        // Act
        var result = await subject.TryGetMetadataAsync(request, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Null);
        client.VerifyNoOtherCalls();
    }

    [Test]
    public async Task TryGetMetadataAsync_NullWithoutFreshTimestamp_WhenEpisodeIsNotFound()
    {
        // Arrange
        var lastRefreshed = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var existing = new MetadataContribution
        {
            ProviderType = Constants.ProviderType,
            DateLastRefreshed = lastRefreshed
        };
        existing.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "4952",
            Ordinal = 0
        });
        var client = new Mock<IClient>();
        client
            .Setup(value => value.GetEpisodeAsync(4952, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EpisodeDto?)null);
        var subject = new ContentMetadataProvider(
            client.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "tv",
                Level = "episode",
                Slug = "s01e01",
                Name = "Winter Is Coming"
            },
            ExistingMetadata = [existing],
            DefaultLanguagePreference = "en"
        };

        // Act
        var result = await subject.TryGetMetadataAsync(request, default).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            Assert.That(existing.DateLastRefreshed, Is.EqualTo(lastRefreshed));
        }

        client.Verify(
            value => value.GetEpisodeAsync(4952, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task TryGetMetadataAsync_EmbeddedResourcesWithOneShowCall_WhenShowIdIsKnown()
    {
        // Arrange
        var show = new ShowDto
        {
            Id = 82,
            Name = "Game of Thrones",
            Type = "Scripted",
            Rating = new RatingDto { Average = 9.2 },
            Schedule = new ScheduleDto { Time = "21:00", Days = ["Sunday"] },
            Embedded = new ShowEmbeddedDto
            {
                Images =
                [
                    new ImageDto
                    {
                        Id = 1,
                        Type = "poster",
                        Main = true,
                        Resolutions = new ImageResolutionsDto
                        {
                            Original = new ImageResolutionDto
                            {
                                Url = "https://static.tvmaze.com/uploads/poster.jpg"
                            }
                        }
                    }
                ],
                Akas =
                [
                    new AkaDto { Name = "Игра престолов" }
                ],
                Seasons = [new SeasonDto { Id = 1, Number = 1, Name = "Season 1" }],
                Episodes =
                [
                    new EpisodeDto
                    {
                        Id = 4952,
                        Name = "Winter Is Coming",
                        Season = 1,
                        Number = 1
                    }
                ]
            }
        };
        var client = new Mock<IClient>();
        client
            .Setup(value => value.GetShowWithEmbedsAsync(
                82, new[] { "episodes", "images", "akas", "seasons" }, It.IsAny<CancellationToken>()))
            .ReturnsAsync(show);
        var subject = new ContentMetadataProvider(
            client.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
        var metadata = new MetadataContribution { ProviderType = "tvmaze" };
        metadata.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "82",
            Ordinal = 0
        });
        var request = new MetadataFetchRequest
        {
            Item = new ContentHierarchyItem
            {
                Id = Guid.NewGuid(),
                ContentType = "tv",
                Level = "show",
                Slug = "game-of-thrones",
                Name = "Game of Thrones"
            },
            ExistingMetadata = [metadata],
            DefaultLanguagePreference = "en"
        };

        // Act
        var result = await subject.TryGetMetadataAsync(request, default).ConfigureAwait(false);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Contribution!.Images, Has.Count.EqualTo(1));
            Assert.That(result.Contribution.AlternateTexts.Single().Language, Is.Null);
            Assert.That(
                result.Contribution.CustomMetadata.Select(value => (value.Key, value.Value)),
                Is.EqualTo(
                [
                    ("rating", "9.2"),
                    ("type", "Scripted"),
                    ("schedule", "Sunday at 21:00")
                ]));
            Assert.That(result.Children, Has.Count.EqualTo(1));
            Assert.That(result.Children[0].Children, Has.Count.EqualTo(1));
        }

        client.Verify(
            value => value.GetShowWithEmbedsAsync(
                82, new[] { "episodes", "images", "akas", "seasons" }, It.IsAny<CancellationToken>()),
            Times.Once);
        client.Verify(
            value => value.GetSeasonsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
