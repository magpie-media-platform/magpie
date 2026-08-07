using System;

namespace Magpie.Extensions.Jellyfin;

internal sealed class JellyfinAuthenticationException(string message) : Exception(message)
{
}
