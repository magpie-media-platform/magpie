using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models;

namespace Magpie.Sdk.Content;

/// <summary>
/// A content hierarchy metadata provider that also names the subject relationships of an item.
/// </summary>
public interface IContentHierarchyMetadataRelationshipProvider : IContentHierarchyMetadataProvider
{
    /// <summary>
    /// Gets the subject relationships for a content hierarchy item.
    /// </summary>
    /// <param name="request">The item and contribution of the relationship request</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<SubjectRelationshipDefinition>> GetRelationshipsAsync(
        MetadataRelationshipRequest request,
        CancellationToken cancellationToken = default);
}
