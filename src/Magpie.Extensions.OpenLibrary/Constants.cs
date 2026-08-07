using System;

namespace Magpie.Extensions.OpenLibrary;

internal static class Constants
{
    internal static readonly Guid ExtensionId = new("6595CD66-CF91-48DD-BA61-3A1E4D74AE31");

    public const string ProviderType = "openlibrary";

    public const string AuthorRelationshipKind = "author";

    public const string IsbnSource = "isbn";

    public const string PublisherSubjectKind = "book-publisher";

    public const string PublisherRelationshipKind = "publisher";

    public const string PersonSubjectKind = "person";

    public const string PlaceSubjectKind = "place";

    public const string TimeSubjectKind = "time";

    public const string OclcSource = "oclc";

    public const string LccnSource = "lccn";

    public const string GoodreadsSource = "goodreads";

    public const string WikidataSource = "wikidata";

    public const string ViafSource = "viaf";

    public const string IsniSource = "isni";

    public const string AmazonSource = "amazon";

    public const string LibraryThingSource = "librarything";

    public const string TranslatorRelationshipKind = "translator";

    public const string IllustratorRelationshipKind = "illustrator";

    public const string EditorRelationshipKind = "editor";

    public const string ContributorRelationshipKind = "contributor";
}
