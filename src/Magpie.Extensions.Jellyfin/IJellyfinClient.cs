using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Extensions.Jellyfin.Models;

namespace Magpie.Extensions.Jellyfin;

internal interface IJellyfinClient
{
    Task<SystemInfo> GetSystemInfoAsync(JellyfinSettings settings, CancellationToken cancellationToken);

    Task<bool> RefreshAllLibrariesAsync(JellyfinSettings settings, CancellationToken cancellationToken);

    Task<bool> RefreshLibraryAsync(
        JellyfinSettings settings,
        string folderId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<VirtualFolder>> GetVirtualFoldersAsync(
        JellyfinSettings settings,
        CancellationToken cancellationToken);

    Task<DeepLinkLookupResult> TryFindItemIdByExternalIdsAsync(
        JellyfinSettings settings,
        IReadOnlyDictionary<string, string> externalIds,
        string? title,
        string? parentLibraryId,
        CancellationToken cancellationToken);
}
