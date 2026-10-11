using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Magpie.Models.UI;
using Magpie.Sdk.UI;

using Microsoft.Extensions.Logging;

namespace Magpie.Api.UI;

internal sealed class ThemeCatalog
{
    private readonly Dictionary<string, (ThemeListItem Theme, Assembly Assembly, string FileName)> _themes =
        new(StringComparer.Ordinal);

    public ThemeCatalog(IEnumerable<IThemeProvider> providers, ILogger<ThemeCatalog> logger)
    {
        foreach (var provider in providers)
        {
            try
            {
                var assembly = provider.GetType().Assembly;
                var definitions = provider.GetThemes().ToArray();
                foreach (var definition in definitions)
                {
                    if (string.IsNullOrWhiteSpace(definition.Name) ||
                        string.IsNullOrWhiteSpace(definition.FileName) ||
                        !Enum.IsDefined(definition.Variant) ||
                        !definition.FileName.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
                        assembly.GetManifestResourceInfo(definition.FileName) is null)
                    {
                        logger.LogWarning(
                            "Theme provider {Provider} supplied an invalid theme",
                            provider.GetType().FullName);
                        continue;
                    }

                    var theme = new ThemeListItem
                    {
                        Id = $"{assembly.GetName().Name}/{definition.Name}/{definition.Variant}",
                        Name = definition.Name,
                        Variant = definition.Variant
                    };
                    if (!_themes.TryAdd(theme.Id, (theme, assembly, definition.FileName)))
                    {
                        logger.LogWarning(
                            "Theme provider {Provider} supplied duplicate theme {ThemeId}",
                            provider.GetType().FullName,
                            theme.Id);
                    }
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Theme provider {Provider} failed to list themes",
                    provider.GetType().FullName);
            }
        }
    }

    public static ThemeSettings DefaultSettings => new()
    {
        Mode = ThemeMode.Dark,
        DarkTheme = "Magpie.Api/Tokyo Night/Dark",
        LightTheme = "Magpie.Api/Frost/Light"
    };

    public IReadOnlyList<ThemeListItem> List() => _themes.Values.Select(entry => entry.Theme).ToArray();

    public Stream? OpenStylesheet(string id) => _themes.TryGetValue(id, out var entry)
        ? entry.Assembly.GetManifestResourceStream(entry.FileName)
        : null;
}
