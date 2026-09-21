using System;

namespace Magpie.Extensions.Jellyfin;

internal static class Constants
{
    internal const string ProviderTypeName = "jellyfin";

    internal static readonly Guid ExtensionId = new("6701BA12-86A7-4746-8B35-144B5B167B8D");

    internal const string LibraryMappingsSettingKey = "libraryMappings";

    internal const string MappingRootFolderPathKey = "rootFolderPath";

    internal const string MappingContentTypeKey = "contentType";

    internal const string MappingServerLibraryIdKey = "serverLibraryId";
}
