using System;

namespace Magpie.Core.Config;

/// <summary>
/// The release search options for a Magpie host.
/// </summary>
public sealed class SearchOptions
{
    /// <summary>
    /// The configuration section name for these options.
    /// </summary>
    public const string SectionName = "Search";

    /// <summary>
    /// The window in which a repeated search reuses the result of the previous search.
    /// </summary>
    public TimeSpan CoalescingWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The age a search result must have before a scan searches the same content again.
    /// </summary>
    public TimeSpan MinimumSearchAge { get; set; } = TimeSpan.FromHours(24);
}
