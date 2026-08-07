using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class LoginError
{
    [YamlMember(Alias = "selector", ApplyNamingConventions = false)]
    public string? Selector { get; set; }

    [YamlMember(Alias = "message", ApplyNamingConventions = false)]
    public FieldDefinition? Message { get; set; }
}
