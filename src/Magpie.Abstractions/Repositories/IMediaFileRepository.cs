using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for media files.
/// </summary>
public interface IMediaFileRepository : ICrudRepository<Guid, MediaFile>, IQueryRepository<MediaFile, MediaFileQuery>
{
    /// <summary>
    /// Gets the media file with the specified host path.
    /// </summary>
    /// <param name="filePath">The full host path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The media file, or <see langword="null"/> when the path does not exist.</returns>
    Task<MediaFile?> GetByPathAsync(string filePath, CancellationToken cancellationToken = default);
}
