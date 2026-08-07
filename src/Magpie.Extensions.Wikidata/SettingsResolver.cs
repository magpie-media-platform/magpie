using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Magpie.Sdk.Services;

using Microsoft.Extensions.Logging;

namespace Magpie.Extensions.Wikidata;

internal sealed class SettingsResolver(
    ISettingsService settingsService,
    ILogger<SettingsResolver> logger) : ISettingsResolver, IDisposable
{
    public const string EnvContactVariable = "MAGPIE_WIKIDATA_CONTACT";

    public const string EnvLanguageVariable = "MAGPIE_WIKIDATA_LANGUAGE";

    private static readonly Guid ExtensionId = new("A70A2CC8-9BC7-4E24-9584-A4A458E4FCFD");

    private readonly SemaphoreSlim _gate = new(1, 1);

    private Settings? _snapshot;
    private bool _missingContactWarned;

    public async Task<RequestContext> ResolveAsync(
        string? defaultLanguagePreference,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _snapshot ??= await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(_snapshot.Contact) && !_missingContactWarned)
            {
                _missingContactWarned = true;
                logger.LogWarning(
                    "Settings group {GroupName} has no {ContactKey}, and {EnvVar} is not set. " +
                    "Wikidata requests will use a generic User-Agent without contact information",
                    Settings.GroupName,
                    Settings.ContactKey,
                    EnvContactVariable);
            }

            var language = ResolveLanguage(_snapshot.Language, defaultLanguagePreference);
            var fallbackLanguages = string.Equals(
                language,
                Settings.FallbackLanguage,
                StringComparison.OrdinalIgnoreCase)
                ? (IReadOnlyList<string>)[Settings.FallbackLanguage]
                : [language, Settings.FallbackLanguage];
            var version = typeof(SettingsResolver).Assembly.GetName().Version ?? new Version(1, 0, 0);
            var contact = string.IsNullOrWhiteSpace(_snapshot.Contact)
                ? Settings.DefaultContact
                : _snapshot.Contact!;
            return new RequestContext
            {
                Language = language,
                FallbackLanguages = fallbackLanguages,
                UserAgent = string.Format(
                    CultureInfo.InvariantCulture,
                    "Magpie/{0} (Wikidata metadata provider; {1})",
                    version,
                    contact)
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string? NormalizeSetting(
        IReadOnlyDictionary<string, string> settings,
        string key,
        bool normalizeLanguage)
    {
        if (!settings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return normalizeLanguage ? NormalizeLanguage(trimmed) : trimmed;
    }

    private static string ResolveLanguage(string? configured, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return NormalizeLanguage(configured!);
        }

        return string.IsNullOrWhiteSpace(fallback)
            ? Settings.FallbackLanguage
            : NormalizeLanguage(fallback!);
    }

    private static string NormalizeLanguage(string value)
    {
        var trimmed = value.Trim();
        var separator = trimmed.IndexOf('-');
        var primary = separator > 0 ? trimmed.Substring(0, separator) : trimmed;
        if (primary.Length is < 2 or > 12)
        {
            return Settings.FallbackLanguage;
        }

        foreach (var character in primary)
        {
            if (!char.IsLetter(character))
            {
                return Settings.FallbackLanguage;
            }
        }

        return primary.ToLowerInvariant();
    }

    private async Task<Settings> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var envContact = Environment.GetEnvironmentVariable(EnvContactVariable);
        if (!string.IsNullOrWhiteSpace(envContact))
        {
            resolved[Settings.ContactKey] = envContact!;
        }

        var envLanguage = Environment.GetEnvironmentVariable(EnvLanguageVariable);
        if (!string.IsNullOrWhiteSpace(envLanguage))
        {
            resolved[Settings.LanguageKey] = envLanguage!;
        }

        var detail = await settingsService
            .GetExtensionGroupAsync(ExtensionId, Settings.GroupName, cancellationToken)
            .ConfigureAwait(false);
        if (detail is not null)
        {
            foreach (var setting in detail.Group.Settings)
            {
                if (!string.IsNullOrWhiteSpace(setting.Value))
                {
                    resolved[setting.Key] = setting.Value;
                }
            }
        }

        return new Settings
        {
            Contact = NormalizeSetting(resolved, Settings.ContactKey, normalizeLanguage: false),
            Language = NormalizeSetting(resolved, Settings.LanguageKey, normalizeLanguage: true)
        };
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
