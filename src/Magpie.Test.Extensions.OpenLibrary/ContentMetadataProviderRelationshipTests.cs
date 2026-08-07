using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.OpenLibrary;
using Magpie.Extensions.OpenLibrary.Mapping;
using Magpie.Extensions.OpenLibrary.QueryEngine;
using Magpie.Models;
using Magpie.Models.Data;
using Magpie.Models.Enums;
using Magpie.Sdk.Content;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using NUnit.Framework;

namespace Magpie.Test.Extensions.OpenLibrary;

[TestFixture]
public sealed class ContentMetadataProviderRelationshipTests
{
    private Mock<ISearchEngine> _searchEngine = null!;
    private IContentHierarchyMetadataRelationshipProvider _subject = null!;
    private MetadataContribution _contribution = null!;

    [SetUp]
    public void SetUp()
    {
        _searchEngine = new Mock<ISearchEngine>(MockBehavior.Strict);
        var definitionProvider = new Mock<IContentTypeDefinitionProvider>(MockBehavior.Strict);
        definitionProvider.SetupGet(value => value.Definition).Returns(new ContentTypeDefinition
        {
            Slug = "books",
            DisplayName = "Books",
            LevelDefinitions =
            [
                new ContentHierarchyLevelDefinition
                {
                    Slug = "book",
                    DisplayName = "Book",
                    SupportedMetadataFields = MetadataField.Language |
                        MetadataField.PublishDate |
                        MetadataField.ExternalIds |
                        MetadataField.Images |
                        MetadataField.ContentRatings |
                        MetadataField.Relationships |
                        MetadataField.SequenceNumber,
                    CustomMetadataFields = []
                }
            ]
        });
        _subject = new ContentMetadataProvider(
            [definitionProvider.Object],
            _searchEngine.Object,
            new FakeTimeProvider(new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<ContentMetadataProvider>.Instance);
        _contribution = new MetadataContribution { ProviderType = Constants.ProviderType };
        _contribution.ExternalIds.Add(new ExternalId
        {
            Source = Constants.ProviderType,
            Value = "OL1W"
        });
    }

    [Test]
    public async Task GetRelationshipsAsync_EmitsSlugKeyedPublisherSubjects_WhenWorkHasPublishers()
    {
        // Arrange
        var hit = new WorkHit
        {
            WorkOlid = "OL1W",
            Title = "Dune",
            Authors = [new AuthorPair("OL1A", "Frank Herbert")],
            Publishers = ["Chilton Books", "Ace Books", "Alfred A. Knopf"]
        };
        _searchEngine
            .Setup(value => value.ResolveWorkAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkResolution { Kind = WorkResolutionKind.Resolved, Hit = hit });
        _searchEngine
            .Setup(value => value.GetRepresentativeEditionAsync(hit, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EditionDto?)null);

        // Act
        var definitions = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = new ContentHierarchyItem
                {
                    Id = Guid.NewGuid(),
                    ContentType = "books",
                    Level = "book",
                    Slug = "work-ol1w",
                    Name = "Dune"
                },
                Contribution = _contribution,
                ExistingMetadata = [],
            }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(definitions, Has.Count.EqualTo(4));
            Assert.That(definitions[0].RelationshipKind, Is.EqualTo("author"));
            Assert.That(definitions[0].SubjectKind, Is.EqualTo("person"));
            Assert.That(definitions[0].SubjectName, Is.EqualTo("Frank Herbert"));
            Assert.That(definitions[0].SubjectExternalIds[Constants.ProviderType], Is.EqualTo("OL1A"));
            Assert.That(
                definitions[0].InitialSubjectContribution!.ExternalIds.Single().Source,
                Is.EqualTo("openlibrary"));
            Assert.That(definitions[0].InitialSubjectContribution!.ExternalIds.Single().Value, Is.EqualTo("OL1A"));
            var publishers = definitions.Skip(1).ToArray();
            Assert.That(
                publishers.Select(definition => definition.SubjectName),
                Is.EqualTo(["Chilton Books", "Ace Books", "Alfred A. Knopf"]));
            Assert.That(publishers.Select(definition => definition.RelationshipKind), Is.All.EqualTo("publisher"));
            Assert.That(publishers.Select(definition => definition.SubjectKind), Is.All.EqualTo("book-publisher"));
            Assert.That(publishers.Select(definition => definition.RelationshipOrdinal), Is.EqualTo([1, 2, 3]));
            Assert.That(
                publishers.SelectMany(definition => definition.SubjectExternalIds.Keys),
                Is.All.EqualTo("openlibrary"));
            Assert.That(
                publishers.Select(definition =>
                    definition.SubjectExternalIds["openlibrary"]),
                Is.EqualTo(["chilton_books", "ace_books", "alfred_a_knopf"]));
            Assert.That(
                publishers.Select(definition =>
                    definition.InitialSubjectContribution!.ExternalIds.Single().Source),
                Is.All.EqualTo("openlibrary"));
            Assert.That(
                publishers.Select(definition =>
                    definition.InitialSubjectContribution!.ExternalIds.Single().Value),
                Is.EqualTo(["chilton_books", "ace_books", "alfred_a_knopf"]));
            Assert.That(
                publishers.Select(definition => definition.InitialSubjectContribution!.Title),
                Is.EqualTo(["Chilton Books", "Ace Books", "Alfred A. Knopf"]));
            Assert.That(
                publishers.Select(definition => definition.InitialSubjectContribution!.ProviderType),
                Is.All.EqualTo("openlibrary"));
            var references = publishers
                .Select(definition => definition.InitialSubjectContribution!.References.Single())
                .ToArray();
            Assert.That(
                references.Select(reference => reference.Url.AbsoluteUri),
                Is.EqualTo(
                [
                    "https://openlibrary.org/publishers/Chilton_Books",
                    "https://openlibrary.org/publishers/Ace_Books",
                    "https://openlibrary.org/publishers/Alfred_A._Knopf"
                ]));
            Assert.That(references.Select(reference => reference.Kind), Is.All.EqualTo("canonical"));
            Assert.That(references.Select(reference => reference.Source), Is.All.EqualTo("openlibrary"));
            Assert.That(references.Select(reference => reference.DisplayName), Is.All.EqualTo("Open Library"));
        }
    }

    [Test]
    public async Task GetRelationshipsAsync_EmitsPersonPlaceAndTimeSubjects_WhenWorkHasSubjectFacets()
    {
        // Arrange
        var hit = new WorkHit
        {
            WorkOlid = "OL1W",
            Title = "War and Peace",
            Authors = [new AuthorPair("OL1A", "Leo Tolstoy")],
            Persons =
            [
                new SubjectFacetPair("Napoleon", "napoleon"),
                new SubjectFacetPair("Alexander I", "alexander_i")
            ],
            Places = [new SubjectFacetPair("Russia", "russia")],
            Times = [new SubjectFacetPair("1805-1812", "1805-1812")]
        };
        _searchEngine
            .Setup(value => value.ResolveWorkAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkResolution { Kind = WorkResolutionKind.Resolved, Hit = hit });
        _searchEngine
            .Setup(value => value.GetRepresentativeEditionAsync(hit, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EditionDto?)null);

        // Act
        var definitions = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = new ContentHierarchyItem
                {
                    Id = Guid.NewGuid(),
                    ContentType = "books",
                    Level = "book",
                    Slug = "work-ol1w",
                    Name = "War and Peace"
                },
                Contribution = _contribution,
                ExistingMetadata = [],
            }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                definitions.Select(definition => (definition.RelationshipKind, definition.SubjectName)),
                Is.EqualTo(
                [
                    ("author", "Leo Tolstoy"),
                    ("person", "Napoleon"),
                    ("person", "Alexander I"),
                    ("place", "Russia"),
                    ("time", "1805-1812")
                ]));
            Assert.That(
                definitions.Select(definition => definition.SubjectKind),
                Is.EqualTo(["person", "person", "person", "place", "time"]));
            Assert.That(definitions.Select(definition => definition.RelationshipOrdinal), Is.EqualTo([0, 1, 2, 3, 4]));
            Assert.That(definitions[0].SubjectExternalIds[Constants.ProviderType], Is.EqualTo("OL1A"));
            Assert.That(
                definitions.Skip(1).Select(definition =>
                    (
                        definition.SubjectExternalIds.Keys.Single(),
                        definition.SubjectExternalIds.Values.Single(),
                        definition.InitialSubjectContribution!.ExternalIds.Single().Source,
                        definition.InitialSubjectContribution!.ExternalIds.Single().Value)),
                Is.EqualTo(
                [
                    ("openlibrary", "napoleon", "openlibrary", "napoleon"),
                    ("openlibrary", "alexander_i", "openlibrary", "alexander_i"),
                    ("openlibrary", "russia", "openlibrary", "russia"),
                    ("openlibrary", "1805-1812", "openlibrary", "1805-1812")
                ]));
            Assert.That(
                definitions.Select(definition => definition.InitialSubjectContribution!.Title),
                Is.EqualTo(["Leo Tolstoy", "Napoleon", "Alexander I", "Russia", "1805-1812"]));
        }
    }

    [Test]
    public async Task GetRelationshipsAsync_EmitsNormalizedFacetIdentities_WhenSubjectKeysAreAbsent()
    {
        // Arrange
        var hit = new WorkHit
        {
            WorkOlid = "OL1W",
            Title = "War and Peace",
            Authors = [new AuthorPair("OL1A", "Leo Tolstoy")],
            Persons = [new SubjectFacetPair("Adrana Ness", null)],
            Places = [new SubjectFacetPair("St. Petersburg", null)],
            Times = [new SubjectFacetPair("19th century", null)]
        };
        _searchEngine
            .Setup(value => value.ResolveWorkAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkResolution { Kind = WorkResolutionKind.Resolved, Hit = hit });
        _searchEngine
            .Setup(value => value.GetRepresentativeEditionAsync(hit, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EditionDto?)null);

        // Act
        var definitions = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = new ContentHierarchyItem
                {
                    Id = Guid.NewGuid(),
                    ContentType = "books",
                    Level = "book",
                    Slug = "work-ol1w",
                    Name = "War and Peace"
                },
                Contribution = _contribution,
                ExistingMetadata = [],
            }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                definitions.Select(definition =>
                    (
                        definition.RelationshipKind,
                        definition.SubjectName,
                        definition.SubjectExternalIds.Values.Single())),
                Is.EqualTo(
                [
                    ("author", "Leo Tolstoy", "OL1A"),
                    ("person", "Adrana Ness", "adrana_ness"),
                    ("place", "St. Petersburg", "st_petersburg"),
                    ("time", "19th century", "19th_century")
                ]));
            Assert.That(
                definitions.Skip(1).Select(definition =>
                    definition.InitialSubjectContribution!.ExternalIds.Single().Value),
                Is.EqualTo(["adrana_ness", "st_petersburg", "19th_century"]));
        }
    }

    [Test]
    public async Task GetRelationshipsAsync_KeepsFacetIdentitiesAligned_WhenPersonKeysCollapse()
    {
        // Arrange
        var hit = new WorkHit
        {
            WorkOlid = "OL1W",
            Title = "War and Peace",
            Authors = [new AuthorPair("OL1A", "Leo Tolstoy")],
            Persons =
            [
                new SubjectFacetPair("Napoleon I, Emperor of the French", "napoleon_i_emperor_of_the_french"),
                new SubjectFacetPair("Napoleon I Emperor of the French", "napoleon_i_emperor_of_the_french"),
                new SubjectFacetPair("Alexander I", "alexander_i")
            ]
        };
        _searchEngine
            .Setup(value => value.ResolveWorkAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkResolution { Kind = WorkResolutionKind.Resolved, Hit = hit });
        _searchEngine
            .Setup(value => value.GetRepresentativeEditionAsync(hit, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EditionDto?)null);

        // Act
        var definitions = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = new ContentHierarchyItem
                {
                    Id = Guid.NewGuid(),
                    ContentType = "books",
                    Level = "book",
                    Slug = "work-ol1w",
                    Name = "War and Peace"
                },
                Contribution = _contribution,
                ExistingMetadata = [],
            }).ConfigureAwait(false);

        // Assert
        Assert.That(
            definitions.Select(definition =>
                (
                    definition.RelationshipKind,
                    definition.SubjectName,
                    definition.SubjectExternalIds.Values.Single())),
            Is.EqualTo(
            [
                ("author", "Leo Tolstoy", "OL1A"),
                ("person", "Napoleon I, Emperor of the French", "napoleon_i_emperor_of_the_french"),
                ("person", "Napoleon I Emperor of the French", "napoleon_i_emperor_of_the_french"),
                ("person", "Alexander I", "alexander_i")
            ]));
    }

    [Test]
    public async Task GetRelationshipsAsync_EmitsContributorRoleRelationships_WhenEditionHasContributors()
    {
        // Arrange
        var hit = new WorkHit
        {
            WorkOlid = "OL1W",
            Title = "The Hobbit",
            Authors = [new AuthorPair("OL1A", "J. R. R. Tolkien")],
            CoverEditionOlid = "OL9M"
        };
        var edition = new EditionDto
        {
            Contributors =
            [
                new EditionContributorDto { Role = "Translator", Name = "Wolfgang Krege" },
                new EditionContributorDto { Role = "Jacket Design", Name = "HarperCollins" },
                new EditionContributorDto { Role = "Illustrator", Name = "Alan Lee" }
            ],
            Contributions = ["Tony Ross (Illustrator)", "Lee, Alan."]
        };
        _searchEngine
            .Setup(value => value.ResolveWorkAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkResolution { Kind = WorkResolutionKind.Resolved, Hit = hit });
        _searchEngine
            .Setup(value => value.GetRepresentativeEditionAsync(hit, It.IsAny<CancellationToken>()))
            .ReturnsAsync(edition);

        // Act
        var definitions = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = new ContentHierarchyItem
                {
                    Id = Guid.NewGuid(),
                    ContentType = "books",
                    Level = "book",
                    Slug = "work-ol1w",
                    Name = "The Hobbit"
                },
                Contribution = _contribution,
                ExistingMetadata = [],
            }).ConfigureAwait(false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                definitions.Select(definition =>
                    (definition.RelationshipKind, definition.RelationshipQualifier, definition.SubjectName)),
                Is.EqualTo(
                [
                    ("author", null, "J. R. R. Tolkien"),
                    ("translator", "Translator", "Wolfgang Krege"),
                    ("contributor", "Jacket Design", "HarperCollins"),
                    ("illustrator", "Illustrator", "Alan Lee"),
                    ("illustrator", "Illustrator", "Tony Ross")
                ]));
            Assert.That(definitions.Select(definition => definition.SubjectKind), Is.All.EqualTo("person"));
            Assert.That(definitions.Select(definition => definition.RelationshipOrdinal), Is.EqualTo([0, 1, 2, 3, 4]));
            Assert.That(definitions[0].SubjectExternalIds[Constants.ProviderType], Is.EqualTo("OL1A"));
            Assert.That(
                definitions.Skip(1).Select(definition =>
                    (
                        definition.SubjectExternalIds.Keys.Single(),
                        definition.SubjectExternalIds.Values.Single(),
                        definition.InitialSubjectContribution!.ExternalIds.Single().Value)),
                Is.EqualTo(
                [
                    ("openlibrary", "wolfgang_krege", "wolfgang_krege"),
                    ("openlibrary", "harpercollins", "harpercollins"),
                    ("openlibrary", "alan_lee", "alan_lee"),
                    ("openlibrary", "tony_ross", "tony_ross")
                ]));
        }
    }

    [Test]
    public async Task GetRelationshipsAsync_ReturnsEmptyWithoutResolving_WhenItemIsNotBookLevel()
    {
        // Act
        var definitions = await _subject.GetRelationshipsAsync(
            new MetadataRelationshipRequest
            {
                Item = new ContentHierarchyItem
                {
                    Id = Guid.NewGuid(),
                    ContentType = "books",
                    Level = "author",
                    Slug = "author",
                    Name = "Frank Herbert"
                },
                Contribution = _contribution,
                ExistingMetadata = [],
            }).ConfigureAwait(false);

        // Assert
        Assert.That(definitions, Is.Empty);
    }
}
