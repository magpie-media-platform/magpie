using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class LoginTest
{
    [YamlMember(Alias = "path", ApplyNamingConventions = false)]
    public string? Path { get; set; }

    [YamlMember(Alias = "selector", ApplyNamingConventions = false)]
    public string? Selector { get; set; }
}
