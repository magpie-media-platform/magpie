using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.MusicBrainz;

internal interface ICoverArtArchiveClient
{
    Task<FetchResult<Uri>> GetReleaseGroupFrontAsync(
        string releaseGroupMbid,
        int thumbnailSize,
        Settings settings,
        CancellationToken cancellationToken);
}
