using System.Collections.Generic;

namespace Magpie.Extensions.Tvmaze.Pipeline;

internal sealed record QueryProgress
{
    internal bool ValidateOnly { get; set; }

    internal HashSet<int> Delivered { get; set; } = [];

    internal Dictionary<int, int> BrowsePages { get; set; } = [];

    internal HashSet<int> ExhaustedBrowse { get; set; } = [];

    internal Dictionary<int, string> ResolvedSources { get; set; } = [];

    internal QueryProgress Copy() => this with
    {
        Delivered = [.. Delivered],
        BrowsePages = new Dictionary<int, int>(BrowsePages),
        ExhaustedBrowse = [.. ExhaustedBrowse],
        ResolvedSources = new Dictionary<int, string>(ResolvedSources)
    };
}
