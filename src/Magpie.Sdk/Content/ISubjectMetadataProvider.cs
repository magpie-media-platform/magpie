using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Sdk.Content;

/// <summary>
/// A source of subject metadata from an external service.
/// </summary>
public interface ISubjectMetadataProvider
{
    /// <summary>
    /// The subject kinds that this provider supplies metadata for.
    /// </summary>
    IEnumerable<string> SupportedSubjectKinds { get; }

    /// <summary>
    /// Gets the metadata for a subject.
    /// </summary>
    /// <param name="subject">The subject to get metadata for</param>
    /// <param name="existingMetadata">The existing metadata contributions for the subject</param>
    /// <param name="defaultLanguagePreference">The language to prefer for metadata text</param>
    /// <param name="cancellationToken">The token that cancels the operation</param>
    Task<MetadataContribution?> TryGetMetadataAsync(
        Subject subject,
        IEnumerable<MetadataContribution> existingMetadata,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default);
}
