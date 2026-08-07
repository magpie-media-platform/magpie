using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Wikidata.QueryEngine;
using Magpie.Models.Data;

namespace Magpie.Extensions.Wikidata;

internal interface IEntityResolver
{
    Task<EntityResolution> ResolveContentAsync(
        EntityKind kind,
        ContentHierarchyItem item,
        IReadOnlyList<MetadataContribution> existingMetadata,
        IReadOnlyList<MetadataContribution> ancestorMetadata,
        RequestContext context,
        CancellationToken cancellationToken);

    Task<EntityResolution> ResolveSubjectAsync(
        EntityKind kind,
        IEnumerable<MetadataContribution> existingMetadata,
        RequestContext context,
        CancellationToken cancellationToken);
}
