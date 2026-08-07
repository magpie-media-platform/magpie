using System;

using Magpie.Models.Arguments;
using Magpie.Models.Data;

namespace Magpie.Abstractions.Repositories;

/// <summary>
/// The repository contract for media files.
/// </summary>
public interface IMediaFileRepository : ICrudRepository<Guid, MediaFile>, IQueryRepository<MediaFile, MediaFileQuery>
{
}
