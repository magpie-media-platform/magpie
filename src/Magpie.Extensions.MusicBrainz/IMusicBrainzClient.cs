using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz.Mapping;
using Magpie.Extensions.MusicBrainz.QueryEngine;

namespace Magpie.Extensions.MusicBrainz;

internal interface IMusicBrainzClient
{
    Task<FetchResult<SearchPage>> SearchAsync(Request request, Settings settings, CancellationToken cancellationToken);

    Task<FetchResult<EntityResponseDto>> GetEntityAsync(
        EntityCacheKey key,
        Settings settings,
        CancellationToken cancellationToken);

    Task<FetchResult<ReleaseGroupSearchResponseDto>> BrowseReleaseGroupsAsync(
        BrowseCacheKey key,
        Settings settings,
        CancellationToken cancellationToken);

    Task<FetchResult<ArtistBrowseResponseDto>> BrowseArtistsAsync(
        ArtistBrowseCacheKey key,
        Settings settings,
        CancellationToken cancellationToken);

    Task<FetchResult<ArtistResolutionResponse>> ResolveArtistCompositionAsync(
        ArtistResolutionRequest request,
        Settings settings,
        CancellationToken cancellationToken);

    Task<FetchResult<SearchPage>> GetIsrcAsync(string isrc, Settings settings, CancellationToken cancellationToken);
}
