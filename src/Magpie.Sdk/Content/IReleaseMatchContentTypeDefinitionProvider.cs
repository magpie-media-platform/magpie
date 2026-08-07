using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Models.Releases;

namespace Magpie.Sdk.Content;

/// <summary>
/// A content type definition provider that matches releases to a content target.
/// </summary>
public interface IReleaseMatchContentTypeDefinitionProvider : IContentTypeDefinitionProvider
{
    /// <summary>
    /// Determines how strongly a release matches a content target.
    /// </summary>
    /// <param name="target">The content target to match against</param>
    /// <param name="contentFacts">The facts for the target</param>
    /// <param name="release">The release to evaluate</param>
    /// <param name="releaseFacts">The facts that produced the release</param>
    ReleaseMatchResult IsMatch(
        ContentTarget target,
        IReadOnlyList<IReadOnlyList<ContentFact>> contentFacts,
        Release release,
        IReadOnlyList<ContentFact> releaseFacts);
}
