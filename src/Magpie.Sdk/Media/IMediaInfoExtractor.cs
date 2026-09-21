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
    /// <param name="cancellationToken">The token that cancels the operation</param>
    /// <returns>The media info, or <see langword="null"/> when it cannot be extracted</returns>
    Task<MediaInfo?> ExtractAsync(string filePath, CancellationToken cancellationToken = default);
}
