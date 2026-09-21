using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Extensions.Jellyfin;

internal interface IJellyfinLibraryService
{
    Task<JellyfinLibrariesResult> ListLibrariesAsync(
        Guid? mediaServerId,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken);
}
