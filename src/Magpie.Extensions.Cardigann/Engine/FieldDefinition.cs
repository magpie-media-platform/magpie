using System.Collections.Generic;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class FieldDefinition
{
    public string? Selector { get; set; }

    public string? Attribute { get; set; }

    public string? Text { get; set; }

    public Dictionary<string, string>? Case { get; set; }

    public bool Optional { get; set; }

    public string? Default { get; set; }

    public List<FieldFilter>? Filters { get; set; }
}
