using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Data;

namespace Magpie.Sdk.Media;

/// <summary>
/// An extractor of media info from a media file.
/// </summary>
public interface IMediaInfoExtractor
{
    /// <summary>
    /// Extracts media info from a media file.
    /// </summary>
    /// <param name="filePath">The path of the media file</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The media info, or <c>null</c> when it cannot be extracted</returns>
    Task<MediaInfo?> ExtractAsync(string filePath, CancellationToken cancellationToken = default);
}
