using System.Collections.Generic;

namespace Magpie.Extensions.Wikidata.QueryEngine;

internal sealed record SubjectFilter
{
    public IReadOnlyList<string> PropertyIds { get; init; } = [];

    public IReadOnlyList<string> Qids { get; init; } = [];

    public IReadOnlyList<string> Names { get; init; } = [];

    public IReadOnlyList<string> UriSchemes { get; init; } = [];

    public IReadOnlyList<string> UriValues { get; init; } = [];

    public IReadOnlyList<PropertyValue> ExternalIds { get; init; } = [];

    public IReadOnlyList<ExternalIdGroup> ExternalIdGroups { get; init; } = [];
}
