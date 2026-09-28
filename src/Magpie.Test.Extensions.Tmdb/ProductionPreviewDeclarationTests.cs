using System;
using System.Linq;

using Magpie.Extensions.Movies;
using Magpie.Extensions.Tmdb.Mapping;
using Magpie.Extensions.Tv;
using Magpie.Models.Enums;
using Magpie.Sdk.Content;

using Microsoft.Extensions.DependencyInjection;

using NUnit.Framework;

namespace Magpie.Test.Extensions.Tmdb;

[TestFixture]
public class ProductionPreviewDeclarationTests
{
    private IContentTypeDefinitionProvider[] _definitions = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new MoviesExtension().RegisterServices(services);
        new TvExtension().RegisterServices(services);
        _definitions = [.. services.BuildServiceProvider().GetServices<IContentTypeDefinitionProvider>()];
    }

    [Test]
    public void Definition_DeclaresPreviews_WhenProductionMovieMetadataContainsPreview()
    {
        // Arrange
        var contribution = new MovieDetailDto
        {
            Id = 550,
            Title = "Fight Club",
            Videos = new VideosDto
            {
                Results = [new VideoDto { Key = "movieTrailer", Site = "YouTube", Type = "Trailer" }]
            }
        }.ToContribution("https://image.tmdb.org/t/p/", DateTime.UtcNow);

        // Act
        var level = _definitions.Single(definition => definition.Definition.Slug == "movie")
            .Definition.LevelDefinitions.Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contribution.Previews, Has.Count.EqualTo(1));
            Assert.That(level.Slug, Is.EqualTo("movie"));
            Assert.That(level.SupportedMetadataFields.HasFlag(MetadataField.Previews), Is.True);
        }
    }

    [Test]
    public void Definition_DeclaresPreviewsOnlyForShow_WhenProductionTvMetadataContainsPreview()
    {
        // Arrange
        var contribution = new TvDetailDto
        {
            Id = 1396,
            Name = "Breaking Bad",
            Videos = new VideosDto
            {
                Results = [new VideoDto { Key = "showTrailer", Site = "YouTube", Type = "Trailer" }]
            }
        }.ToContribution("https://image.tmdb.org/t/p/", DateTime.UtcNow);

        // Act
        var levels = _definitions.Single(definition => definition.Definition.Slug == "tv").Definition.LevelDefinitions;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contribution.Previews, Has.Count.EqualTo(1));
            Assert.That(levels.Single(level => level.Slug == "show").SupportedMetadataFields
                .HasFlag(MetadataField.Previews), Is.True);
            Assert.That(levels.Single(level => level.Slug == "season").SupportedMetadataFields
                .HasFlag(MetadataField.Previews), Is.False);
            Assert.That(levels.Single(level => level.Slug == "episode").SupportedMetadataFields
                .HasFlag(MetadataField.Previews), Is.False);
        }
    }

    [Test]
    public void ToContribution_LeavesPreviewsEmpty_WhenProductionDetailsHaveNoVideos()
    {
        // Arrange
        var movie = new MovieDetailDto { Id = 550, Title = "Fight Club" };
        var show = new TvDetailDto { Id = 1396, Name = "Breaking Bad" };

        // Act
        var movieContribution = movie.ToContribution("https://image.tmdb.org/t/p/", DateTime.UtcNow);
        var showContribution = show.ToContribution("https://image.tmdb.org/t/p/", DateTime.UtcNow);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(movieContribution.Previews, Is.Empty);
            Assert.That(showContribution.Previews, Is.Empty);
        }
    }
}
