using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Releases;

namespace Magpie.Sdk.Content;

/// <summary>
/// A provider of content facts for release evaluation.
/// </summary>
public interface IContentFactProvider
{
    /// <summary>
    /// Determines whether the provider can supply facts for a content target.
    /// </summary>
    /// <param name="target">The content target</param>
    bool CanProvideFacts(ContentTarget target);

    /// <summary>
    /// Gets the facts for a supported content target.
    /// </summary>
    /// <param name="target">The content target</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<IReadOnlyList<IReadOnlyList<ContentFact>>> GetFactsAsync(
        ContentTarget target,
        CancellationToken cancellationToken = default);
}
