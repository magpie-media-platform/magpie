using System;

using Magpie.Models.Filters.Subject;

namespace Magpie.Models.Filters;

/// <summary>
/// Patch for <see cref="SubjectFilter"/>.
/// </summary>
public sealed record SubjectFilterPatch
{
    /// <inheritdoc cref="SubjectFilter.Id" />
    public Guid Id { get; init; }
    /// <inheritdoc cref="SubjectFilter.Name" />
    public FieldPatch<string>? Name { get; init; }
    /// <inheritdoc cref="SubjectFilter.Criteria" />
    public FieldPatch<SubjectCriteria>? Criteria { get; init; }
    /// <inheritdoc cref="SubjectFilter.Tags" />
    public SetPatch<string, string>? Tags { get; init; }
}
