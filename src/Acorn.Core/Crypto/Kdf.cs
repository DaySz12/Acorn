using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace Acorn.Core.Crypto;

/// <summary>Key derivation: Argon2id for the master password, HKDF-SHA256 for the recovery key.</summary>
public static class Kdf
{
    public const int KeySize = 32;
    public const int SaltSize = 16;

    private static ReadOnlySpan<byte> RecoveryInfo => "Acorn recovery KEK v1"u8;

    /// <summary>Derives a 32-byte KEK from the UTF-8 master password. Returns a pinned array the caller must zero.</summary>
    public static byte[] DerivePasswordKey(ReadOnlySpan<byte> passwordUtf8, ReadOnlySpan<byte> salt, Argon2idParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        // Validate before allocating anything: a hostile header must not be able to trigger a huge allocation.
        parameters.EnsureWithinBounds();
        if (salt.Length != SaltSize)
        {
            throw new ArgumentException("Salt must be 16 bytes.", nameof(salt));
        }

        var password = Secrets.AllocatePinned(passwordUtf8.Length);
        passwordUtf8.CopyTo(password);
        byte[]? derived = null;
        try
        {
            using var argon = new Argon2id(password)
            {
                Salt = salt.ToArray(),
                MemorySize = parameters.MemoryKiB,
                Iterations = parameters.Iterations,
                DegreeOfParallelism = parameters.Parallelism,
            };
            derived = argon.GetBytes(KeySize);

            var key = Secrets.AllocatePinned(KeySize);
            derived.CopyTo(key, 0);
            return key;
        }
        finally
        {
            Secrets.Zero(password);
            Secrets.Zero(derived);
        }
    }

    /// <summary>
    /// Derives a 32-byte KEK from the recovery key with HKDF-SHA256. The recovery key is 200 random
    /// bits, so a slow KDF adds nothing (see docs/FORMAT.md). Returns a pinned array the caller must zero.
    /// </summary>
    public static byte[] DeriveRecoveryKey(ReadOnlySpan<byte> recoveryKey, ReadOnlySpan<byte> salt)
    {
        if (salt.Length != SaltSize)
        {
            throw new ArgumentException("Salt must be 16 bytes.", nameof(salt));
        }

        var key = Secrets.AllocatePinned(KeySize);
        HKDF.DeriveKey(HashAlgorithmName.SHA256, recoveryKey, key, salt, RecoveryInfo);
        return key;
    }
}
