using System.Collections.Generic;

using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class CardigannDefinition
{
    [YamlMember(Alias = "id", ApplyNamingConventions = false)]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "name", ApplyNamingConventions = false)]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "description", ApplyNamingConventions = false)]
    public string? Description { get; set; }

    [YamlMember(Alias = "language", ApplyNamingConventions = false)]
    public string? Language { get; set; }

    [YamlMember(Alias = "type", ApplyNamingConventions = false)]
    public string Type { get; set; } = string.Empty;

    [YamlMember(Alias = "links", ApplyNamingConventions = false)]
    public List<string> Links { get; set; } = [];

    [YamlMember(Alias = "caps", ApplyNamingConventions = false)]
    public CapabilitiesBlock Caps { get; set; } = new();

    [YamlMember(Alias = "categories", ApplyNamingConventions = false)]
    public Dictionary<string, string> Categories { get; set; } = [];

    [YamlMember(Alias = "settings", ApplyNamingConventions = false)]
    public List<CardigannSettingDefinition> Settings { get; set; } = [];

    [YamlMember(Alias = "login", ApplyNamingConventions = false)]
    public LoginBlock? Login { get; set; }

    [YamlMember(Alias = "search", ApplyNamingConventions = false)]
    public SearchSection Search { get; set; } = new();

    [YamlMember(Alias = "download", ApplyNamingConventions = false)]
    public DownloadBlock? Download { get; set; }
}
