using System.Collections.Generic;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class CardigannDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Language { get; set; }

    public string Type { get; set; } = string.Empty;

    public List<string> Links { get; set; } = [];

    public CapabilitiesBlock Caps { get; set; } = new();

    public Dictionary<string, string> Categories { get; set; } = [];

    public List<CardigannSettingDefinition> Settings { get; set; } = [];

    public LoginBlock? Login { get; set; }

    public SearchSection Search { get; set; } = new();

    public DownloadBlock? Download { get; set; }
}
