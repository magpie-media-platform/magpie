namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record SubjectResolution
{
    internal required SubjectResolutionKind Kind { get; init; }

    internal int Id { get; init; }

    internal string? Name { get; init; }

    internal string? Warning { get; init; }

    internal bool ViaCompanySearch { get; init; }

    internal bool Incomplete { get; init; }

    internal string? ExternalIdSource { get; init; }

    internal string? ExternalId { get; init; }
}
