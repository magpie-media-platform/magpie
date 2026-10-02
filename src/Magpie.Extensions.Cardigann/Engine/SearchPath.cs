using System.Collections.Generic;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class SearchPath
{
    public string Path { get; set; } = string.Empty;

    public string? Method { get; set; }

    public Dictionary<string, string>? Inputs { get; set; }

    public ResponseBlock? Response { get; set; }
}
