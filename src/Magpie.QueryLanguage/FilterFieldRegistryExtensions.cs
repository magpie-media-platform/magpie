using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.QueryLanguage;

internal static class FilterFieldRegistryExtensions
{
    private static readonly HashSet<CriteriaValueOperator> UnsupportedImageUrlOperators =
    [
        CriteriaValueOperator.GreaterThan,
        CriteriaValueOperator.GreaterThanOrEqual,
        CriteriaValueOperator.LessThan,
        CriteriaValueOperator.LessThanOrEqual,
        CriteriaValueOperator.ContainsString,
        CriteriaValueOperator.NotContainsString,
        CriteriaValueOperator.StartsWithString,
        CriteriaValueOperator.NotStartsWithString,
        CriteriaValueOperator.EndsWithString,
        CriteriaValueOperator.NotEndsWithString,
    ];

    private static readonly CollectionFieldDefinition GenreFieldDefinition = CreateGenreFieldDefinition(nameof(Genre));

    private static readonly CollectionFieldDefinition GenresFieldDefinition =
        CreateGenreFieldDefinition(nameof(MetadataCriteria.Genres));

    private static readonly CollectionFieldDefinition TagFieldDefinition = CreateTagFieldDefinition(nameof(Tag));

    private static readonly CollectionFieldDefinition TagsFieldDefinition =
        CreateTagFieldDefinition(nameof(MetadataCriteria.Tags));

    private static readonly CollectionFieldDefinition AwardFieldDefinition = CreateAwardFieldDefinition(nameof(Award));

    private static readonly CollectionFieldDefinition AwardsFieldDefinition =
        CreateAwardFieldDefinition(nameof(MetadataCriteria.Awards));

    private static readonly CollectionFieldDefinition ExternalIdFieldDefinition =
        CreateExternalIdFieldDefinition(nameof(ExternalId));

    private static readonly CollectionFieldDefinition ExternalIdsFieldDefinition =
        CreateExternalIdFieldDefinition(nameof(MetadataCriteria.ExternalIds));

    private static readonly CollectionFieldDefinition ContentRatingFieldDefinition =
        CreateContentRatingFieldDefinition(nameof(ContentRating));

    private static readonly CollectionFieldDefinition ContentRatingsFieldDefinition =
        CreateContentRatingFieldDefinition(nameof(MetadataCriteria.ContentRatings));

    private static readonly CollectionFieldDefinition ImageFieldDefinition = CreateImageFieldDefinition(nameof(Image));

    private static readonly CollectionFieldDefinition ImagesFieldDefinition =
        CreateImageFieldDefinition(nameof(MetadataCriteria.Images));

    private static readonly CollectionFieldDefinition RelationshipFieldDefinition =
        CreateRelationshipFieldDefinition(nameof(Relationship));

    private static readonly CollectionFieldDefinition RelationshipsFieldDefinition =
        CreateRelationshipFieldDefinition(nameof(MetadataCriteria.Relationships));

    private static readonly MetadataCollectionMutation<GenreCriteria> GenreCollectionMutation = new(
        metadata => metadata.Genres,
        (metadata, criteria) => metadata with
        {
            Genres = criteria
        });

    private static readonly MetadataCollectionMutation<TagCriteria> TagCollectionMutation = new(
        metadata => metadata.Tags,
        (metadata, criteria) => metadata with
        {
            Tags = criteria
        });

    private static readonly MetadataCollectionMutation<AwardCriteria> AwardCollectionMutation = new(
        metadata => metadata.Awards,
        (metadata, criteria) => metadata with
        {
            Awards = criteria
        });

    private static readonly MetadataCollectionMutation<ExternalIdCriteria> ExternalIdCollectionMutation = new(
        metadata => metadata.ExternalIds,
        (metadata, criteria) => metadata with
        {
            ExternalIds = criteria
        });

    private static readonly MetadataCollectionMutation<ContentRatingCriteria> ContentRatingCollectionMutation = new(
        metadata => metadata.ContentRatings,
        (metadata, criteria) => metadata with
        {
            ContentRatings = criteria
        });

    private static readonly MetadataCollectionMutation<ImageCriteria> ImageCollectionMutation = new(
        metadata => metadata.Images,
        (metadata, criteria) => metadata with
        {
            Images = criteria
        });

