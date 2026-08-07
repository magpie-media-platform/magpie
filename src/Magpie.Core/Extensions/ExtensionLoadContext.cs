using System.Collections.Generic;

using Magpie.Models.Data;
using Magpie.Sdk.Extensions;

namespace Magpie.Core.Extensions;

internal sealed record ExtensionLoadContext(IReadOnlyList<IExtension> Loaded, IReadOnlyList<Extension> Failed);
