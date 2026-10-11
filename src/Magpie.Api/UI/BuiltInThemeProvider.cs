using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

using Magpie.Models.UI;
using Magpie.Sdk.UI;

namespace Magpie.Api.UI;

internal sealed class BuiltInThemeProvider : IThemeProvider
{
    public IEnumerable<ThemeDefinition> GetThemes()
    {
        using var stream = typeof(BuiltInThemeProvider).Assembly.GetManifestResourceStream("Themes/builtins.json")!;
        return JsonSerializer.Deserialize<ThemeDefinition[]>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter() }
            })!;
    }
}
