# Stored-value protection

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

Magpie encrypts sensitive values, such as [integration credentials](settings.md#keeping-declarations-separate-from-values), before it stores them. `IDataProtectionProvider` [replaces that protection](README.md#replacement-components). You write one to keep the keys somewhere else, or to use a different scheme. A protected value outlives the process and the extension version that wrote it, so a replacement has to keep reading everything already stored.

The built-in provider uses the key in `MAGPIE_SECRET_KEY` when that variable is set, and otherwise `<DataRoot>/keys/integration-secret.key`. When the variable is set, it also reads values protected with the key file. Existing records keep the format and key of whichever provider wrote them, so a replacement must read those values or migrate them before it is activated. See [Preserving keys](../../administration/maintenance.md#preserving-keys).

Register one singleton in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point). The host registers its default only when no extension does.

```csharp
services.AddSingleton<IDataProtectionProvider, ExampleDataProtectionProvider>();
```

If the extension is later disabled or uninstalled, Magpie falls back to the built-in provider at the next start, and it cannot read values the replacement protected in another format or with another key. Include a way back to the built-in format in the extension's removal instructions.

## Contract

`Protect` returns a protected string and `Unprotect` returns the clear value. `ActiveKeyId` names the key used for new writes, and `TryGetKeyId` reports which key a protected value used. The methods are synchronous and take no cancellation token, and a singleton's dependencies must handle concurrent calls.

Magpie uses `TryGetKeyId` to find values that are not protected yet. At startup it protects each media-server and notification-rule settings record for which `TryGetKeyId` returns false, and until then it reads such a record as clear text. So return true for every value that `Protect` produced and for every older value you can read. If you do not, Magpie protects an already protected value a second time.

Changing the write key does not re-encrypt existing values. After the active key changes from A to B, new values use B and unchanged records still need A, so keep A while any record uses it. All [nodes](../../administration/clustering.md#preparing-shared-state-and-node-identity) sharing the records need a compatible format and access to the required keys.

If `Unprotect` cannot read a value, whether it is malformed, its key is gone, or authentication fails, throw. The repository logs and rethrows, so every read that includes that record fails. One unreadable download client record makes the list of download clients fail. Never return an unreadable string as a clear credential, and never log clear values or key material.

The built-in `v1:<key-id>:<base64-payload>` format is a good reference. This is a `TryGetKeyId` that reads it:

```csharp
/// <inheritdoc />
public bool TryGetKeyId(string protectedValue, out string keyId)
{
    keyId = string.Empty;
    var parts = protectedValue.Split(':', 3);
    if (parts.Length != 3 ||
        !string.Equals(parts[0], "v1", StringComparison.Ordinal))
    {
        return false;
    }

    try
    {
        _ = Convert.FromBase64String(parts[2]);
    }
    catch (FormatException)
    {
        return false;
    }

    keyId = parts[1];
    return true;
}
```

It only reads the envelope. `Unprotect` is what authenticates the payload. Use an established authenticated-encryption implementation for `Protect` and `Unprotect`, and keep the built-in format and key IDs if existing values should stay readable. See the [built-in provider](../../../src/Magpie.Core/Security/DefaultDataProtectionProvider.cs) and the [contract](../../../src/Magpie.Sdk/Security/IDataProtectionProvider.cs). Signing keys for authentication are a separate matter, covered in [Key storage](key-storage.md).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
