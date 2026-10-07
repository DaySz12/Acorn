namespace Acorn.Core.Crypto;

/// <summary>Wraps the 32-byte DEK with a KEK using AES-256-GCM.</summary>
public static class KeyWrap
{
    public const int DekSize = 32;

    public static AeadBox Wrap(ReadOnlySpan<byte> kek, ReadOnlySpan<byte> dek, ReadOnlySpan<byte> associatedData)
    {
        if (dek.Length != DekSize)
        {
            throw new ArgumentException("DEK must be 32 bytes.", nameof(dek));
        }
        return Aead.Encrypt(kek, dek, associatedData);
    }

    /// <summary>Returns the DEK in a pinned array the caller must zero.</summary>
    public static byte[] Unwrap(ReadOnlySpan<byte> kek, AeadBox wrapped, ReadOnlySpan<byte> associatedData)
    {
        var dek = Aead.Decrypt(kek, wrapped, associatedData);
        if (dek.Length != DekSize)
        {
            Secrets.Zero(dek);
            throw new VaultAuthenticationException();
        }
        return dek;
    }
}
