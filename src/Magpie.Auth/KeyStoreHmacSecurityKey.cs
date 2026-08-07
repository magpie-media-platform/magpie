using Magpie.Sdk.Security;

using Microsoft.IdentityModel.Tokens;

namespace Magpie.Auth;

internal sealed class KeyStoreHmacSecurityKey(IKeyStore keyStore, string keyName) : SecurityKey
{
    public override int KeySize => 256;

    internal IKeyStore KeyStore { get; } = keyStore;

    internal string KeyName { get; } = keyName;
}
