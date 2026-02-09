using System;
using System.Linq;

using Magpie.Models.Enums;
using Magpie.QueryLanguage;

using NUnit.Framework;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
public class FilterQueryTransformTests
{
    [Test]
    public void ToContentFilterCriteria_SetsNameCriterion_WhenNameEquals()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("name = \"Breaking Bad\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var criterion = item.Name.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(criterion,
                Has.Property(nameof(criterion.Operator))
                    .EqualTo(CriteriaValueOperator.Equals));
            Assert.That(criterion,
                Has.Property(nameof(criterion.Value))
                    .EqualTo("Breaking Bad"));
        }
    }

    [Test]
    public void ToContentFilterCriteria_SetsMonitoringCriterion_WhenMonitoringEquals()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("monitoring = Active");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var criterion = item.Monitoring.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(criterion,
                Has.Property(nameof(criterion.Operator))
                    .EqualTo(CriteriaValueOperator.Equals));
            Assert.That(criterion,
                Has.Property(nameof(criterion.Value))
                    .EqualTo(MonitoringIntent.Active));
        }
    }

    [Test]
    public void ToContentFilterCriteria_SetsFileSizeCriterion_WhenFileSizeGreaterThan()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("fileSize > 1000000000");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var mediaFile = criteria.MediaFiles.And.First();
        var criterion = mediaFile.FileSize.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(criterion,
                Has.Property(nameof(criterion.Operator))
                    .EqualTo(CriteriaValueOperator.GreaterThan));
            Assert.That(criterion,
                Has.Property(nameof(criterion.Value))
                    .EqualTo(1000000000L));
        }
    }

    [Test]
    public void ToContentFilterCriteria_SetsBothCriteria_WhenMultipleScalarsWithAnd()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("monitoring = Active and contentType = \"show\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var monitoring = item.Monitoring;
        var contentType = item.ContentType;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(monitoring,
                Has.Property(nameof(monitoring.And))
                    .With.Exactly(1).Items);
            Assert.That(contentType,
                Has.Property(nameof(contentType.And))
                    .With.Exactly(1).Items);
        }
    }

    [Test]
    public void ToContentFilterCriteria_SetsTitleCriterion_WhenTitleContains()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("title contains \"Star\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var meta = item.Metadata.And.First();
        var criterion = meta.Title.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(criterion,
                Has.Property(nameof(criterion.Operator))
                    .EqualTo(CriteriaValueOperator.ContainsString));
            Assert.That(criterion,
                Has.Property(nameof(criterion.Value)).EqualTo("Star"));
        }
    }

    [Test]
    public void ToContentFilterCriteria_SetsPublishDateCriterion_WhenPublishDateGreaterThanOrEqual()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("publishDate >= \"2020-01-01\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var meta = item.Metadata.And.First();
        var criterion = meta.PublishDate.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(criterion,
                Has.Property(nameof(criterion.Operator))
                    .EqualTo(CriteriaValueOperator.GreaterThanOrEqual));
            Assert.That(criterion,
                Has.Property(nameof(criterion.Value))
                    .EqualTo(new DateTime(2020, 1, 1)));
        }
    }

    [Test]
    public void ToContentFilterCriteria_SetsDurationCriterion_WhenDurationGreaterThan()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("duration > \"02:00:00\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var meta = item.Metadata.And.First();
        var criterion = meta.Duration.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(criterion,
                Has.Property(nameof(criterion.Operator))
                    .EqualTo(CriteriaValueOperator.GreaterThan));
            Assert.That(criterion,
                Has.Property(nameof(criterion.Value))
                    .EqualTo(TimeSpan.FromHours(2)));
        }
    }

    [Test]
    public void ToContentFilterCriteria_SetsIsNullOperator_WhenNullUsed()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("overview null");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var meta = item.Metadata.And.First();
        var criterion = meta.Overview.And.First();

        Assert.That(criterion,
            Has.Property(nameof(criterion.Operator))
                .EqualTo(CriteriaValueOperator.IsNull));
    }

    [Test]
    public void ToContentFilterCriteria_SetsIsNotNullOperator_WhenNotNullUsed()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("overview !null");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var meta = item.Metadata.And.First();
        var criterion = meta.Overview.And.First();

        Assert.That(criterion,
            Has.Property(nameof(criterion.Operator))
                .EqualTo(CriteriaValueOperator.IsNotNull));
    }

    [Test]
    public void ToContentFilterCriteria_SetsInOperator_WhenInUsed()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("contentType in (\"movie\", \"show\")");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var criterion = item.ContentType.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(criterion,
                Has.Property(nameof(criterion.Operator))
                    .EqualTo(CriteriaValueOperator.In));
            Assert.That(criterion,
                Has.Property(nameof(criterion.Values))
                    .With.Exactly(2).Items);
        }
    }

    [Test]
    public void ToContentFilterCriteria_SetsGenreCriterion_WhenGenreHas()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("genre has \"Action\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var meta = item.Metadata.And.First();
        var genres = meta.Genres;

        Assert.That(genres,
            Has.Property(nameof(genres.Some))
                .With.Exactly(1).Items);
        var genre = genres.Some.First();
        var nameSet = genre.Name;
        Assert.That(nameSet,
            Has.Property(nameof(nameSet.And))
                .With.Exactly(1).Items);
        var criterion = nameSet.And.First();
        Assert.That(criterion,
            Has.Property(nameof(criterion.Value))
                .EqualTo("Action"));
    }

    [Test]
    public void ToContentFilterCriteria_SetsGenreNone_WhenGenreNotHas()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("genre !has \"Horror\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var meta = item.Metadata.And.First();
        var genres = meta.Genres;

        Assert.That(genres,
            Has.Property(nameof(genres.None))
                .With.Exactly(1).Items);
        var genre = genres.None.First();
        var nameSet = genre.Name;
        Assert.That(nameSet,
            Has.Property(nameof(nameSet.And))
                .With.Exactly(1).Items);
        var criterion = nameSet.And.First();
        Assert.That(criterion,
            Has.Property(nameof(criterion.Value))
                .EqualTo("Horror"));
    }

    [Test]
    public void ToContentFilterCriteria_AddsMultipleGenres_WhenOrUsed()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("genre has \"Action\" or genre has \"Comedy\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var meta = item.Metadata.And.First();
        var genres = meta.Genres;

        Assert.That(genres,
            Has.Property(nameof(genres.Some))
                .With.Exactly(2).Items);
    }

    [Test]
    public void ToContentFilterCriteria_SetsMultipleNameCriteria_WhenGenreHasCompoundCondition()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse(
            "genres has (name startswith \"A\" and name endswith \"ion\")");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var meta = item.Metadata.And.First();
        var genres = meta.Genres;

        Assert.That(genres,
            Has.Property(nameof(genres.Some))
                .With.Exactly(1).Items);
        var genre = genres.Some.First();
        var nameSet = genre.Name;
        Assert.That(nameSet,
            Has.Property(nameof(nameSet.And))
                .With.Exactly(2).Items);
    }

    [Test]
    public void ToContentFilterCriteria_SetsAwardCriteria_WhenComplexAwardCondition()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse(
            "awards has (body = \"Academy Awards\" and name = \"Best Picture\" and year >= 2000)");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var meta = item.Metadata.And.First();
        var awards = meta.Awards;

        Assert.That(awards,
            Has.Property(nameof(awards.Some))
                .With.Exactly(1).Items);
        var award = awards.Some.First();

        var bodySet = award.Body;
        var nameSet = award.Name;
        var yearSet = award.Year;
        var bodyCriterion = bodySet.And.First();
        var nameCriterion = nameSet.And.First();
        var yearCriterion = yearSet.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bodyCriterion,
                Has.Property(nameof(bodyCriterion.Value))
                    .EqualTo("Academy Awards"));
            Assert.That(nameCriterion,
                Has.Property(nameof(nameCriterion.Value))
                    .EqualTo("Best Picture"));
            Assert.That(yearCriterion,
                Has.Property(nameof(yearCriterion.Operator))
                    .EqualTo(CriteriaValueOperator.GreaterThanOrEqual));
            Assert.That(yearCriterion,
                Has.Property(nameof(yearCriterion.Value))
                    .EqualTo(2000));
        }
    }

    [Test]
    public void ToContentFilterCriteria_SetsExternalIdCriteria_WhenExternalIdsHasCondition()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse(
            "externalIds has (source = \"imdb\" and value = \"tt0903747\")");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var meta = item.Metadata.And.First();
        var externalIds = meta.ExternalIds;

        Assert.That(externalIds,
            Has.Property(nameof(externalIds.Some))
                .With.Exactly(1).Items);
        var externalId = externalIds.Some.First();

        var sourceSet = externalId.Source;
        var valueSet = externalId.Value;
        var sourceCriterion = sourceSet.And.First();
        var valueCriterion = valueSet.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sourceCriterion,
                Has.Property(nameof(sourceCriterion.Value))
                    .EqualTo("imdb"));
            Assert.That(valueCriterion,
                Has.Property(nameof(valueCriterion.Value))
                    .EqualTo("tt0903747"));
        }
    }

    [Test]
    public void ToContentFilterCriteria_SetsSomeAndNone_WhenGenreHasAndNotHas()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("genre has \"Action\" and genre !has \"Horror\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var meta = item.Metadata.And.First();
        var genres = meta.Genres;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(genres,
                Has.Property(nameof(genres.Some))
                    .With.Exactly(1).Items);
            Assert.That(genres,
                Has.Property(nameof(genres.None))
                    .With.Exactly(1).Items);
        }
    }

    [Test]
    public void ToContentFilterCriteria_PopulatesBothSections_WhenFieldsSpanSections()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse(
            "contentType = \"movie\" and fileSize > 5000000000");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var contentHierarchyItem = criteria.ContentHierarchyItem;
        var mediaFiles = criteria.MediaFiles;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(contentHierarchyItem,
                Has.Property(nameof(contentHierarchyItem.And))
                    .With.Exactly(1).Items);
            Assert.That(mediaFiles,
                Has.Property(nameof(mediaFiles.And))
                    .With.Exactly(1).Items);
        }
    }

    [Test]
    public void ToContentFilterCriteria_SetsGuidCriterion_WhenGuidEquals()
    {
        // Arrange
        var id = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var parseResult = FilterQueryParser.Parse($"parentId = \"{id}\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var criterion = item.ParentId.And.First();

        Assert.That(criterion,
            Has.Property(nameof(criterion.Value)).EqualTo(id));
    }

    [Test]
    public void ToContentFilterCriteria_ReturnsError_WhenUnknownField()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("unknownField = \"value\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).False);

        var error = result.Error!.Value;
        Assert.That(error,
            Has.Property(nameof(error.Message))
                .Contains("Unknown field"));
    }

    [Test]
    public void ToContentFilterCriteria_ReturnsError_WhenNotExpression()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("not name = \"test\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).False);

        var error = result.Error!.Value;
        Assert.That(error,
            Has.Property(nameof(error.Message)).Contains("NOT"));
    }

    [Test]
    public void ToContentFilterCriteria_ReturnsError_WhenOrOnDifferentFields()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("name = \"a\" or title = \"b\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).False);

        var error = result.Error!.Value;
        Assert.That(error,
            Has.Property(nameof(error.Message)).Contains("OR"));
    }

    [Test]
    public void ToContentFilterCriteria_MapsAllOperators_WhenDifferentOperatorsUsed()
    {
        AssertOperatorTransform("=", CriteriaValueOperator.Equals);
        AssertOperatorTransform("!=", CriteriaValueOperator.NotEquals);
        AssertOperatorTransform(">", CriteriaValueOperator.GreaterThan);
        AssertOperatorTransform(">=", CriteriaValueOperator.GreaterThanOrEqual);
        AssertOperatorTransform("<", CriteriaValueOperator.LessThan);
        AssertOperatorTransform("<=", CriteriaValueOperator.LessThanOrEqual);
        AssertOperatorTransformString("contains", CriteriaValueOperator.ContainsString);
        AssertOperatorTransformString("!contains", CriteriaValueOperator.NotContainsString);
        AssertOperatorTransformString("startswith", CriteriaValueOperator.StartsWithString);
        AssertOperatorTransformString("!startswith", CriteriaValueOperator.NotStartsWithString);
        AssertOperatorTransformString("endswith", CriteriaValueOperator.EndsWithString);
        AssertOperatorTransformString("!endswith", CriteriaValueOperator.NotEndsWithString);
    }

    [Test]
    public void ToContentFilterCriteria_UnwrapsGroup_WhenExpressionGrouped()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("(name = \"test\")");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var item = criteria.ContentHierarchyItem.And.First();
        var nameSet = item.Name;
        Assert.That(nameSet,
            Has.Property(nameof(nameSet.And))
                .With.Exactly(1).Items);
    }

    private static void AssertOperatorTransform(
        string queryOp,
        CriteriaValueOperator expectedOp)
    {
        var parseResult = FilterQueryParser.Parse($"fileSize {queryOp} 100");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criterion = result.Value!.MediaFiles.And.First().FileSize.And.First();
        Assert.That(criterion,
            Has.Property(nameof(criterion.Operator))
                .EqualTo(expectedOp));
    }

    private static void AssertOperatorTransformString(
        string queryOp,
        CriteriaValueOperator expectedOp)
    {
        var parseResult = FilterQueryParser.Parse($"name {queryOp} \"test\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        var result = FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criterion = result.Value!.ContentHierarchyItem.And.First().Name.And.First();
        Assert.That(criterion,
            Has.Property(nameof(criterion.Operator))
                .EqualTo(expectedOp));
    }

    [Test]
    public void ToSubjectFilterCriteria_SetsKindCriterion_WhenKindEquals()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("kind = \"person\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var criterion = criteria.Kind.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(criterion,
                Has.Property(nameof(criterion.Operator))
                    .EqualTo(CriteriaValueOperator.Equals));
            Assert.That(criterion,
                Has.Property(nameof(criterion.Value))
                    .EqualTo("person"));
        }
    }

    [Test]
    public void ToSubjectFilterCriteria_SetsDateLastRefreshed_WhenGreaterThanOrEqual()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("dateLastRefreshed >= \"2024-01-01\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var criterion = criteria.DateLastRefreshed.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(criterion,
                Has.Property(nameof(criterion.Operator))
                    .EqualTo(CriteriaValueOperator.GreaterThanOrEqual));
            Assert.That(criterion,
                Has.Property(nameof(criterion.Value))
                    .EqualTo(new DateTime(2024, 1, 1)));
        }
    }

    [Test]
    public void ToSubjectFilterCriteria_SetsTitleCriterion_WhenTitleContains()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("title contains \"Smith\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var meta = criteria.Metadata.And.First();
        var criterion = meta.Title.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(criterion,
                Has.Property(nameof(criterion.Operator))
                    .EqualTo(CriteriaValueOperator.ContainsString));
            Assert.That(criterion,
                Has.Property(nameof(criterion.Value))
                    .EqualTo("Smith"));
        }
    }

    [Test]
    public void ToSubjectFilterCriteria_SetsBirthDateCriterion_WhenGreaterThanOrEqual()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("birthDate >= \"1990-01-01\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var meta = criteria.Metadata.And.First();
        var criterion = meta.BirthDate.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(criterion,
                Has.Property(nameof(criterion.Operator))
                    .EqualTo(CriteriaValueOperator.GreaterThanOrEqual));
            Assert.That(criterion,
                Has.Property(nameof(criterion.Value))
                    .EqualTo(new DateTime(1990, 1, 1)));
        }
    }

    [Test]
    public void ToSubjectFilterCriteria_SetsBothCriteria_WhenMultipleScalarsWithAnd()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("kind = \"person\" and title contains \"John\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var kind = criteria.Kind;
        var metadata = criteria.Metadata;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(kind,
                Has.Property(nameof(kind.And))
                    .With.Exactly(1).Items);
            Assert.That(metadata,
                Has.Property(nameof(metadata.And))
                    .With.Exactly(1).Items);
        }

        var meta = metadata.And.First();
        var titleSet = meta.Title;
        Assert.That(titleSet,
            Has.Property(nameof(titleSet.And))
                .With.Exactly(1).Items);
    }

    [Test]
    public void ToSubjectFilterCriteria_SetsAwardCriterion_WhenAwardHas()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("award has \"Oscar\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var meta = criteria.Metadata.And.First();
        var awards = meta.Awards;

        Assert.That(awards,
            Has.Property(nameof(awards.Some))
                .With.Exactly(1).Items);
        var award = awards.Some.First();
        var nameSet = award.Name;
        Assert.That(nameSet,
            Has.Property(nameof(nameSet.And))
                .With.Exactly(1).Items);
        var criterion = nameSet.And.First();
        Assert.That(criterion,
            Has.Property(nameof(criterion.Value))
                .EqualTo("Oscar"));
    }

    [Test]
    public void ToSubjectFilterCriteria_SetsAwardCriteria_WhenComplexAwardCondition()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse(
            "awards has (body = \"Academy Awards\" and name = \"Best Actor\" and year >= 2000)");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var meta = criteria.Metadata.And.First();
        var awards = meta.Awards;

        Assert.That(awards,
            Has.Property(nameof(awards.Some))
                .With.Exactly(1).Items);
        var award = awards.Some.First();

        var bodySet = award.Body;
        var nameSet = award.Name;
        var yearSet = award.Year;
        var bodyCriterion = bodySet.And.First();
        var nameCriterion = nameSet.And.First();
        var yearCriterion = yearSet.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bodyCriterion,
                Has.Property(nameof(bodyCriterion.Value))
                    .EqualTo("Academy Awards"));
            Assert.That(nameCriterion,
                Has.Property(nameof(nameCriterion.Value))
                    .EqualTo("Best Actor"));
            Assert.That(yearCriterion,
                Has.Property(nameof(yearCriterion.Operator))
                    .EqualTo(CriteriaValueOperator.GreaterThanOrEqual));
            Assert.That(yearCriterion,
                Has.Property(nameof(yearCriterion.Value))
                    .EqualTo(2000));
        }
    }

    [Test]
    public void ToSubjectFilterCriteria_SetsExternalIdCriteria_WhenExternalIdsHasCondition()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse(
            "externalIds has (source = \"imdb\" and value = \"nm0000138\")");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var meta = criteria.Metadata.And.First();
        var externalIds = meta.ExternalIds;

        Assert.That(externalIds,
            Has.Property(nameof(externalIds.Some))
                .With.Exactly(1).Items);
        var externalId = externalIds.Some.First();

        var sourceSet = externalId.Source;
        var valueSet = externalId.Value;
        var sourceCriterion = sourceSet.And.First();
        var valueCriterion = valueSet.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sourceCriterion,
                Has.Property(nameof(sourceCriterion.Value))
                    .EqualTo("imdb"));
            Assert.That(valueCriterion,
                Has.Property(nameof(valueCriterion.Value))
                    .EqualTo("nm0000138"));
        }
    }

    [Test]
    public void ToSubjectFilterCriteria_SetsImageCriteria_WhenImagesHasCondition()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("images has (type = \"poster\")");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var meta = criteria.Metadata.And.First();
        var images = meta.Images;

        Assert.That(images,
            Has.Property(nameof(images.Some))
                .With.Exactly(1).Items);
        var image = images.Some.First();
        var typeSet = image.Type;
        Assert.That(typeSet,
            Has.Property(nameof(typeSet.And))
                .With.Exactly(1).Items);
        var criterion = typeSet.And.First();
        Assert.That(criterion,
            Has.Property(nameof(criterion.Value))
                .EqualTo("poster"));
    }

    [Test]
    public void ToSubjectFilterCriteria_SetsAwardNone_WhenAwardNotHas()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("award !has \"Razzie\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var meta = criteria.Metadata.And.First();
        var awards = meta.Awards;

        Assert.That(awards,
            Has.Property(nameof(awards.None))
                .With.Exactly(1).Items);
        var award = awards.None.First();
        var nameSet = award.Name;
        Assert.That(nameSet,
            Has.Property(nameof(nameSet.And))
                .With.Exactly(1).Items);
        var criterion = nameSet.And.First();
        Assert.That(criterion,
            Has.Property(nameof(criterion.Value))
                .EqualTo("Razzie"));
    }

    [Test]
    public void ToSubjectFilterCriteria_AddsMultipleAwards_WhenOrUsed()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("award has \"Oscar\" or award has \"Emmy\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var meta = criteria.Metadata.And.First();
        var awards = meta.Awards;

        Assert.That(awards,
            Has.Property(nameof(awards.Some))
                .With.Exactly(2).Items);
    }

    [Test]
    public void ToSubjectFilterCriteria_SetsEstablishedCriterion_WhenGreaterThanOrEqual()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("established >= \"1900-01-01\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var meta = criteria.Metadata.And.First();
        var criterion = meta.Established.And.First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(criterion,
                Has.Property(nameof(criterion.Operator))
                    .EqualTo(CriteriaValueOperator.GreaterThanOrEqual));
            Assert.That(criterion,
                Has.Property(nameof(criterion.Value))
                    .EqualTo(new DateTime(1900, 1, 1)));
        }
    }

    [Test]
    public void ToSubjectFilterCriteria_ReturnsError_WhenUnknownField()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("unknownField = \"value\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).False);

        var error = result.Error!.Value;
        Assert.That(error,
            Has.Property(nameof(error.Message))
                .Contains("Unknown field"));
    }

    [Test]
    public void ToSubjectFilterCriteria_ReturnsError_WhenNotExpression()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("not kind = \"person\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).False);

        var error = result.Error!.Value;
        Assert.That(error,
            Has.Property(nameof(error.Message)).Contains("NOT"));
    }

    [Test]
    public void ToSubjectFilterCriteria_ReturnsError_WhenOrOnDifferentFields()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("kind = \"a\" or title = \"b\"");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).False);

        var error = result.Error!.Value;
        Assert.That(error,
            Has.Property(nameof(error.Message)).Contains("OR"));
    }

    [Test]
    public void ToSubjectFilterCriteria_UnwrapsGroup_WhenExpressionGrouped()
    {
        // Arrange
        var parseResult = FilterQueryParser.Parse("(kind = \"person\")");
        Assert.That(parseResult,
            Has.Property(nameof(parseResult.IsSuccess)).True);

        // Act
        var result = FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);

        // Assert
        Assert.That(result, Has.Property(nameof(result.IsSuccess)).True);

        var criteria = result.Value!;
        var kind = criteria.Kind;
        Assert.That(kind,
            Has.Property(nameof(kind.And))
                .With.Exactly(1).Items);
    }
}
