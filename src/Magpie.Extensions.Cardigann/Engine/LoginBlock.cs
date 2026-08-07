using System.Collections.Generic;

using YamlDotNet.Serialization;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class LoginBlock
{
    [YamlMember(Alias = "method", ApplyNamingConventions = false)]
    public string? Method { get; set; }

    [YamlMember(Alias = "path", ApplyNamingConventions = false)]
    public string? Path { get; set; }

    [YamlMember(Alias = "submitpath", ApplyNamingConventions = false)]
    public string? SubmitPath { get; set; }

    [YamlMember(Alias = "form", ApplyNamingConventions = false)]
    public string? Form { get; set; }

    [YamlMember(Alias = "inputs", ApplyNamingConventions = false)]
    public Dictionary<string, string>? Inputs { get; set; }

    [YamlMember(Alias = "selectorinputs", ApplyNamingConventions = false)]
    public Dictionary<string, FieldDefinition>? SelectorInputs { get; set; }

    [YamlMember(Alias = "getselectorinputs", ApplyNamingConventions = false)]
    public Dictionary<string, FieldDefinition>? GetSelectorInputs { get; set; }

    [YamlMember(Alias = "error", ApplyNamingConventions = false)]
    public List<LoginError>? Error { get; set; }

    [YamlMember(Alias = "captcha", ApplyNamingConventions = false)]
    public LoginCaptcha? Captcha { get; set; }

    [YamlMember(Alias = "test", ApplyNamingConventions = false)]
    public LoginTest? Test { get; set; }

    [YamlMember(Alias = "headers", ApplyNamingConventions = false)]
    public Dictionary<string, List<string>>? Headers { get; set; }
}
