# Key storage

[Documentation](../../README.md) / [Development](../README.md) / [Extension components](README.md)

Magpie signs access tokens and hashes API keys with HMAC keys, which it looks up by name. `IKeyStore` [replaces where those keys live](README.md#replacement-components). You write one to keep them in an external key service instead of files on disk. It is separate from [protection of stored integration secrets](data-protection.md).

Magpie uses two named keys. `jwt-hmac` signs and validates access tokens, and `api-key-hmac` hashes API keys. The default store keeps them in `<DataRoot>/keys/<name>.key`. A name must keep identifying the same key after a move. If `api-key-hmac` changes, every existing API key stops working, and if `jwt-hmac` changes, tokens signed with the old key fail validation. A new store cannot recover keys it never received, so an extension that replaces the store in an existing installation needs migration and recovery instructions that move both keys and keep them available on every node that signs or verifies. See [Preserving keys](../../administration/maintenance.md#preserving-keys) and [shared state across nodes](../../administration/clustering.md#preparing-shared-state-and-node-identity).

Register the class as a singleton in [`IExtension.RegisterServices`](../../reference/extensions.md#entry-point). The host registers its default only when no extension does.

```csharp
services.AddSingleton<IKeyStore, ExampleKeyStore>();
```

## Contract

The store checks for a key, creates one, computes an HMAC-SHA256, and verifies one. Token signing requires that algorithm, so use the key service's cryptographic operations and not an ordinary hash. Creation must not replace an existing key. If the key exists, throw `InvalidOperationException` or `IOException` and keep it. Magpie catches only those two types, checks again that the key exists, and carries on, and any other exception fails the sign-in or API-key request. Verification must reject changed data and invalid HMACs.

Magpie calls the store synchronously and without a cancellation token for every access token it issues or validates, so a slow key service slows every authenticated request. Let storage and cryptographic failures propagate as exceptions. Return `false` only for an HMAC that does not match, and never turn an unavailable key service into a valid result. A singleton's dependencies must handle concurrent calls.

This `VerifyHmacAsync` compares in constant time, using the class's own `ComputeHmacAsync`:

```csharp
/// <inheritdoc />
public async Task<bool> VerifyHmacAsync(
    string keyName,
    byte[] data,
    byte[] hmac,
    CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    var computed = await ComputeHmacAsync(
        keyName,
        data,
        cancellationToken).ConfigureAwait(false);
    cancellationToken.ThrowIfCancellationRequested();
    return CryptographicOperations.FixedTimeEquals(computed, hmac);
}
```

See the [built-in implementation](../../../src/Magpie.Core/Security/AtomicFileKeyStore.cs) and the [IKeyStore contract](../../../src/Magpie.Sdk/Security/IKeyStore.cs).

---

[Extension components](README.md) | [Development](../README.md) | [Documentation](../../README.md)
