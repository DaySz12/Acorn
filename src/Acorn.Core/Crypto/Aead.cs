using System.Security.Cryptography;

namespace Acorn.Core.Crypto;

/// <summary>AES-256-GCM output: nonce, ciphertext and tag.</summary>
public sealed record AeadBox(byte[] Nonce, byte[] Ciphertext, byte[] Tag);

/// <summary>AES-256-GCM with a fresh random 96-bit nonce for every encryption.</summary>
public static class Aead
{
    public const int KeySize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;

    public static AeadBox Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        if (key.Length != KeySize)
        {
            throw new ArgumentException("Key must be 32 bytes.", nameof(key));
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return new AeadBox(nonce, ciphertext, tag);
    }

    /// <summary>
    /// Decrypts into a pinned array the caller must zero. Any authentication failure becomes a
    /// <see cref="VaultAuthenticationException"/> with a generic message.
    /// </summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> key, AeadBox box, ReadOnlySpan<byte> associatedData)
    {
        ArgumentNullException.ThrowIfNull(box);
        if (key.Length != KeySize)
        {
            throw new ArgumentException("Key must be 32 bytes.", nameof(key));
        }
        if (box.Nonce.Length != NonceSize || box.Tag.Length != TagSize)
        {
            throw new VaultAuthenticationException();
        }

        var plaintext = Secrets.AllocatePinned(box.Ciphertext.Length);
        try
        {
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(box.Nonce, box.Ciphertext, box.Tag, plaintext, associatedData);
            return plaintext;
        }
        catch (CryptographicException)
        {
            Secrets.Zero(plaintext);
            throw new VaultAuthenticationException();
        }
    }
}
