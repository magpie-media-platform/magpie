using System;
using System.Collections.Generic;
using System.Text.Json;

using Magpie.Abstractions.Security;
using Magpie.Sdk.Security;

namespace Magpie.Core.Security;

internal sealed class DataProtectionService(IDataProtectionProvider dataProtectionProvider) : IDataProtectionService
{
    private const string EnvelopeKey = "__magpieSecret";

    public string ActiveKeyId => dataProtectionProvider.ActiveKeyId;

    public Dictionary<string, string> Protect(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var plaintext = JsonSerializer.Serialize(settings, JsonSerializerOptions.Default);
        return new Dictionary<string, string> { [EnvelopeKey] = dataProtectionProvider.Protect(plaintext) };
    }

    public Dictionary<string, string> Unprotect(IReadOnlyDictionary<string, string> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (!stored.TryGetValue(EnvelopeKey, out var protectedValue))
        {
            throw new InvalidOperationException("Stored secret settings do not contain a protected envelope.");
        }

        var json = dataProtectionProvider.Unprotect(protectedValue);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Default)
            ?? throw new InvalidOperationException("Stored secret settings contain an empty protected envelope.");
    }

    public string Protect(string secret)
    {
        return dataProtectionProvider.Protect(secret);
    }

    public string Unprotect(string protectedSecret)
    {
        return dataProtectionProvider.Unprotect(protectedSecret);
    }

    public bool TryGetKeyId(IReadOnlyDictionary<string, string> stored, out string keyId)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (stored.TryGetValue(EnvelopeKey, out var protectedValue))
        {
            return dataProtectionProvider.TryGetKeyId(protectedValue, out keyId);
        }

        keyId = string.Empty;
        return false;
    }
}
