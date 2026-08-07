using System.Collections.Generic;

namespace Magpie.Extensions.Tvmaze;

internal static class RelationshipKinds
{
    public const string PersonSubject = "person";

    public const string StudioSubject = "studio";

    public const string Cast = "cast";

    public const string GuestCast = "guest-cast";

    public const string Director = "director";

    public const string Writer = "writer";

    public const string Crew = "crew";

    public const string Network = "network";

    public static IReadOnlyList<string> PersonRelationships { get; } =
        [Cast, Crew, Director, Writer];
}
