using System.Collections.Generic;
using System.Net.Http;

using Microsoft.AspNetCore.Routing.Patterns;

namespace Magpie.CommandLine.Descriptors;

internal sealed record MagpieCommandManifestEntry(
    string Group,
    string Command,
    HttpMethod Method,
    RoutePattern RoutePattern,
    string? Description,
    string GroupDescription,
    IReadOnlyList<MagpieCommandParameterDescriptor> PathParameters,
    IReadOnlyList<MagpieCommandParameterDescriptor> QueryParameters,
    MagpieCommandBodyKind BodyKind,
    string? FileFieldName);
