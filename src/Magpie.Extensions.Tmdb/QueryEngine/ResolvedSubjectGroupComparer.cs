using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Magpie.Extensions.Tmdb.QueryEngine;

internal sealed class ResolvedSubjectGroupComparer : IEqualityComparer<ResolvedSubjectGroup>
{
    private ResolvedSubjectGroupComparer()
    {
    }

    internal static ResolvedSubjectGroupComparer Instance { get; } = new();

    /// <inheritdoc />
    public bool Equals(ResolvedSubjectGroup? x, ResolvedSubjectGroup? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null ||
            !x.BindingKey.Equals(y.BindingKey, StringComparison.Ordinal) ||
            x.Excluded != y.Excluded ||
            x.Incomplete != y.Incomplete)
        {
            return false;
        }

        return SubjectIdentities(x).SetEquals(SubjectIdentities(y)) &&
            x.IncompleteRoles.ToHashSet().SetEquals(y.IncompleteRoles) &&
            AlternativeIdentities(x).SequenceEqual(AlternativeIdentities(y), StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public int GetHashCode(ResolvedSubjectGroup obj)
    {
        var hash = new HashCode();
        hash.Add(obj.BindingKey, StringComparer.Ordinal);
        hash.Add(obj.Excluded);
        hash.Add(obj.Incomplete);
        foreach (var subject in SubjectIdentities(obj)
            .OrderBy(value => value.Id)
            .ThenBy(value => value.Role)
            .ThenBy(value => value.ViaCompanySearch))
        {
            hash.Add(subject);
        }

        foreach (var role in obj.IncompleteRoles.OrderBy(value => value))
        {
            hash.Add(role);
        }

        foreach (var alternative in AlternativeIdentities(obj))
        {
            hash.Add(alternative, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static HashSet<(int Id, SubjectRole Role, bool ViaCompanySearch)> SubjectIdentities(
        ResolvedSubjectGroup group) =>
        group.Subjects.Select(subject => (subject.Id, subject.Role, subject.ViaCompanySearch)).ToHashSet();

    private static IEnumerable<string> AlternativeIdentities(ResolvedSubjectGroup group) =>
        group.Alternatives.Select(alternative => JsonSerializer.Serialize(new
        {
            Roles = alternative.Roles.OrderBy(role => role),
            IdentityClauses = alternative.IdentityClauses
                .Select(clause => clause.OrderBy(identity => identity.Name, StringComparer.Ordinal)
                    .ThenBy(identity => identity.ExternalIdSource, StringComparer.Ordinal)
                    .ThenBy(identity => identity.ExternalId, StringComparer.Ordinal)
                    .ThenBy(identity => identity.Operator)
                    .ThenBy(identity => identity.Negate))
                .OrderBy(clause => JsonSerializer.Serialize(clause), StringComparer.Ordinal),
            alternative.Criteria
        })).OrderBy(value => value, StringComparer.Ordinal);
}
