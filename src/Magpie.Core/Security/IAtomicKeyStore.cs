using Magpie.Sdk.Security;

namespace Magpie.Core.Security;

internal interface IAtomicKeyStore : IKeyStore
{
    string KeysDirectory { get; }

    void WriteKeyFileAtomically(string path, string contents);

    void RestrictExistingKeyFile(string path);
}
