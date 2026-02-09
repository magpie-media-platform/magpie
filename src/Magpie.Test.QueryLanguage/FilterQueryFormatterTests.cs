using System;

using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.QueryLanguage;

using NUnit.Framework;

namespace Magpie.Test.QueryLanguage;

[TestFixture]
public class FilterQueryFormatterTests
{
    [Test]
    public void FormatContentFilterCriteria_ReturnsEmptyString_WhenCriteriaEmpty()
    {
        // Arrange
        var criteria = new ContentFilterCriteria();

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void FormatSubjectFilterCriteria_ReturnsEmptyString_WhenCriteriaEmpty()
    {
        // Arrange
        var criteria = new SubjectFilterCriteria();

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenNameEqualsSet()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Name = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.Equals, Value = "Breaking Bad" }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result, Is.EqualTo("Name = \"Breaking Bad\""));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenMonitoringEqualsSet()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Monitoring = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Value = MonitoringIntent.Active
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result, Is.EqualTo("Monitoring = Active"));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenFileSizeGreaterThan()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            MediaFiles = new()
            {
                And =
                [
                    new()
                    {
                        FileSize = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Operator = CriteriaValueOperator.GreaterThan,
                                    Value = 1000000000
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result, Is.EqualTo("FileSize > 1000000000"));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenMultipleScalarsSet()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Monitoring = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Value = MonitoringIntent.Active
                                }
                            ]
                        },
                        ContentType = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.Equals, Value = "show" }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("Monitoring = Active"));
            Assert.That(result, Does.Contain("ContentType = \"show\""));
            Assert.That(result, Does.Contain(" and "));
        }
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenTitleContainsSet()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Metadata = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Title = new()
                                    {
                                        And =
                                        [
                                            new()
                                            {
                                                Operator = CriteriaValueOperator.ContainsString,
                                                Value = "Star"
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result, Is.EqualTo("Title contains \"Star\""));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenDateGreaterThanOrEqual()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Metadata = new()
                        {
                            And =
                            [
                                new()
                                {
                                    PublishDate = new()
                                    {
                                        And =
                                        [
                                            new()
                                            {
                                                Operator = CriteriaValueOperator.GreaterThanOrEqual,
                                                Value = new(2020, 1, 1)
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result,
            Is.EqualTo("PublishDate >= \"2020-01-01\""));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenDurationSet()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Metadata = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Duration = new()
                                    {
                                        And =
                                        [
                                            new()
                                            {
                                                Operator = CriteriaValueOperator.GreaterThan,
                                                Value = TimeSpan.FromHours(2)
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result, Is.EqualTo("Duration > \"02:00:00\""));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenGenreHasSet()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Metadata = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Genres = new()
                                    {
                                        Some =
                                        [
                                            new()
                                            {
                                                Name = new()
                                                {
                                                    And =
                                                    [
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.Equals,
                                                            Value = "Action"
                                                        }
                                                    ]
                                                }
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result, Is.EqualTo("Genre has \"Action\""));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenGenreNotHasSet()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Metadata = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Genres = new()
                                    {
                                        None =
                                        [
                                            new()
                                            {
                                                Name = new()
                                                {
                                                    And =
                                                    [
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.Equals,
                                                            Value = "Horror"
                                                        }
                                                    ]
                                                }
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result, Is.EqualTo("Genre !has \"Horror\""));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenMultipleGenresAsOr()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Metadata = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Genres = new()
                                    {
                                        Some =
                                        [
                                            new()
                                            {
                                                Name = new()
                                                {
                                                    And =
                                                    [
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.Equals,
                                                            Value = "Action"
                                                        }
                                                    ]
                                                }
                                            },
                                            new()
                                            {
                                                Name = new()
                                                {
                                                    And =
                                                    [
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.Equals,
                                                            Value = "Comedy"
                                                        }
                                                    ]
                                                }
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result,
            Is.EqualTo("Genre has \"Action\" or Genre has \"Comedy\""));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenGenrePluralWithMultipleConditions()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Metadata = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Genres = new()
                                    {
                                        Some =
                                        [
                                            new()
                                            {
                                                Name = new()
                                                {
                                                    And =
                                                    [
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.StartsWithString,
                                                            Value = "A"
                                                        },
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.EndsWithString,
                                                            Value = "ion"
                                                        }
                                                    ]
                                                }
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result,
            Is.EqualTo("Genres has (Name startswith \"A\" and Name endswith \"ion\")"));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenComplexAwardSet()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Metadata = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Awards = new()
                                    {
                                        Some =
                                        [
                                            new()
                                            {
                                                Body = new()
                                                {
                                                    And =
                                                    [
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.Equals,
                                                            Value = "Academy Awards"
                                                        }
                                                    ]
                                                },
                                                Name = new()
                                                {
                                                    And =
                                                    [
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.Equals,
                                                            Value = "Best Picture"
                                                        }
                                                    ]
                                                },
                                                Year = new()
                                                {
                                                    And =
                                                    [
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.GreaterThanOrEqual,
                                                            Value = 2000
                                                        }
                                                    ]
                                                }
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result,
            Is.EqualTo(
                "Awards has (Body = \"Academy Awards\" and Name = \"Best Picture\" and Year >= 2000)"));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenAllOperatorsUsed()
    {
        // Arrange
        AssertOperator(CriteriaValueOperator.Equals, "=");
        AssertOperator(CriteriaValueOperator.NotEquals, "!=");
        AssertOperator(CriteriaValueOperator.GreaterThan, ">");
        AssertOperator(CriteriaValueOperator.GreaterThanOrEqual, ">=");
        AssertOperator(CriteriaValueOperator.LessThan, "<");
        AssertOperator(CriteriaValueOperator.LessThanOrEqual, "<=");
        AssertOperatorString(CriteriaValueOperator.ContainsString, "contains");
        AssertOperatorString(CriteriaValueOperator.NotContainsString, "!contains");
        AssertOperatorString(CriteriaValueOperator.StartsWithString, "startswith");
        AssertOperatorString(CriteriaValueOperator.NotStartsWithString, "!startswith");
        AssertOperatorString(CriteriaValueOperator.EndsWithString, "endswith");
        AssertOperatorString(CriteriaValueOperator.NotEndsWithString, "!endswith");
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenNullOperatorUsed()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Name = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.IsNull }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result, Is.EqualTo("Name null"));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenNotNullOperatorUsed()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Name = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.IsNotNull }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result, Is.EqualTo("Name !null"));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenInOperatorUsed()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        ContentType = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.In, Values = ["movie", "show"] }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result,
            Is.EqualTo("ContentType in (\"movie\", \"show\")"));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenNotInOperatorUsed()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        ContentType = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.NotIn, Values = ["movie", "show"] }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result,
            Is.EqualTo("ContentType !in (\"movie\", \"show\")"));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenMultipleSectionsWithAnd()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        ContentType = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.Equals, Value = "movie" }]
                        }
                    }
                ]
            },
            MediaFiles = new()
            {
                And =
                [
                    new()
                    {
                        FileSize = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Operator = CriteriaValueOperator.GreaterThan,
                                    Value = 5000000000
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result,
            Is.EqualTo("ContentType = \"movie\" and FileSize > 5000000000"));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenGenreHasAndNotHasTogether()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Metadata = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Genres = new()
                                    {
                                        Some =
                                        [
                                            new()
                                            {
                                                Name = new()
                                                {
                                                    And =
                                                    [
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.Equals,
                                                            Value = "Action"
                                                        }
                                                    ]
                                                }
                                            }
                                        ],
                                        None =
                                        [
                                            new()
                                            {
                                                Name = new()
                                                {
                                                    And =
                                                    [
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.Equals,
                                                            Value = "Horror"
                                                        }
                                                    ]
                                                }
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result,
            Is.EqualTo("Genre has \"Action\" and Genre !has \"Horror\""));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenOrInAndContextWithParentheses()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Monitoring = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Operator = CriteriaValueOperator.Equals,
                                    Value = MonitoringIntent.Active
                                }
                            ]
                        },
                        Metadata = new()
                        {
                            And =
                            [
                                new()
                                {
                                    Genres = new()
                                    {
                                        Some =
                                        [
                                            new()
                                            {
                                                Name = new()
                                                {
                                                    And =
                                                    [
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.Equals,
                                                            Value = "Action"
                                                        }
                                                    ]
                                                }
                                            },
                                            new()
                                            {
                                                Name = new()
                                                {
                                                    And =
                                                    [
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.Equals,
                                                            Value = "Comedy"
                                                        }
                                                    ]
                                                }
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result,
            Is.EqualTo(
                "Monitoring = Active and (Genre has \"Action\" or Genre has \"Comedy\")"));
    }

    [Test]
    public void FormatSubjectFilterCriteria_ReturnsQuery_WhenKindEqualsSet()
    {
        // Arrange
        var criteria = new SubjectFilterCriteria
        {
            Kind = new()
            {
                And = [new() { Operator = CriteriaValueOperator.Equals, Value = "person" }]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result, Is.EqualTo("Kind = \"person\""));
    }

    [Test]
    public void FormatSubjectFilterCriteria_ReturnsQuery_WhenDateLastRefreshedSet()
    {
        // Arrange
        var criteria = new SubjectFilterCriteria
        {
            DateLastRefreshed = new()
            {
                And =
                [
                    new()
                    {
                        Operator = CriteriaValueOperator.LessThan,
                        Value = new (2024, 1, 1)
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result,
            Is.EqualTo("DateLastRefreshed < \"2024-01-01\""));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenGuidEqualsSet()
    {
        // Arrange
        var id = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        ParentId = new()
                        {
                            And = [new() { Operator = CriteriaValueOperator.Equals, Value = id }]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result,
            Is.EqualTo(
                "ParentId = \"12345678-1234-1234-1234-123456789abc\""));
    }

    [Test]
    public void FormatContentFilterCriteria_ReturnsQuery_WhenExternalIdCollectionSet()
    {
        // Arrange
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Metadata = new()
                        {
                            And =
                            [
                                new()
                                {
                                    ExternalIds = new()
                                    {
                                        Some =
                                        [
                                            new()
                                            {
                                                Source = new()
                                                {
                                                    And =
                                                    [
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.Equals,
                                                            Value = "imdb"
                                                        }
                                                    ]
                                                },
                                                Value = new()
                                                {
                                                    And =
                                                    [
                                                        new()
                                                        {
                                                            Operator = CriteriaValueOperator.Equals,
                                                            Value = "tt0903747"
                                                        }
                                                    ]
                                                }
                                            }
                                        ]
                                    }
                                }
                            ]
                        }
                    }
                ]
            }
        };

        // Act
        var result = FilterQueryFormatter.Format(criteria);

        // Assert
        Assert.That(result,
            Is.EqualTo(
                "ExternalIds has (Source = \"imdb\" and Value = \"tt0903747\")"));
    }

    private static void AssertOperator(CriteriaValueOperator op, string expectedSyntax)
    {
        var criteria = new ContentFilterCriteria
        {
            MediaFiles = new()
            {
                And =
                [
                    new()
                    {
                        FileSize = new()
                        {
                            And = [new() { Operator = op, Value = 100 }]
                        }
                    }
                ]
            }
        };

        var result = FilterQueryFormatter.Format(criteria);

        Assert.That(result,
            Is.EqualTo($"FileSize {expectedSyntax} 100"));
    }

    private static void AssertOperatorString(CriteriaValueOperator op, string expectedSyntax)
    {
        var criteria = new ContentFilterCriteria
        {
            ContentHierarchyItem = new()
            {
                And =
                [
                    new()
                    {
                        Name = new()
                        {
                            And = [new() { Operator = op, Value = "test" }]
                        }
                    }
                ]
            }
        };

        var result = FilterQueryFormatter.Format(criteria);

        Assert.That(result,
            Is.EqualTo($"Name {expectedSyntax} \"test\""));
    }

    [TestFixture]
    public class ContentFilterRoundTripTests
    {
        [TestCase("Name = \"Breaking Bad\"")]
        [TestCase("Monitoring = Active")]
        [TestCase("FileSize > 1000000000")]
        [TestCase("Title contains \"Star\"")]
        [TestCase("PublishDate >= \"2020-01-01\"")]
        [TestCase("Duration > \"02:00:00\"")]
        [TestCase("Name null")]
        [TestCase("Name !null")]
        [TestCase("ContentType in (\"movie\", \"show\")")]
        [TestCase("ContentType !in (\"movie\", \"show\")")]
        [TestCase("Genre has \"Action\"")]
        [TestCase("Genre !has \"Horror\"")]
        [TestCase("ParentId = \"12345678-1234-1234-1234-123456789abc\"")]
        public void RoundTrip_PreservesQuery_WhenSimpleScalar(string query)
        {
            // Arrange
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True,
                $"Parse failed: {parseResult.Error?.Message}");

            var transformResult =
                FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True,
                $"Transform failed: {transformResult.Error?.Message}");

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted, Is.EqualTo(query));
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenMultipleScalars()
        {
            // Arrange
            var query = "ContentType = \"show\" and Monitoring = Active";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(formatted,
                    Does.Contain("ContentType = \"show\""));
                Assert.That(formatted,
                    Does.Contain("Monitoring = Active"));
                Assert.That(formatted, Does.Contain(" and "));
            }
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenMultipleGenresAsOr()
        {
            // Arrange
            var query = "Genre has \"Action\" or Genre has \"Comedy\"";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted, Is.EqualTo(query));
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenGenreHasAndNotHas()
        {
            // Arrange
            var query = "Genre has \"Action\" and Genre !has \"Horror\"";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted, Is.EqualTo(query));
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenGenrePluralWithMultipleConditions()
        {
            // Arrange
            var query = "Genres has (Name startswith \"A\" and Name endswith \"ion\")";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted, Is.EqualTo(query));
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenComplexAwardSet()
        {
            // Arrange
            var query = "Awards has (Body = \"Academy Awards\" and Name = \"Best Picture\" and Year >= 2000)";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted, Is.EqualTo(query));
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenExternalIdCollectionSet()
        {
            // Arrange
            var query = "ExternalIds has (Source = \"imdb\" and Value = \"tt0903747\")";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted, Is.EqualTo(query));
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenMultipleSections()
        {
            // Arrange
            var query = "ContentType = \"movie\" and FileSize > 5000000000";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted, Is.EqualTo(query));
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenOrInAndContext()
        {
            // Arrange
            var query = "Monitoring = Active and (Genre has \"Action\" or Genre has \"Comedy\")";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted, Is.EqualTo(query));
        }

        [Test]
        public void RoundTrip_NormalizesFormat_WhenDurationUsesShorthand()
        {
            var query = "duration > \"2h\"";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToContentFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted,
                Is.EqualTo("Duration > \"02:00:00\""));
        }
    }

    [TestFixture]
    public class SubjectFilterRoundTripTests
    {
        [TestCase("Kind = \"person\"")]
        [TestCase("DateLastRefreshed < \"2024-01-01\"")]
        [TestCase("Title contains \"Smith\"")]
        [TestCase("BirthDate >= \"1990-01-01\"")]
        public void RoundTrip_PreservesQuery_WhenSimpleScalar(string query)
        {
            // Arrange
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True,
                $"Parse failed: {parseResult.Error?.Message}");

            var transformResult =
                FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True,
                $"Transform failed: {transformResult.Error?.Message}");

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted, Is.EqualTo(query));
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenMultipleScalars()
        {
            // Arrange
            var query = "Kind = \"person\" and Title contains \"John\"";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted, Is.EqualTo(query));
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenSimpleAwardHas()
        {
            // Arrange
            var query = "Award has \"Oscar\"";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted,
                Is.EqualTo("Awards has (Name = \"Oscar\")"));
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenComplexAwardSet()
        {
            // Arrange
            var query = "Awards has (Body = \"Academy Awards\" and Name = \"Best Actor\" and Year >= 2000)";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted, Is.EqualTo(query));
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenExternalIdCollectionSet()
        {
            // Arrange
            var query = "ExternalIds has (Source = \"imdb\" and Value = \"nm0000138\")";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted, Is.EqualTo(query));
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenImageCollectionSet()
        {
            // Arrange
            var query = "Images has (Type = \"poster\")";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted, Is.EqualTo(query));
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenAwardNotHas()
        {
            // Arrange
            var query = "Award !has \"Razzie\"";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted,
                Is.EqualTo("Awards !has (Name = \"Razzie\")"));
        }

        [Test]
        public void RoundTrip_PreservesQuery_WhenMultipleAwardsAsOr()
        {
            // Arrange
            var query = "Award has \"Oscar\" or Award has \"Emmy\"";
            var parseResult = FilterQueryParser.Parse(query);
            Assert.That(parseResult,
                Has.Property(nameof(parseResult.IsSuccess)).True);

            var transformResult =
                FilterQueryTransform.ToSubjectFilterCriteria(parseResult.Value!);
            Assert.That(transformResult,
                Has.Property(nameof(transformResult.IsSuccess)).True);

            // Act
            var formatted = FilterQueryFormatter.Format(transformResult.Value!);

            // Assert
            Assert.That(formatted,
                Is.EqualTo(
                    "Awards has (Name = \"Oscar\") or Awards has (Name = \"Emmy\")"));
        }
    }
}
