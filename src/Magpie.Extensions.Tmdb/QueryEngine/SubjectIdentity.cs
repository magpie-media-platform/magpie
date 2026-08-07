using Magpie.Models.Enums;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed record SubjectIdentity(
    string? Name,
    string? ExternalIdSource,
    string? ExternalId,
    CriteriaValueOperator Operator = CriteriaValueOperator.Equals,
    bool Negate = false);
