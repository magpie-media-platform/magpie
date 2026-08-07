using System.Collections.Generic;

using Magpie.Models.Enums;
using Magpie.Models.Filtering;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal sealed record QueryFilter
{
    internal int SourceIndex { get; init; } = -1;

    public required QueryFilterKind Kind { get; init; }

    public required CriteriaValueOperator Operator { get; init; }

    public required bool Negate { get; init; }

    public CombineMode CombineMode { get; init; } = CombineMode.Or;

    public string? RemoteField { get; init; }

    public string? AlternateRemoteField { get; init; }

    public IReadOnlyList<string> StringValues { get; init; } = [];

    public IReadOnlyList<TemporalValue> TemporalValues { get; init; } = [];

    public IReadOnlyList<long> DurationMilliseconds { get; init; } = [];

    public IReadOnlyList<IdentityValue> IdentityValues { get; init; } = [];

    public IReadOnlyList<ArtistRelationshipValue> RelationshipValues { get; init; } = [];

    public IReadOnlyList<LabelRelationshipValue> LabelValues { get; init; } = [];

    public bool RemoteApplied { get; init; }

    public bool ReleaseIndexApplied { get; init; }

    public bool VerifyLocally { get; init; }

    public bool IncludePrimaryTitle { get; init; }
}