    private static readonly MetadataCollectionMutation<RelationshipCriteria> RelationshipCollectionMutation = new(
        metadata => metadata.Relationships,
        (metadata, criteria) => metadata with
        {
            Relationships = criteria
        });

    private static readonly Dictionary<string, ScalarFieldInfo<ContentTransformContext>> ContentFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(ContentHierarchyItemCriteria.Name)] = new StringField<ContentTransformContext>(
                (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
                {
                    Name = AddCriterion(ctx.ContentItem.Name, op, v, vs)
                })),
            [nameof(ContentHierarchyItemCriteria.Slug)] = new StringField<ContentTransformContext>(
                (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
                {
                    Slug = AddCriterion(ctx.ContentItem.Slug, op, v, vs)
                })),
            [nameof(ContentHierarchyItemCriteria.Path)] = new StringField<ContentTransformContext>(
                (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
                {
                    Path = AddCriterion(ctx.ContentItem.Path, op, v, vs)
                })),
            [nameof(ContentHierarchyItemCriteria.ContentType)] = new StringField<ContentTransformContext>(
                (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
                {
                    ContentType = AddCriterion(ctx.ContentItem.ContentType, op, v, vs)
                })),
            [nameof(ContentHierarchyItemCriteria.Level)] = new StringField<ContentTransformContext>(
                (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
                {
                    Level = AddCriterion(ctx.ContentItem.Level, op, v, vs)
                })),
            [nameof(ContentHierarchyItemCriteria.Monitoring)] =
                new StructField<ContentTransformContext, bool>(
                    (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
                    {
                        Monitoring = AddCriterion(ctx.ContentItem.Monitoring, op, v ?? default, vs)
                    })),
            [nameof(ContentHierarchyItemCriteria.PendingMetadataRefresh)] =
                new BooleanField<ContentTransformContext>(
                    (ctx, value) => ctx.SetContentItem(ctx.ContentItem with
                    {
                        PendingMetadataRefresh = value
                    })),
            [nameof(ContentHierarchyItemCriteria.HasSearchableTitle)] =
                new BooleanField<ContentTransformContext>(
                    (ctx, value) => ctx.SetContentItem(ctx.ContentItem with
                    {
                        HasSearchableTitle = value
                    })),
            [nameof(ContentHierarchyItemCriteria.Fulfillment)] =
                new StructField<ContentTransformContext, FulfillmentStatus>(
                    (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
                    {
                        Fulfillment = AddCriterion(ctx.ContentItem.Fulfillment, op, v ?? default, vs)
                    })),
            [nameof(ContentHierarchyItemCriteria.ActiveAcquisitionStatus)] =
                new StructField<ContentTransformContext, DownloadStatus>(
                    (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
                    {
                        ActiveAcquisitionStatus = AddCriterion(
                            ctx.ContentItem.ActiveAcquisitionStatus,
                            op,
                            v ?? default,
                            vs)
                    })),
            [nameof(ContentHierarchyItemCriteria.QualityStatus)] =
                new StructField<ContentTransformContext, QualityStatus>(
                    (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
                    {
                        QualityStatus = AddCriterion(ctx.ContentItem.QualityStatus, op, v ?? default, vs)
                    })),
            [nameof(ContentHierarchyItemCriteria.RootFolderId)] = new StructField<ContentTransformContext, Guid>(
                (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
                {
                    RootFolderId = AddCriterion(ctx.ContentItem.RootFolderId, op, v ?? default, vs)
                })),
            [nameof(ContentHierarchyItemCriteria.ParentId)] = new StructField<ContentTransformContext, Guid>(
                (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
                {
                    ParentId = AddCriterion(ctx.ContentItem.ParentId, op, v ?? default, vs)
                })),
            [nameof(MetadataCriteria.Title)] = new StringField<ContentTransformContext>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    Title = AddCriterion(ctx.Metadata.Title, op, v, vs)
                })),
            [nameof(MetadataCriteria.OriginalTitle)] = new StringField<ContentTransformContext>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    OriginalTitle = AddCriterion(ctx.Metadata.OriginalTitle, op, v, vs)
                })),
            [nameof(MetadataCriteria.Overview)] = new StringField<ContentTransformContext>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    Overview = AddCriterion(ctx.Metadata.Overview, op, v, vs)
                })),
            [nameof(MetadataCriteria.Language)] = new StringField<ContentTransformContext>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    Language = AddCriterion(ctx.Metadata.Language, op, v, vs)
                })),
            [nameof(MetadataCriteria.PublishDate)] = new TemporalField<ContentTransformContext>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    PublishDate = AddCriterion(ctx.Metadata.PublishDate, op, v, vs)
                })),
            [nameof(MetadataCriteria.Duration)] = new StructField<ContentTransformContext, TimeSpan>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    Duration = AddCriterion(ctx.Metadata.Duration, op, v ?? default, vs)
                })),
            [nameof(MetadataCriteria.ProductionStatus)] = new StringField<ContentTransformContext>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    ProductionStatus = AddCriterion(ctx.Metadata.ProductionStatus, op, v, vs)
                })),
        };

    private static readonly Dictionary<string, CollectionFieldInfo<ContentTransformContext>> ContentCollectionFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Genre)] = new(GenreFieldDefinition, GenreCollectionMutation.Add),
            [nameof(MetadataCriteria.Genres)] = new(GenresFieldDefinition, GenreCollectionMutation.Add),
            [nameof(Tag)] = new(TagFieldDefinition, TagCollectionMutation.Add),
            [nameof(MetadataCriteria.Tags)] = new(TagsFieldDefinition, TagCollectionMutation.Add),
            [nameof(Award)] = new(AwardFieldDefinition, AwardCollectionMutation.Add),
            [nameof(MetadataCriteria.Awards)] = new(AwardsFieldDefinition, AwardCollectionMutation.Add),
            [nameof(ExternalId)] = new(ExternalIdFieldDefinition, ExternalIdCollectionMutation.Add),
            [nameof(MetadataCriteria.ExternalIds)] = new(ExternalIdsFieldDefinition, ExternalIdCollectionMutation.Add),
            [nameof(ContentRating)] = new(ContentRatingFieldDefinition, ContentRatingCollectionMutation.Add),
            [nameof(MetadataCriteria.ContentRatings)] =
                new(ContentRatingsFieldDefinition, ContentRatingCollectionMutation.Add),
            [nameof(Image)] = new(ImageFieldDefinition, ImageCollectionMutation.Add),
            [nameof(MetadataCriteria.Images)] = new(ImagesFieldDefinition, ImageCollectionMutation.Add),
            [nameof(Relationship)] = new(RelationshipFieldDefinition, RelationshipCollectionMutation.Add),
            [nameof(MetadataCriteria.Relationships)] =
                new(RelationshipsFieldDefinition, RelationshipCollectionMutation.Add),
        };

    private static readonly Dictionary<string, ScalarFieldInfo<SubjectTransformContext>> SubjectFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(SubjectFilterCriteria.Kind)] = new StringField<SubjectTransformContext>(
                (ctx, op, v, vs) => ctx.SetSubject(ctx.Subject with
                {
                    Kind = AddCriterion(ctx.Subject.Kind, op, v, vs)
                })),
            [nameof(SubjectFilterCriteria.DateLastRefreshed)] = new StructField<SubjectTransformContext, DateTime>(
                (ctx, op, v, vs) => ctx.SetSubject(ctx.Subject with
                {
                    DateLastRefreshed = AddCriterion(ctx.Subject.DateLastRefreshed, op, v ?? default, vs)
                })),
            [nameof(MetadataCriteria.Title)] = new StringField<SubjectTransformContext>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    Title = AddCriterion(ctx.Metadata.Title, op, v, vs)
                })),
            [nameof(MetadataCriteria.OriginalTitle)] = new StringField<SubjectTransformContext>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    OriginalTitle = AddCriterion(ctx.Metadata.OriginalTitle, op, v, vs)
                })),
            [nameof(MetadataCriteria.Overview)] = new StringField<SubjectTransformContext>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    Overview = AddCriterion(ctx.Metadata.Overview, op, v, vs)
                })),
            [nameof(MetadataCriteria.Language)] = new StringField<SubjectTransformContext>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    Language = AddCriterion(ctx.Metadata.Language, op, v, vs)
                })),
            [nameof(MetadataCriteria.BirthDate)] = new TemporalField<SubjectTransformContext>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    BirthDate = AddCriterion(ctx.Metadata.BirthDate, op, v, vs)
                })),
            [nameof(MetadataCriteria.DeathDate)] = new TemporalField<SubjectTransformContext>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    DeathDate = AddCriterion(ctx.Metadata.DeathDate, op, v, vs)
                })),
            [nameof(MetadataCriteria.Established)] = new TemporalField<SubjectTransformContext>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    Established = AddCriterion(ctx.Metadata.Established, op, v, vs)
                })),
            [nameof(MetadataCriteria.Dissolved)] = new TemporalField<SubjectTransformContext>(
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    Dissolved = AddCriterion(ctx.Metadata.Dissolved, op, v, vs)
                })),
        };

    private static readonly Dictionary<string, CollectionFieldInfo<SubjectTransformContext>> SubjectCollectionFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Tag)] = new(TagFieldDefinition, TagCollectionMutation.Add),
            [nameof(MetadataCriteria.Tags)] = new(TagsFieldDefinition, TagCollectionMutation.Add),
            [nameof(Award)] = new(AwardFieldDefinition, AwardCollectionMutation.Add),
            [nameof(MetadataCriteria.Awards)] = new(AwardsFieldDefinition, AwardCollectionMutation.Add),
            [nameof(ExternalId)] = new(ExternalIdFieldDefinition, ExternalIdCollectionMutation.Add),
            [nameof(MetadataCriteria.ExternalIds)] = new(ExternalIdsFieldDefinition, ExternalIdCollectionMutation.Add),
            [nameof(Image)] = new(ImageFieldDefinition, ImageCollectionMutation.Add),
            [nameof(MetadataCriteria.Images)] = new(ImagesFieldDefinition, ImageCollectionMutation.Add),
        };

    public static ScalarFieldInfo<ContentTransformContext>? GetContentField(this string name)
    {
        return ContentFields.GetValueOrDefault(name);
    }

    public static CollectionFieldInfo<ContentTransformContext>? GetContentCollectionField(this string name)
    {
        return ContentCollectionFields.GetValueOrDefault(name);
    }

    public static ScalarFieldInfo<SubjectTransformContext>? GetSubjectField(this string name)
    {
        return SubjectFields.GetValueOrDefault(name);
    }

    public static CollectionFieldInfo<SubjectTransformContext>? GetSubjectCollectionField(this string name)
    {
        return SubjectCollectionFields.GetValueOrDefault(name);
    }

    private static CriteriaSet<PropertyCriterion<T>> AddCriterion<T>(
        CriteriaSet<PropertyCriterion<T>> set,
        CriteriaValueOperator op,
        T? value,
        IEnumerable<T>? values)
    {
        var criterion = new PropertyCriterion<T>
        {
            Operator = op,
            Value = value,
            Values = values?.ToList() ?? []
        };

        return new()
        {
            And = set.And.Append(criterion)
        };
    }

    private static CriteriaSet<PropertyCriterion<TemporalValue>> AddCriterion(
        CriteriaSet<PropertyCriterion<TemporalValue>> set,
        CriteriaValueOperator op,
        TemporalValue? value,
        IEnumerable<TemporalValue>? values)
    {
        var criterion = new PropertyCriterion<TemporalValue>
        {
            Operator = op,
            Value = value,
            Values = values?.ToList() ?? []
        };

        return new()
        {
            And = set.And.Append(criterion)
        };
    }

    private static CollectionCriteria<T> AppendSomeGroup<T>(CollectionCriteria<T> existing, IReadOnlyList<object> group)
    {
        return new()
        {
            Some = [.. existing.Some, [.. group.Cast<T>()]],
            None = existing.None
        };
    }

    private static CollectionCriteria<T> AppendNone<T>(CollectionCriteria<T> existing, IReadOnlyList<object> group)
    {
        return new()
        {
            Some = existing.Some,
            None = [.. existing.None, .. group.Cast<T>()]
        };
    }

    private static CriteriaSet<PropertyCriterion<string>> StringCriterion(CriteriaValueOperator op, object? value)
    {
        return new()
        {
            And = [new() { Operator = op, Value = (string?)value }]
        };
    }

    private static CriteriaSet<PropertyCriterion<Guid>> GuidCriterion(CriteriaValueOperator op, object? value)
    {
        return new()
        {
            And = [new() { Operator = op, Value = value is Guid guid ? guid : Guid.Empty }]
        };
    }

    private static CriteriaSet<PropertyCriterion<int>> IntCriterion(CriteriaValueOperator op, object? value)
    {
        return new()
        {
            And = [new() { Operator = op, Value = value is int i ? i : 0 }]
        };
    }

    private static CriteriaSet<PropertyCriterion<T>> MergeCriteriaSet<T>(
        CriteriaSet<PropertyCriterion<T>> a,
        CriteriaSet<PropertyCriterion<T>> b)
    {
        return new()
        {
            And = a.And.Concat(b.And)
        };
    }

    private static CollectionFieldDefinition CreateGenreFieldDefinition(string queryName)
    {
        return new CollectionFieldDefinition(
            queryName,
            typeof(string),
            new Dictionary<string, ItemFieldInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(GenreCriteria.Name)] = new(nameof(GenreCriteria.Name), typeof(string))
            },
            value => new GenreCriteria
            {
                Name = StringCriterion(CriteriaValueOperator.Equals, value)
            },
            (_, op, value) => new GenreCriteria
            {
                Name = StringCriterion(op, value)
            },
            (a, b) => MergeGenreCriteria((GenreCriteria)a, (GenreCriteria)b));
    }

    private static GenreCriteria MergeGenreCriteria(GenreCriteria a, GenreCriteria b)
    {
        return new GenreCriteria
        {
            Name = MergeCriteriaSet(a.Name, b.Name)
        };
    }

    private static CollectionFieldDefinition CreateTagFieldDefinition(string queryName)
    {
        return new CollectionFieldDefinition(
            queryName,
            typeof(string),
            new Dictionary<string, ItemFieldInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(TagCriteria.Name)] = new(nameof(TagCriteria.Name), typeof(string))
            },
            value => new TagCriteria
            {
                Name = StringCriterion(CriteriaValueOperator.Equals, value)
            },
            (_, op, value) => new TagCriteria
            {
                Name = StringCriterion(op, value)
            },
            (a, b) => MergeTagCriteria((TagCriteria)a, (TagCriteria)b));
    }

    private static TagCriteria MergeTagCriteria(TagCriteria a, TagCriteria b)
    {
        return new TagCriteria
        {
            Name = MergeCriteriaSet(a.Name, b.Name)
        };
    }

    private static CollectionFieldDefinition CreateAwardFieldDefinition(string queryName)
    {
        return new CollectionFieldDefinition(
            queryName,
            typeof(string),
            new Dictionary<string, ItemFieldInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(AwardCriteria.Body)] = new(nameof(AwardCriteria.Body), typeof(string)),
                [nameof(AwardCriteria.Name)] = new(nameof(AwardCriteria.Name), typeof(string)),
                [nameof(AwardCriteria.Category)] = new(nameof(AwardCriteria.Category), typeof(string)),
                [nameof(AwardCriteria.Status)] = new(nameof(AwardCriteria.Status), typeof(string)),
                [nameof(AwardCriteria.Year)] = new(nameof(AwardCriteria.Year), typeof(int)),
                [nameof(AwardCriteria.Language)] = new(nameof(AwardCriteria.Language), typeof(string)),
                [nameof(AwardCriteria.Country)] = new(nameof(AwardCriteria.Country), typeof(string))
            },
            value => new AwardCriteria
            {
                Name = StringCriterion(CriteriaValueOperator.Equals, value)
            },
            CreateAwardFieldCriteria,
            (a, b) => MergeAwardCriteria((AwardCriteria)a, (AwardCriteria)b));
    }

    private static AwardCriteria CreateAwardFieldCriteria(string propName, CriteriaValueOperator op, object? value)
    {
        return propName switch
        {
            nameof(AwardCriteria.Body) => new() { Body = StringCriterion(op, value) },
            nameof(AwardCriteria.Name) => new() { Name = StringCriterion(op, value) },
            nameof(AwardCriteria.Category) => new() { Category = StringCriterion(op, value) },
            nameof(AwardCriteria.Status) => new() { Status = StringCriterion(op, value) },
            nameof(AwardCriteria.Year) => new() { Year = IntCriterion(op, value) },
            nameof(AwardCriteria.Language) => new() { Language = StringCriterion(op, value) },
            nameof(AwardCriteria.Country) => new() { Country = StringCriterion(op, value) },
            _ => throw new ArgumentOutOfRangeException(nameof(propName), propName, null)
        };
    }

    private static AwardCriteria MergeAwardCriteria(AwardCriteria a, AwardCriteria b)
    {
        return new AwardCriteria
        {
            Body = MergeCriteriaSet(a.Body, b.Body),
            Name = MergeCriteriaSet(a.Name, b.Name),
            Category = MergeCriteriaSet(a.Category, b.Category),
            Status = MergeCriteriaSet(a.Status, b.Status),
            Year = MergeCriteriaSet(a.Year, b.Year),
            Language = MergeCriteriaSet(a.Language, b.Language),
            Country = MergeCriteriaSet(a.Country, b.Country)
        };
    }

    private static CollectionFieldDefinition CreateExternalIdFieldDefinition(string queryName)
    {
        return new CollectionFieldDefinition(
            queryName,
            typeof(string),
            new Dictionary<string, ItemFieldInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(ExternalIdCriteria.Source)] = new(nameof(ExternalIdCriteria.Source), typeof(string)),
                [nameof(ExternalIdCriteria.Value)] = new(nameof(ExternalIdCriteria.Value), typeof(string))
            },
            value => new ExternalIdCriteria
            {
                Value = StringCriterion(CriteriaValueOperator.Equals, value)
            },
            CreateExternalIdFieldCriteria,
            (a, b) => MergeExternalIdCriteria((ExternalIdCriteria)a, (ExternalIdCriteria)b));
    }

    private static ExternalIdCriteria CreateExternalIdFieldCriteria(
        string propName,
        CriteriaValueOperator op,
        object? value)
    {
        return propName switch
        {
            nameof(ExternalIdCriteria.Source) => new() { Source = StringCriterion(op, value) },
            nameof(ExternalIdCriteria.Value) => new() { Value = StringCriterion(op, value) },
            _ => throw new ArgumentOutOfRangeException(nameof(propName), propName, null)
        };
    }

    private static ExternalIdCriteria MergeExternalIdCriteria(ExternalIdCriteria a, ExternalIdCriteria b)
    {
        return new ExternalIdCriteria
        {
            Source = MergeCriteriaSet(a.Source, b.Source),
            Value = MergeCriteriaSet(a.Value, b.Value)
        };
    }

    private static CollectionFieldDefinition CreateContentRatingFieldDefinition(string queryName)
    {
        return new CollectionFieldDefinition(
            queryName,
            typeof(string),
            new Dictionary<string, ItemFieldInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(ContentRatingCriteria.Body)] = new(nameof(ContentRatingCriteria.Body), typeof(string)),
                [nameof(ContentRatingCriteria.Rating)] = new(nameof(ContentRatingCriteria.Rating), typeof(string))
            },
            value => new ContentRatingCriteria
            {
                Rating = StringCriterion(CriteriaValueOperator.Equals, value)
            },
            CreateContentRatingFieldCriteria,
            (a, b) => MergeContentRatingCriteria((ContentRatingCriteria)a, (ContentRatingCriteria)b));
    }

    private static ContentRatingCriteria CreateContentRatingFieldCriteria(
        string propName,
        CriteriaValueOperator op,
        object? value)
    {
        return propName switch
        {
            nameof(ContentRatingCriteria.Body) => new() { Body = StringCriterion(op, value) },
            nameof(ContentRatingCriteria.Rating) => new() { Rating = StringCriterion(op, value) },
            _ => throw new ArgumentOutOfRangeException(nameof(propName), propName, null)
        };
    }

    private static ContentRatingCriteria MergeContentRatingCriteria(ContentRatingCriteria a, ContentRatingCriteria b)
    {
        return new ContentRatingCriteria
        {
            Body = MergeCriteriaSet(a.Body, b.Body),
            Rating = MergeCriteriaSet(a.Rating, b.Rating)
        };
    }

    private static CollectionFieldDefinition CreateImageFieldDefinition(string queryName)
    {
        return new CollectionFieldDefinition(
            queryName,
            typeof(string),
            new Dictionary<string, ItemFieldInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(ImageCriteria.Type)] = new(nameof(ImageCriteria.Type), typeof(string)),
                [nameof(ImageCriteria.MimeType)] = new(nameof(ImageCriteria.MimeType), typeof(string)),
                [nameof(ImageCriteria.Url)] =
                    new(
                        nameof(ImageCriteria.Url),
                        typeof(string),
                        UnsupportedImageUrlOperators,
                        requireAbsoluteUri: true),
                [nameof(ImageCriteria.Source)] = new(nameof(ImageCriteria.Source), typeof(string)),
                [nameof(ImageCriteria.Language)] = new(nameof(ImageCriteria.Language), typeof(string)),
                [nameof(ImageCriteria.Width)] = new(nameof(ImageCriteria.Width), typeof(int)),
                [nameof(ImageCriteria.Height)] = new(nameof(ImageCriteria.Height), typeof(int))
            },
            value => new ImageCriteria
            {
                Type = StringCriterion(CriteriaValueOperator.Equals, value)
            },
            CreateImageFieldCriteria,
            (a, b) => MergeImageCriteria((ImageCriteria)a, (ImageCriteria)b));
    }

    private static ImageCriteria CreateImageFieldCriteria(string propName, CriteriaValueOperator op, object? value)
    {
        return propName switch
        {
            nameof(ImageCriteria.Type) => new() { Type = StringCriterion(op, value) },
            nameof(ImageCriteria.MimeType) => new() { MimeType = StringCriterion(op, value) },
            nameof(ImageCriteria.Url) => new() { Url = StringCriterion(op, value) },
            nameof(ImageCriteria.Source) => new() { Source = StringCriterion(op, value) },
            nameof(ImageCriteria.Language) => new() { Language = StringCriterion(op, value) },
            nameof(ImageCriteria.Width) => new() { Width = IntCriterion(op, value) },
            nameof(ImageCriteria.Height) => new() { Height = IntCriterion(op, value) },
            _ => throw new ArgumentOutOfRangeException(nameof(propName), propName, null)
        };
    }

    private static ImageCriteria MergeImageCriteria(ImageCriteria a, ImageCriteria b)
    {
        return new ImageCriteria
        {
            Type = MergeCriteriaSet(a.Type, b.Type),
            MimeType = MergeCriteriaSet(a.MimeType, b.MimeType),
            Url = MergeCriteriaSet(a.Url, b.Url),
            Source = MergeCriteriaSet(a.Source, b.Source),
            Language = MergeCriteriaSet(a.Language, b.Language),
            Width = MergeCriteriaSet(a.Width, b.Width),
            Height = MergeCriteriaSet(a.Height, b.Height)
        };
    }

    private static CollectionFieldDefinition CreateRelationshipFieldDefinition(string queryName)
    {
        return new CollectionFieldDefinition(
            queryName,
            typeof(string),
            new Dictionary<string, ItemFieldInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(RelationshipCriteria.Kind)] = new(nameof(RelationshipCriteria.Kind), typeof(string)),
                [nameof(RelationshipCriteria.Type)] = new(nameof(RelationshipCriteria.Type), typeof(string)),
                [nameof(RelationshipCriteria.Qualifier)] = new(nameof(RelationshipCriteria.Qualifier), typeof(string)),
                [nameof(RelationshipCriteria.Subject)] = new(nameof(RelationshipCriteria.Subject), typeof(string)),
                ["Name"] = new(nameof(RelationshipCriteria.Subject), typeof(string)),
                [nameof(RelationshipCriteria.RelatedMetadataContainerId)] =
                    new(nameof(RelationshipCriteria.RelatedMetadataContainerId), typeof(Guid)),
                ["ExternalId"] = new(nameof(ExternalIdCriteria.Value), typeof(string)),
                ["ExternalIdSource"] = new(nameof(ExternalIdCriteria.Source), typeof(string))
            },
            value => new RelationshipCriteria
            {
                Kind = StringCriterion(CriteriaValueOperator.Equals, value)
            },
            CreateRelationshipFieldCriteria,
            (a, b) => MergeRelationshipCriteria((RelationshipCriteria)a, (RelationshipCriteria)b));
    }

    private static RelationshipCriteria CreateRelationshipFieldCriteria(
        string propName,
        CriteriaValueOperator op,
        object? value)
    {
        return propName switch
        {
            nameof(RelationshipCriteria.Kind) => new() { Kind = StringCriterion(op, value) },
            nameof(RelationshipCriteria.Type) => new() { Type = StringCriterion(op, value) },
            nameof(RelationshipCriteria.Qualifier) => new() { Qualifier = StringCriterion(op, value) },
            nameof(RelationshipCriteria.Subject) => new() { Subject = StringCriterion(op, value) },
            nameof(RelationshipCriteria.RelatedMetadataContainerId) => new()
            {
                RelatedMetadataContainerId = GuidCriterion(op, value)
            },
            nameof(ExternalIdCriteria.Value) or nameof(ExternalIdCriteria.Source) => new()
            {
                ExternalIds = new()
                {
                    Some = [[CreateExternalIdFieldCriteria(propName, op, value)]]
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(propName), propName, null)
        };
    }

    private static RelationshipCriteria MergeRelationshipCriteria(RelationshipCriteria a, RelationshipCriteria b)
    {
        return new RelationshipCriteria
        {
            Kind = MergeCriteriaSet(a.Kind, b.Kind),
            Type = MergeCriteriaSet(a.Type, b.Type),
            Qualifier = MergeCriteriaSet(a.Qualifier, b.Qualifier),
            Subject = MergeCriteriaSet(a.Subject, b.Subject),
            RelatedMetadataContainerId = MergeCriteriaSet(
                a.RelatedMetadataContainerId,
                b.RelatedMetadataContainerId),
            ExternalIds = MergeRelationshipExternalIds(a.ExternalIds, b.ExternalIds)
        };
    }

    private static CollectionCriteria<ExternalIdCriteria> MergeRelationshipExternalIds(
        CollectionCriteria<ExternalIdCriteria> a,
        CollectionCriteria<ExternalIdCriteria> b)
    {
        if (a.Some is [[var aOnly]] && b.Some is [[var bOnly]] && a.None.Count == 0 && b.None.Count == 0)
        {
            return new()
            {
                Some = [[MergeExternalIdCriteria(aOnly, bOnly)]]
            };
        }

        return new()
        {
            Some = [.. a.Some, .. b.Some],
            None = [.. a.None, .. b.None]
        };
    }

    private sealed class StringField<TContext>(
        Action<TContext, CriteriaValueOperator, string?, IEnumerable<string>?> addCriterion)
        : ScalarFieldInfo<TContext>(typeof(string))
    {
        public override void AddCriterion(
            TContext context,
            CriteriaValueOperator op,
            object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, (string?)value, values?.Cast<string>());
        }
    }

    private sealed class StructField<TContext, TValue>(
        Action<TContext, CriteriaValueOperator, TValue?, IEnumerable<TValue>?> addCriterion)
        : ScalarFieldInfo<TContext>(typeof(TValue))
        where TValue : struct
    {
        public override void AddCriterion(
            TContext context,
            CriteriaValueOperator op,
            object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, value is TValue typed ? typed : null, values?.Cast<TValue>());
        }
    }

    private sealed class TemporalField<TContext>(
        Action<TContext, CriteriaValueOperator, TemporalValue?, IEnumerable<TemporalValue>?> addCriterion)
        : ScalarFieldInfo<TContext>(typeof(TemporalValue))
    {
        public override void AddCriterion(
            TContext context,
            CriteriaValueOperator op,
            object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, value as TemporalValue, values?.Cast<TemporalValue>());
        }
    }

    private sealed class BooleanField<TContext>(Action<TContext, bool?> setValue)
        : ScalarFieldInfo<TContext>(typeof(bool))
    {
        public override void AddCriterion(
            TContext context,
            CriteriaValueOperator op,
            object? value,
            IEnumerable<object>? values)
        {
            var boolean = value as bool?;
            setValue(
                context,
                op switch
                {
                    CriteriaValueOperator.Equals => boolean,
                    CriteriaValueOperator.NotEquals => boolean is { } typed ? !typed : null,
                    _ => null
                });
        }
    }

    private sealed class MetadataCollectionMutation<TCriteria>(
        Func<MetadataCriteria, CollectionCriteria<TCriteria>> getCollection,
        Func<MetadataCriteria, CollectionCriteria<TCriteria>, MetadataCriteria> setCollection)
    {
        public void Add(ContentTransformContext context, IReadOnlyList<object> group, bool isSome)
        {
            context.SetMetadata(Apply(context.Metadata, group, isSome));
        }

        public void Add(SubjectTransformContext context, IReadOnlyList<object> group, bool isSome)
        {
            context.SetMetadata(Apply(context.Metadata, group, isSome));
        }

        private MetadataCriteria Apply(MetadataCriteria metadata, IReadOnlyList<object> group, bool isSome)
        {
            var collection = getCollection(metadata);
            var updatedCollection = isSome ? AppendSomeGroup(collection, group) : AppendNone(collection, group);

            return setCollection(metadata, updatedCollection);
        }
    }
}
