using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Abstractions.Content;

/// <summary>
/// Provides metadata for subjects from an external source.
/// </summary>
public interface ISubjectMetadataProvider
{
    /// <summary>
    /// Get the subject kinds supported by the provider.
    /// </summary>
    IEnumerable<string> SupportedSubjectKinds { get; }

    /// <summary>
    /// Fetch metadata for a subject.
    /// </summary>
    /// <param name="subject">The subject to fetch metadata for.</param>
    /// <param name="existingMetadata">The existing metadata contributions for the subject.</param>
    /// <param name="defaultLanguagePreference">The default language preference to use for fetching metadata.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<MetadataContribution?> TryGetMetadataAsync(
        Subject subject,
        IEnumerable<MetadataContribution> existingMetadata,
        string defaultLanguagePreference,
        CancellationToken cancellationToken = default);
}
