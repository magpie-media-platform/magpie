using System.Collections.Generic;

using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class SearchPath
{
    [YamlMember(Alias = "path", ApplyNamingConventions = false)]
    public string Path { get; set; } = string.Empty;

    [YamlMember(Alias = "inputs", ApplyNamingConventions = false)]
    public Dictionary<string, string>? Inputs { get; set; }

    [YamlMember(Alias = "response", ApplyNamingConventions = false)]
    public ResponseBlock? Response { get; set; }
}
