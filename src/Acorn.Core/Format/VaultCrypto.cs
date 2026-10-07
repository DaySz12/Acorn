using System.Security.Cryptography;
using Acorn.Core.Crypto;

namespace Acorn.Core.Format;

/// <summary>
/// Key hierarchy operations: wrap/unwrap the DEK and seal/open the payload.
/// All returned key arrays are pinned and must be zeroed by the caller.
/// </summary>
public static class VaultCrypto
{
    public static byte[] GenerateDek()
    {
        var dek = Secrets.AllocatePinned(KeyWrap.DekSize);
        RandomNumberGenerator.Fill(dek);
        return dek;
    }

    /// <summary>Builds a current-version header with fresh salts and both DEK wraps.</summary>
    public static VaultHeader CreateHeader(ReadOnlySpan<byte> dek, ReadOnlySpan<byte> passwordUtf8, Argon2idParameters kdf, RecoveryKey recoveryKey)
    {
        ArgumentNullException.ThrowIfNull(recoveryKey);
        var (byPassword, passwordSalt) = WrapWithPassword(dek, passwordUtf8, kdf);
        var (byRecovery, recoverySalt) = WrapWithRecoveryKey(dek, recoveryKey);
        return new VaultHeader(VaultFormat.CurrentVersion, kdf, passwordSalt, recoverySalt, byPassword, byRecovery);
    }

    /// <summary>Replaces only the password wrap (new salt, possibly new KDF parameters). The recovery wrap is kept.</summary>
    public static VaultHeader RewrapPassword(VaultHeader header, ReadOnlySpan<byte> dek, ReadOnlySpan<byte> passwordUtf8, Argon2idParameters kdf)
    {
        ArgumentNullException.ThrowIfNull(header);
        var (byPassword, passwordSalt) = WrapWithPassword(dek, passwordUtf8, kdf);
        return header with
        {
            FormatVersion = VaultFormat.CurrentVersion,
            PasswordKdf = kdf,
            PasswordSalt = passwordSalt,
            WrappedDekByPassword = byPassword,
        };
    }

    /// <summary>Derives the KEK using the parameters stored in the header and unwraps the DEK.</summary>
    public static byte[] UnwrapWithPassword(VaultHeader header, ReadOnlySpan<byte> passwordUtf8)
    {
        ArgumentNullException.ThrowIfNull(header);
        var kek = Kdf.DerivePasswordKey(passwordUtf8, header.PasswordSalt, header.PasswordKdf);
        try
        {
            return KeyWrap.Unwrap(kek, header.WrappedDekByPassword, VaultCodec.PasswordWrapAad(header.PasswordKdf, header.PasswordSalt));
        }
        finally
        {
            Secrets.Zero(kek);
        }
    }

    public static byte[] UnwrapWithRecoveryKey(VaultHeader header, RecoveryKey recoveryKey)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(recoveryKey);
        var kek = Kdf.DeriveRecoveryKey(recoveryKey.Bytes, header.RecoverySalt);
        try
        {
            return KeyWrap.Unwrap(kek, header.WrappedDekByRecovery, VaultCodec.RecoveryWrapAad(header.RecoverySalt));
        }
        finally
        {
            Secrets.Zero(kek);
        }
    }

    /// <summary>
    /// Encrypts the payload with a fresh nonce, using the full serialized header as AAD,
    /// and returns the complete file bytes in the current format.
    /// </summary>
    public static byte[] Seal(VaultHeader header, ReadOnlySpan<byte> dek, ReadOnlySpan<byte> plaintext)
    {
        ArgumentNullException.ThrowIfNull(header);
        var headerBytes = VaultCodec.WriteHeader(header with { FormatVersion = VaultFormat.CurrentVersion });
        var payload = Aead.Encrypt(dek, plaintext, headerBytes);
        return VaultCodec.WriteFile(headerBytes, payload);
    }

    /// <summary>Decrypts the payload. The stored header bytes are the AAD.</summary>
    public static byte[] OpenPayload(VaultFile file, ReadOnlySpan<byte> dek)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Aead.Decrypt(dek, file.Payload, file.HeaderBytes.Span);
    }

    private static (AeadBox Wrapped, byte[] Salt) WrapWithPassword(ReadOnlySpan<byte> dek, ReadOnlySpan<byte> passwordUtf8, Argon2idParameters kdf)
    {
        var salt = RandomNumberGenerator.GetBytes(Kdf.SaltSize);
        var kek = Kdf.DerivePasswordKey(passwordUtf8, salt, kdf);
        try
        {
            return (KeyWrap.Wrap(kek, dek, VaultCodec.PasswordWrapAad(kdf, salt)), salt);
        }
        finally
        {
            Secrets.Zero(kek);
        }
    }

    private static (AeadBox Wrapped, byte[] Salt) WrapWithRecoveryKey(ReadOnlySpan<byte> dek, RecoveryKey recoveryKey)
    {
        var salt = RandomNumberGenerator.GetBytes(Kdf.SaltSize);
        var kek = Kdf.DeriveRecoveryKey(recoveryKey.Bytes, salt);
        try
        {
            return (KeyWrap.Wrap(kek, dek, VaultCodec.RecoveryWrapAad(salt)), salt);
        }
        finally
        {
            Secrets.Zero(kek);
        }
    }
}
