using System;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.MusicBrainz.Mapping;

namespace Magpie.Extensions.MusicBrainz.QueryEngine;

internal interface IQueryCoordinator
{
    Task<FetchResult<SearchPage>> GetSearchPageAsync(
        SearchCacheKey key,
        Func<CancellationToken, Task<FetchResult<SearchPage>>> loader,
        CancellationToken cancellationToken);

    Task<FetchResult<VerifiedScan>> GetVerifiedScanAsync(
        string fingerprint,
        Func<VerifiedScan, bool> isSufficient,
        Func<VerifiedScan?, CancellationToken, Task<FetchResult<VerifiedScan>>> loader,
        CancellationToken cancellationToken);

    Task<FetchResult<EntityResponseDto>> GetEntityAsync(
        EntityCacheKey key,
        Func<CancellationToken, Task<FetchResult<EntityResponseDto>>> loader,
        CancellationToken cancellationToken);

    Task<FetchResult<ReleaseGroupSearchResponseDto>> GetBrowsePageAsync(
        BrowseCacheKey key,
        Func<CancellationToken, Task<FetchResult<ReleaseGroupSearchResponseDto>>> loader,
        CancellationToken cancellationToken);

    Task<FetchResult<ArtistBrowseResponseDto>> GetArtistBrowsePageAsync(
        ArtistBrowseCacheKey key,
        Func<CancellationToken, Task<FetchResult<ArtistBrowseResponseDto>>> loader,
        CancellationToken cancellationToken);

    Task<FetchResult<ArtistResolutionResponse>> GetArtistResolutionAsync(
        ArtistResolutionCacheKey key,
        Func<CancellationToken, Task<FetchResult<ArtistResolutionResponse>>> loader,
        CancellationToken cancellationToken);

    Task<FetchResult<SearchPage>> GetIsrcAsync(
        string isrc,
        Func<CancellationToken, Task<FetchResult<SearchPage>>> loader,
        CancellationToken cancellationToken);

    Task<FetchResult<Uri>> GetCoverAsync(
        CoverCacheKey key,
        Func<CancellationToken, Task<FetchResult<Uri>>> loader,
        CancellationToken cancellationToken);
}
