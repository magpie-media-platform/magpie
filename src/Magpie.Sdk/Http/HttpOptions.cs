using System;

namespace Magpie.Sdk.Http;

/// <summary>
/// The HTTP defaults for a Magpie host.
/// </summary>
public sealed class HttpOptions
{
    /// <summary>
    /// The configuration section name for these options.
    /// </summary>
    public const string SectionName = "Http";

    /// <summary>
    /// The total request timeout, including retries and upstream cooldown waits.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
}
