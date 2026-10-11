using System;
using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Sdk.Extensions;

namespace Magpie.App.Hosting;

internal sealed record LoadedExtensionSet(
    IReadOnlyList<Extension> Discovered,
    IReadOnlyList<IExtension> Loaded,
    IReadOnlyDictionary<Guid, string> PackageVersions);
