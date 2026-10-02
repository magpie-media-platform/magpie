using System.Collections.Generic;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class LoginBlock
{
    public string? Method { get; set; }

    public string? Path { get; set; }

    public string? SubmitPath { get; set; }

    public string? Form { get; set; }

    public Dictionary<string, string>? Inputs { get; set; }

    public Dictionary<string, FieldDefinition>? SelectorInputs { get; set; }

    public Dictionary<string, FieldDefinition>? GetSelectorInputs { get; set; }

    public List<LoginError>? Error { get; set; }

    public LoginCaptcha? Captcha { get; set; }

    public LoginTest? Test { get; set; }

    public Dictionary<string, List<string>>? Headers { get; set; }
}
