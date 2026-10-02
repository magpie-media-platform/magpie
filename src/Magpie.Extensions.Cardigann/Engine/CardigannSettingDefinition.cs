using System.Collections.Generic;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class CardigannSettingDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Label { get; set; }

    public string? Description { get; set; }

    public object? Default { get; set; }

    public bool Required { get; set; }

    public Dictionary<string, string>? Options { get; set; }
}
