using System.Collections.Generic;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class DownloadBlock
{
    public List<FieldDefinition> Selectors { get; set; } = [];

    public string? Selector { get; set; }

    public string? Attribute { get; set; }

    public InfoHashBlock? InfoHash { get; set; }

    public List<FieldFilter>? Filters { get; set; }

    public string? Method { get; set; }
}
