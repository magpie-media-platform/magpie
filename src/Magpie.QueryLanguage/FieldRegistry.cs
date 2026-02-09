using System;
using System.Collections.Generic;
using System.Linq;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.QueryLanguage;

internal static class FieldRegistry
{
    private static readonly Dictionary<string, ScalarFieldInfo> ContentFields = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(ContentHierarchyItemCriteria.Name)] = new StringContentItemField(
            nameof(ContentHierarchyItemCriteria.Name),
            (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
            {
                Name = AddCriterion(ctx.ContentItem.Name, op, v, vs)
            })),
        [nameof(ContentHierarchyItemCriteria.Slug)] = new StringContentItemField(
            nameof(ContentHierarchyItemCriteria.Slug),
            (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
            {
                Slug = AddCriterion(ctx.ContentItem.Slug, op, v, vs)
            })),
        [nameof(ContentHierarchyItemCriteria.Path)] = new StringContentItemField(
            nameof(ContentHierarchyItemCriteria.Path),
            (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
            {
                Path = AddCriterion(ctx.ContentItem.Path, op, v, vs)
            })),
        [nameof(ContentHierarchyItemCriteria.ContentType)] = new StringContentItemField(
            nameof(ContentHierarchyItemCriteria.ContentType),
            (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
            {
                ContentType = AddCriterion(ctx.ContentItem.ContentType, op, v, vs)
            })),
        [nameof(ContentHierarchyItemCriteria.Level)] = new StringContentItemField(
            nameof(ContentHierarchyItemCriteria.Level),
            (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
            {
                Level = AddCriterion(ctx.ContentItem.Level, op, v, vs)
            })),
        [nameof(ContentHierarchyItemCriteria.Monitoring)] = new EnumContentItemField<MonitoringIntent>(
            nameof(ContentHierarchyItemCriteria.Monitoring),
            (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
            {
                Monitoring = AddCriterion(ctx.ContentItem.Monitoring, op, v ?? default, vs)
            })),
        [nameof(ContentHierarchyItemCriteria.RootFolderId)] = new GuidContentItemField(
            nameof(ContentHierarchyItemCriteria.RootFolderId),
            (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
            {
                RootFolderId = AddCriterion(ctx.ContentItem.RootFolderId, op, v ?? default, vs)
            })),
        [nameof(ContentHierarchyItemCriteria.ParentId)] = new GuidContentItemField(
            nameof(ContentHierarchyItemCriteria.ParentId),
            (ctx, op, v, vs) => ctx.SetContentItem(ctx.ContentItem with
            {
                ParentId = AddCriterion(ctx.ContentItem.ParentId, op, v ?? default, vs)
            })),
        [nameof(MetadataCriteria.Title)] = new StringMetadataField(
            nameof(MetadataCriteria.Title),
            (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
            {
                Title = AddCriterion(ctx.Metadata.Title, op, v, vs)
            })),
        [nameof(MetadataCriteria.OriginalTitle)] = new StringMetadataField(
            nameof(MetadataCriteria.OriginalTitle),
            (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
            {
                OriginalTitle = AddCriterion(ctx.Metadata.OriginalTitle, op, v, vs)
            })),
        [nameof(MetadataCriteria.Overview)] = new StringMetadataField(
            nameof(MetadataCriteria.Overview),
            (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
            {
                Overview = AddCriterion(ctx.Metadata.Overview, op, v, vs)
            })),
        [nameof(MetadataCriteria.Language)] = new StringMetadataField(
            nameof(MetadataCriteria.Language),
            (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
            {
                Language = AddCriterion(ctx.Metadata.Language, op, v, vs)
            })),
        [nameof(MetadataCriteria.PublishDate)] = new DateTimeMetadataField(
            nameof(MetadataCriteria.PublishDate),
            (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
            {
                PublishDate = AddCriterion(ctx.Metadata.PublishDate, op, v ?? default, vs)
            })),
        [nameof(MetadataCriteria.Duration)] = new TimeSpanMetadataField(
            nameof(MetadataCriteria.Duration),
            (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
            {
                Duration = AddCriterion(ctx.Metadata.Duration, op, v ?? default, vs)
            })),
        [nameof(MetadataCriteria.ProductionStatus)] = new StringMetadataField(
            nameof(MetadataCriteria.ProductionStatus),
            (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
            {
                ProductionStatus = AddCriterion(ctx.Metadata.ProductionStatus, op, v, vs)
            })),
        [nameof(MediaFileCriteria.FileKind)] = new StringMediaFileField(
            nameof(MediaFileCriteria.FileKind),
            (ctx, op, v, vs) => ctx.SetMediaFile(ctx.MediaFile with
            {
                FileKind = AddCriterion(ctx.MediaFile.FileKind, op, v, vs)
            })),
        [nameof(MediaFileCriteria.MimeType)] = new StringMediaFileField(
            nameof(MediaFileCriteria.MimeType),
            (ctx, op, v, vs) => ctx.SetMediaFile(ctx.MediaFile with
            {
                MimeType = AddCriterion(ctx.MediaFile.MimeType, op, v, vs)
            })),
        [nameof(MediaFileCriteria.FilePath)] = new StringMediaFileField(
            nameof(MediaFileCriteria.FilePath),
            (ctx, op, v, vs) => ctx.SetMediaFile(ctx.MediaFile with
            {
                FilePath = AddCriterion(ctx.MediaFile.FilePath, op, v, vs)
            })),
        [nameof(MediaFileCriteria.FileSize)] = new LongMediaFileField(
            nameof(MediaFileCriteria.FileSize),
            (ctx, op, v, vs) => ctx.SetMediaFile(ctx.MediaFile with
            {
                FileSize = AddCriterion(ctx.MediaFile.FileSize, op, v ?? 0, vs)
            })),
    };

    private static readonly Dictionary<string, CollectionFieldInfo> ContentCollectionFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Genre)] = CreateGenreField(nameof(Genre)),
            [nameof(MetadataCriteria.Genres)] = CreateGenreField(nameof(MetadataCriteria.Genres)),
            [nameof(Tag)] = CreateTagField(nameof(Tag)),
            [nameof(MetadataCriteria.Tags)] = CreateTagField(nameof(MetadataCriteria.Tags)),
            [nameof(Award)] = CreateAwardField(nameof(Award)),
            [nameof(MetadataCriteria.Awards)] = CreateAwardField(nameof(MetadataCriteria.Awards)),
            [nameof(ExternalId)] = CreateExternalIdField(nameof(ExternalId)),
            [nameof(MetadataCriteria.ExternalIds)] = CreateExternalIdField(nameof(MetadataCriteria.ExternalIds)),
            [nameof(ContentRating)] = CreateContentRatingField(nameof(ContentRating)),
            [nameof(MetadataCriteria.ContentRatings)] =
                CreateContentRatingField(nameof(MetadataCriteria.ContentRatings)),
            [nameof(Image)] = CreateImageField(nameof(Image)),
            [nameof(MetadataCriteria.Images)] = CreateImageField(nameof(MetadataCriteria.Images)),
        };

    private static readonly Dictionary<string, SubjectScalarFieldInfo> SubjectFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(SubjectFilterCriteria.Kind)] = new StringSubjectField(
                nameof(SubjectFilterCriteria.Kind),
                (ctx, op, v, vs) => ctx.SetSubject(ctx.Subject with
                {
                    Kind = AddCriterion(ctx.Subject.Kind, op, v, vs)
                })),
            [nameof(SubjectFilterCriteria.DateLastRefreshed)] = new DateTimeSubjectField(
                nameof(SubjectFilterCriteria.DateLastRefreshed),
                (ctx, op, v, vs) => ctx.SetSubject(ctx.Subject with
                {
                    DateLastRefreshed = AddCriterion(ctx.Subject.DateLastRefreshed, op, v ?? default, vs)
                })),
            [nameof(MetadataCriteria.Title)] = new StringSubjectMetadataField(
                nameof(MetadataCriteria.Title),
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    Title = AddCriterion(ctx.Metadata.Title, op, v, vs)
                })),
            [nameof(MetadataCriteria.OriginalTitle)] = new StringSubjectMetadataField(
                nameof(MetadataCriteria.OriginalTitle),
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    OriginalTitle = AddCriterion(ctx.Metadata.OriginalTitle, op, v, vs)
                })),
            [nameof(MetadataCriteria.Overview)] = new StringSubjectMetadataField(
                nameof(MetadataCriteria.Overview),
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    Overview = AddCriterion(ctx.Metadata.Overview, op, v, vs)
                })),
            [nameof(MetadataCriteria.Language)] = new StringSubjectMetadataField(
                nameof(MetadataCriteria.Language),
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    Language = AddCriterion(ctx.Metadata.Language, op, v, vs)
                })),
            [nameof(MetadataCriteria.BirthDate)] = new DateTimeSubjectMetadataField(
                nameof(MetadataCriteria.BirthDate),
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    BirthDate = AddCriterion(ctx.Metadata.BirthDate, op, v ?? default, vs)
                })),
            [nameof(MetadataCriteria.DeathDate)] = new DateTimeSubjectMetadataField(
                nameof(MetadataCriteria.DeathDate),
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    DeathDate = AddCriterion(ctx.Metadata.DeathDate, op, v ?? default, vs)
                })),
            [nameof(MetadataCriteria.Established)] = new DateTimeSubjectMetadataField(
                nameof(MetadataCriteria.Established),
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    Established = AddCriterion(ctx.Metadata.Established, op, v ?? default, vs)
                })),
            [nameof(MetadataCriteria.Dissolved)] = new DateTimeSubjectMetadataField(
                nameof(MetadataCriteria.Dissolved),
                (ctx, op, v, vs) => ctx.SetMetadata(ctx.Metadata with
                {
                    Dissolved = AddCriterion(ctx.Metadata.Dissolved, op, v ?? default, vs)
                })),
        };

    private static readonly Dictionary<string, SubjectCollectionFieldInfo> SubjectCollectionFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Award)] = CreateSubjectAwardField(nameof(Award)),
            [nameof(MetadataCriteria.Awards)] = CreateSubjectAwardField(nameof(MetadataCriteria.Awards)),
            [nameof(ExternalId)] = CreateSubjectExternalIdField(nameof(ExternalId)),
            [nameof(MetadataCriteria.ExternalIds)] =
                CreateSubjectExternalIdField(nameof(MetadataCriteria.ExternalIds)),
            [nameof(Image)] = CreateSubjectImageField(nameof(Image)),
            [nameof(MetadataCriteria.Images)] = CreateSubjectImageField(nameof(MetadataCriteria.Images)),
        };

    public static ScalarFieldInfo? GetContentField(string name)
    {
        return ContentFields.GetValueOrDefault(name);
    }

    public static CollectionFieldInfo? GetContentCollectionField(string name)
    {
        return ContentCollectionFields.GetValueOrDefault(name);
    }

    public static SubjectScalarFieldInfo? GetSubjectField(string name)
    {
        return SubjectFields.GetValueOrDefault(name);
    }

    public static SubjectCollectionFieldInfo? GetSubjectCollectionField(string name)
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

    private static CollectionFieldInfo CreateGenreField(string queryName)
    {
        return new CollectionFieldInfo(
            queryName,
            typeof(string),
            new Dictionary<string, ItemFieldInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(GenreCriteria.Name)] = new(nameof(GenreCriteria.Name), typeof(string))
            },
            value => new GenreCriteria
            {
                Name = new()
                {
                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = (string?)value }]
                }
            },
            (propName, op, value) => new GenreCriteria
            {
                Name = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
            (a, b) => MergeGenreCriteria((GenreCriteria)a, (GenreCriteria)b),
            (ctx, criteria, isSome) =>
            {
                var genre = (GenreCriteria)criteria;
                var meta = ctx.Metadata;
                if (isSome)
                {
                    ctx.SetMetadata(meta with
                    {
                        Genres = new()
                        {
                            Some = meta.Genres.Some.Append(genre).ToList(),
                            None = meta.Genres.None
                        }
                    });
                }
                else
                {
                    ctx.SetMetadata(meta with
                    {
                        Genres = new()
                        {
                            Some = meta.Genres.Some,
                            None = meta.Genres.None.Append(genre).ToList()
                        }
                    });
                }
            });
    }

    private static GenreCriteria MergeGenreCriteria(GenreCriteria a, GenreCriteria b)
    {
        return new GenreCriteria
        {
            Name = new()
            {
                And = a.Name.And.Concat(b.Name.And)
            }
        };
    }

    private static CollectionFieldInfo CreateTagField(string queryName)
    {
        return new CollectionFieldInfo(
            queryName,
            typeof(string),
            new Dictionary<string, ItemFieldInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(TagCriteria.Name)] = new(nameof(TagCriteria.Name), typeof(string))
            },
            value => new TagCriteria
            {
                Name = new()
                {
                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = (string?)value }]
                }
            },
            (propName, op, value) => new TagCriteria
            {
                Name = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
            (a, b) => MergeTagCriteria((TagCriteria)a, (TagCriteria)b),
            (ctx, criteria, isSome) =>
            {
                var tag = (TagCriteria)criteria;
                var meta = ctx.Metadata;
                if (isSome)
                {
                    ctx.SetMetadata(meta with
                    {
                        Tags = new()
                        {
                            Some = meta.Tags.Some.Append(tag).ToList(),
                            None = meta.Tags.None
                        }
                    });
                }
                else
                {
                    ctx.SetMetadata(meta with
                    {
                        Tags = new()
                        {
                            Some = meta.Tags.Some,
                            None = meta.Tags.None.Append(tag).ToList()
                        }
                    });
                }
            });
    }

    private static TagCriteria MergeTagCriteria(TagCriteria a, TagCriteria b)
    {
        return new TagCriteria
        {
            Name = new()
            {
                And = a.Name.And.Concat(b.Name.And)
            }
        };
    }

    private static CollectionFieldInfo CreateAwardField(string queryName)
    {
        return new CollectionFieldInfo(
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
                Name = new()
                {
                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = (string?)value }]
                }
            },
            CreateAwardFieldCriteria,
            (a, b) => MergeAwardCriteria((AwardCriteria)a, (AwardCriteria)b),
            (ctx, criteria, isSome) =>
            {
                var award = (AwardCriteria)criteria;
                var meta = ctx.Metadata;
                if (isSome)
                {
                    ctx.SetMetadata(meta with
                    {
                        Awards = new()
                        {
                            Some = meta.Awards.Some.Append(award).ToList(),
                            None = meta.Awards.None
                        }
                    });
                }
                else
                {
                    ctx.SetMetadata(meta with
                    {
                        Awards = new()
                        {
                            Some = meta.Awards.Some,
                            None = meta.Awards.None.Append(award).ToList()
                        }
                    });
                }
            });
    }

    private static AwardCriteria CreateAwardFieldCriteria(string propName, CriteriaValueOperator op, object? value)
    {
        var criteria = new AwardCriteria();
        return propName switch
        {
            nameof(AwardCriteria.Body) => criteria with
            {
                Body = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
            nameof(AwardCriteria.Name) => criteria with
            {
                Name = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
            nameof(AwardCriteria.Category) => criteria with
            {
                Category = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
            nameof(AwardCriteria.Status) => criteria with
            {
                Status = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
            nameof(AwardCriteria.Year) => criteria with
            {
                Year = new()
                {
                    And = [new PropertyCriterion<int> { Operator = op, Value = value is int i ? i : 0 }]
                }
            },
            nameof(AwardCriteria.Language) => criteria with
            {
                Language = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
            nameof(AwardCriteria.Country) => criteria with
            {
                Country = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
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

    private static CriteriaSet<PropertyCriterion<T>> MergeCriteriaSet<T>(
        CriteriaSet<PropertyCriterion<T>> a,
        CriteriaSet<PropertyCriterion<T>> b)
    {
        return new()
        {
            And = a.And.Concat(b.And)
        };
    }

    private static CollectionFieldInfo CreateExternalIdField(string queryName)
    {
        return new CollectionFieldInfo(
            queryName,
            typeof(string),
            new Dictionary<string, ItemFieldInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(ExternalIdCriteria.Source)] = new(nameof(ExternalIdCriteria.Source), typeof(string)),
                [nameof(ExternalIdCriteria.Value)] = new(nameof(ExternalIdCriteria.Value), typeof(string))
            },
            value => new ExternalIdCriteria
            {
                Value = new()
                {
                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = (string?)value }]
                }
            },
            CreateExternalIdFieldCriteria,
            (a, b) => MergeExternalIdCriteria((ExternalIdCriteria)a, (ExternalIdCriteria)b),
            (ctx, criteria, isSome) =>
            {
                var externalId = (ExternalIdCriteria)criteria;
                var meta = ctx.Metadata;
                if (isSome)
                {
                    ctx.SetMetadata(meta with
                    {
                        ExternalIds = new()
                        {
                            Some = meta.ExternalIds.Some.Append(externalId).ToList(),
                            None = meta.ExternalIds.None
                        }
                    });
                }
                else
                {
                    ctx.SetMetadata(meta with
                    {
                        ExternalIds = new()
                        {
                            Some = meta.ExternalIds.Some,
                            None = meta.ExternalIds.None.Append(externalId).ToList()
                        }
                    });
                }
            });
    }

    private static ExternalIdCriteria CreateExternalIdFieldCriteria(
        string propName,
        CriteriaValueOperator op,
        object? value)
    {
        var criteria = new ExternalIdCriteria();
        return propName switch
        {
            nameof(ExternalIdCriteria.Source) => criteria with
            {
                Source = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
            nameof(ExternalIdCriteria.Value) => criteria with
            {
                Value = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
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

    private static CollectionFieldInfo CreateContentRatingField(string queryName)
    {
        return new CollectionFieldInfo(
            queryName,
            typeof(string),
            new Dictionary<string, ItemFieldInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(ContentRatingCriteria.Body)] = new(nameof(ContentRatingCriteria.Body), typeof(string)),
                [nameof(ContentRatingCriteria.Rating)] = new(nameof(ContentRatingCriteria.Rating), typeof(string))
            },
            value => new ContentRatingCriteria
            {
                Rating = new()
                {
                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = (string?)value }]
                }
            },
            CreateContentRatingFieldCriteria,
            (a, b) => MergeContentRatingCriteria((ContentRatingCriteria)a, (ContentRatingCriteria)b),
            (ctx, criteria, isSome) =>
            {
                var rating = (ContentRatingCriteria)criteria;
                var meta = ctx.Metadata;
                if (isSome)
                {
                    ctx.SetMetadata(meta with
                    {
                        ContentRatings = new()
                        {
                            Some = meta.ContentRatings.Some.Append(rating).ToList(),
                            None = meta.ContentRatings.None
                        }
                    });
                }
                else
                {
                    ctx.SetMetadata(meta with
                    {
                        ContentRatings = new()
                        {
                            Some = meta.ContentRatings.Some,
                            None = meta.ContentRatings.None.Append(rating).ToList()
                        }
                    });
                }
            });
    }

    private static ContentRatingCriteria CreateContentRatingFieldCriteria(
        string propName,
        CriteriaValueOperator op,
        object? value)
    {
        var criteria = new ContentRatingCriteria();
        return propName switch
        {
            nameof(ContentRatingCriteria.Body) => criteria with
            {
                Body = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
            nameof(ContentRatingCriteria.Rating) => criteria with
            {
                Rating = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
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

    private static CollectionFieldInfo CreateImageField(string queryName)
    {
        return new CollectionFieldInfo(
            queryName,
            typeof(string),
            new Dictionary<string, ItemFieldInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(ImageCriteria.Type)] = new(nameof(ImageCriteria.Type), typeof(string)),
                [nameof(ImageCriteria.MimeType)] = new(nameof(ImageCriteria.MimeType), typeof(string)),
                [nameof(ImageCriteria.Url)] = new(nameof(ImageCriteria.Url), typeof(string)),
                [nameof(ImageCriteria.Source)] = new(nameof(ImageCriteria.Source), typeof(string)),
                [nameof(ImageCriteria.Language)] = new(nameof(ImageCriteria.Language), typeof(string)),
                [nameof(ImageCriteria.Width)] = new(nameof(ImageCriteria.Width), typeof(int)),
                [nameof(ImageCriteria.Height)] = new(nameof(ImageCriteria.Height), typeof(int))
            },
            value => new ImageCriteria
            {
                Type = new()
                {
                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = (string?)value }]
                }
            },
            CreateImageFieldCriteria,
            (a, b) => MergeImageCriteria((ImageCriteria)a, (ImageCriteria)b),
            (ctx, criteria, isSome) =>
            {
                var image = (ImageCriteria)criteria;
                var meta = ctx.Metadata;
                if (isSome)
                {
                    ctx.SetMetadata(meta with
                    {
                        Images = new()
                        {
                            Some = meta.Images.Some.Append(image).ToList(),
                            None = meta.Images.None
                        }
                    });
                }
                else
                {
                    ctx.SetMetadata(meta with
                    {
                        Images = new()
                        {
                            Some = meta.Images.Some,
                            None = meta.Images.None.Append(image).ToList()
                        }
                    });
                }
            });
    }

    private static ImageCriteria CreateImageFieldCriteria(string propName, CriteriaValueOperator op, object? value)
    {
        var criteria = new ImageCriteria();
        return propName switch
        {
            nameof(ImageCriteria.Type) => criteria with
            {
                Type = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
            nameof(ImageCriteria.MimeType) => criteria with
            {
                MimeType = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
            nameof(ImageCriteria.Url) => criteria with
            {
                Url = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
            nameof(ImageCriteria.Source) => criteria with
            {
                Source = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
            nameof(ImageCriteria.Language) => criteria with
            {
                Language = new()
                {
                    And = [new() { Operator = op, Value = (string?)value }]
                }
            },
            nameof(ImageCriteria.Width) => criteria with
            {
                Width = new()
                {
                    And = [new PropertyCriterion<int> { Operator = op, Value = value is int i ? i : 0 }]
                }
            },
            nameof(ImageCriteria.Height) => criteria with
            {
                Height = new()
                {
                    And = [new PropertyCriterion<int> { Operator = op, Value = value is int i ? i : 0 }]
                }
            },
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

    private sealed class StringContentItemField(
        string queryName,
        Action<ContentTransformContext, CriteriaValueOperator, string?, IEnumerable<string>?> addCriterion)
        : ScalarFieldInfo(queryName, typeof(string))
    {
        public override void AddCriterion(
            ContentTransformContext context, CriteriaValueOperator op,
            object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, (string?)value, values?.Cast<string>());
        }
    }

    private sealed class GuidContentItemField(
        string queryName,
        Action<ContentTransformContext, CriteriaValueOperator, Guid?, IEnumerable<Guid>?> addCriterion)
        : ScalarFieldInfo(queryName, typeof(Guid))
    {
        public override void AddCriterion(
            ContentTransformContext context, CriteriaValueOperator op,
            object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, value is Guid g ? g : null, values?.Cast<Guid>());
        }
    }

    private sealed class EnumContentItemField<T>(
        string queryName,
        Action<ContentTransformContext, CriteriaValueOperator, T?, IEnumerable<T>?> addCriterion)
        : ScalarFieldInfo(queryName, typeof(T)) where T : struct, Enum
    {
        public override void AddCriterion(
            ContentTransformContext context, CriteriaValueOperator op,
            object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, value is T e ? e : null, values?.Cast<T>());
        }
    }

    private sealed class StringMetadataField(
        string queryName,
        Action<ContentTransformContext, CriteriaValueOperator, string?, IEnumerable<string>?> addCriterion)
        : ScalarFieldInfo(queryName, typeof(string))
    {
        public override void AddCriterion(
            ContentTransformContext context, CriteriaValueOperator op,
            object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, (string?)value, values?.Cast<string>());
        }
    }

    private sealed class DateTimeMetadataField(
        string queryName,
        Action<ContentTransformContext, CriteriaValueOperator, DateTime?, IEnumerable<DateTime>?> addCriterion)
        : ScalarFieldInfo(queryName, typeof(DateTime))
    {
        public override void AddCriterion(
            ContentTransformContext context, CriteriaValueOperator op,
            object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, value is DateTime dt ? dt : null, values?.Cast<DateTime>());
        }
    }

    private sealed class TimeSpanMetadataField(
        string queryName,
        Action<ContentTransformContext, CriteriaValueOperator, TimeSpan?, IEnumerable<TimeSpan>?> addCriterion)
        : ScalarFieldInfo(queryName, typeof(TimeSpan))
    {
        public override void AddCriterion(
            ContentTransformContext context, CriteriaValueOperator op,
            object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, value is TimeSpan ts ? ts : null, values?.Cast<TimeSpan>());
        }
    }

    private sealed class StringMediaFileField(
        string queryName,
        Action<ContentTransformContext, CriteriaValueOperator, string?, IEnumerable<string>?> addCriterion)
        : ScalarFieldInfo(queryName, typeof(string))
    {
        public override void AddCriterion(
            ContentTransformContext context, CriteriaValueOperator op,
            object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, (string?)value, values?.Cast<string>());
        }
    }

    private sealed class LongMediaFileField(
        string queryName,
        Action<ContentTransformContext, CriteriaValueOperator, long?, IEnumerable<long>?> addCriterion)
        : ScalarFieldInfo(queryName, typeof(long))
    {
        public override void AddCriterion(
            ContentTransformContext context, CriteriaValueOperator op,
            object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, value is long l ? l : null, values?.Cast<long>());
        }
    }

    private sealed class StringSubjectField(
        string queryName,
        Action<SubjectTransformContext, CriteriaValueOperator, string?, IEnumerable<string>?> addCriterion)
        : SubjectScalarFieldInfo(queryName, typeof(string))
    {
        public override void AddCriterion(
            SubjectTransformContext context,
            CriteriaValueOperator op, object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, (string?)value, values?.Cast<string>());
        }
    }

    private sealed class DateTimeSubjectField(
        string queryName,
        Action<SubjectTransformContext, CriteriaValueOperator, DateTime?, IEnumerable<DateTime>?> addCriterion)
        : SubjectScalarFieldInfo(queryName, typeof(DateTime))
    {
        public override void AddCriterion(
            SubjectTransformContext context,
            CriteriaValueOperator op, object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, value is DateTime dt ? dt : null, values?.Cast<DateTime>());
        }
    }

    private sealed class StringSubjectMetadataField(
        string queryName,
        Action<SubjectTransformContext, CriteriaValueOperator, string?, IEnumerable<string>?> addCriterion)
        : SubjectScalarFieldInfo(queryName, typeof(string))
    {
        public override void AddCriterion(
            SubjectTransformContext context,
            CriteriaValueOperator op,
            object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, (string?)value, values?.Cast<string>());
        }
    }

    private sealed class DateTimeSubjectMetadataField(
        string queryName,
        Action<SubjectTransformContext, CriteriaValueOperator, DateTime?, IEnumerable<DateTime>?> addCriterion)
        : SubjectScalarFieldInfo(queryName, typeof(DateTime))
    {
        public override void AddCriterion(
            SubjectTransformContext context,
            CriteriaValueOperator op,
            object? value,
            IEnumerable<object>? values)
        {
            addCriterion(context, op, value is DateTime dt ? dt : null, values?.Cast<DateTime>());
        }
    }

    private static SubjectCollectionFieldInfo CreateSubjectAwardField(string queryName)
    {
        return new SubjectCollectionFieldInfo(
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
                Name = new()
                {
                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = (string?)value }]
                }
            },
            CreateAwardFieldCriteria,
            (a, b) => MergeAwardCriteria((AwardCriteria)a, (AwardCriteria)b),
            (ctx, criteria, isSome) =>
            {
                var award = (AwardCriteria)criteria;
                var meta = ctx.Metadata;
                if (isSome)
                {
                    ctx.SetMetadata(meta with
                    {
                        Awards = new()
                        {
                            Some = meta.Awards.Some.Append(award).ToList(),
                            None = meta.Awards.None
                        }
                    });
                }
                else
                {
                    ctx.SetMetadata(meta with
                    {
                        Awards = new()
                        {
                            Some = meta.Awards.Some,
                            None = meta.Awards.None.Append(award).ToList()
                        }
                    });
                }
            });
    }

    private static SubjectCollectionFieldInfo CreateSubjectExternalIdField(string queryName)
    {
        return new SubjectCollectionFieldInfo(
            queryName,
            typeof(string),
            new Dictionary<string, ItemFieldInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(ExternalIdCriteria.Source)] = new(nameof(ExternalIdCriteria.Source), typeof(string)),
                [nameof(ExternalIdCriteria.Value)] = new(nameof(ExternalIdCriteria.Value), typeof(string))
            },
            value => new ExternalIdCriteria
            {
                Value = new()
                {
                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = (string?)value }]
                }
            },
            CreateExternalIdFieldCriteria,
            (a, b) => MergeExternalIdCriteria((ExternalIdCriteria)a, (ExternalIdCriteria)b),
            (ctx, criteria, isSome) =>
            {
                var externalId = (ExternalIdCriteria)criteria;
                var meta = ctx.Metadata;
                if (isSome)
                {
                    ctx.SetMetadata(meta with
                    {
                        ExternalIds = new()
                        {
                            Some = meta.ExternalIds.Some.Append(externalId).ToList(),
                            None = meta.ExternalIds.None
                        }
                    });
                }
                else
                {
                    ctx.SetMetadata(meta with
                    {
                        ExternalIds = new()
                        {
                            Some = meta.ExternalIds.Some,
                            None = meta.ExternalIds.None.Append(externalId).ToList()
                        }
                    });
                }
            });
    }

    private static SubjectCollectionFieldInfo CreateSubjectImageField(string queryName)
    {
        return new SubjectCollectionFieldInfo(
            queryName,
            typeof(string),
            new Dictionary<string, ItemFieldInfo>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(ImageCriteria.Type)] = new(nameof(ImageCriteria.Type), typeof(string)),
                [nameof(ImageCriteria.MimeType)] = new(nameof(ImageCriteria.MimeType), typeof(string)),
                [nameof(ImageCriteria.Url)] = new(nameof(ImageCriteria.Url), typeof(string)),
                [nameof(ImageCriteria.Source)] = new(nameof(ImageCriteria.Source), typeof(string)),
                [nameof(ImageCriteria.Language)] = new(nameof(ImageCriteria.Language), typeof(string)),
                [nameof(ImageCriteria.Width)] = new(nameof(ImageCriteria.Width), typeof(int)),
                [nameof(ImageCriteria.Height)] = new(nameof(ImageCriteria.Height), typeof(int))
            },
            value => new ImageCriteria
            {
                Type = new()
                {
                    And = [new() { Operator = CriteriaValueOperator.Equals, Value = (string?)value }]
                }
            },
            CreateImageFieldCriteria,
            (a, b) => MergeImageCriteria((ImageCriteria)a, (ImageCriteria)b),
            (ctx, criteria, isSome) =>
            {
                var image = (ImageCriteria)criteria;
                var meta = ctx.Metadata;
                if (isSome)
                {
                    ctx.SetMetadata(meta with
                    {
                        Images = new()
                        {
                            Some = meta.Images.Some.Append(image).ToList(),
                            None = meta.Images.None
                        }
                    });
                }
                else
                {
                    ctx.SetMetadata(meta with
                    {
                        Images = new()
                        {
                            Some = meta.Images.Some,
                            None = meta.Images.None.Append(image).ToList()
                        }
                    });
                }
            });
    }
}
