using System.Collections.Generic;

using Magpie.Models;

namespace Magpie.Extensions.Jellyfin;

internal sealed record JellyfinLibrariesResult
{
    public IReadOnlyList<SettingSchemaOption> Options { get; init; } = [];

    public ConfigurationFailureKind FailureKind { get; init; }
}
