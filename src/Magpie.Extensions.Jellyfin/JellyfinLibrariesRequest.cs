using System;
using System.Collections.Generic;

namespace Magpie.Extensions.Jellyfin;

internal sealed record JellyfinLibrariesRequest
{
    public Guid? MediaServerId { get; init; }

    public IReadOnlyDictionary<string, string>? Settings { get; init; }
}
