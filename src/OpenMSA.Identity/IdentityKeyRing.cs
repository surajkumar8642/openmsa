using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace OpenMSA.Identity;

public sealed class IdentityKeyRing
{
    private readonly List<RsaSecurityKey> _keys = [];

    public IReadOnlyList<RsaSecurityKey> Keys => _keys;

    public RsaSecurityKey GenerateNew(string keyId)
    {
        var rsa = RSA.Create(3072);
        var key = new RsaSecurityKey(rsa) { KeyId = keyId };
        _keys.Add(key);
        return key;
    }

    public RsaSecurityKey ActiveKey => _keys[^1];
}
