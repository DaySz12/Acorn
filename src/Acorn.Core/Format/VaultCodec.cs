using Acorn.Core.Crypto;

namespace Acorn.Core.Format;

/// <summary>
/// Reads and writes the binary container. Reading supports every version from
/// <see cref="VaultFormat.OldestSupportedVersion"/>; writing always produces
/// <see cref="VaultFormat.CurrentVersion"/>. Layout is documented in docs/FORMAT.md.
/// </summary>
public static class VaultCodec
{
    private const int WrappedKeySize = Aead.NonceSize + KeyWrap.DekSize + Aead.TagSize;

    private static ReadOnlySpan<byte> PasswordWrapLabel => "ACORN/wrap/password/v1"u8;
    private static ReadOnlySpan<byte> RecoveryWrapLabel => "ACORN/wrap/recovery/v1"u8;

    /// <summary>
    /// Parses and validates a vault file without any key derivation. Out-of-range KDF parameters,
    /// unknown algorithms, newer versions and malformed lengths are all rejected here.
    /// </summary>
    public static VaultFile Parse(ReadOnlySpan<byte> data)
    {
        var reader = new SpanReader(data);
        if (!reader.ReadBytes(VaultFormat.Magic.Length).SequenceEqual(VaultFormat.Magic))
        {
            throw new VaultFormatException("The file is not an Acorn vault.");
        }

        int version = reader.ReadUInt16();
        if (version > VaultFormat.CurrentVersion)
        {
            throw new UnsupportedVaultVersionException(version);
        }
        if (version < VaultFormat.OldestSupportedVersion)
        {
            throw new VaultFormatException("The vault format version is not supported.");
        }

        // v1 had no algorithm identifiers; Argon2id and HKDF-SHA256 were implied.
        if (version >= 2 && reader.ReadByte() != VaultFormat.KdfArgon2id)
        {
            throw new VaultFormatException("The vault uses an unknown key derivation algorithm.");
        }
        var kdf = new Argon2idParameters(ReadInt32(ref reader), ReadInt32(ref reader), ReadInt32(ref reader));
        kdf.EnsureWithinBounds();
        var passwordSalt = reader.ReadBytes(Kdf.SaltSize).ToArray();

        if (version >= 2 && reader.ReadByte() != VaultFormat.RecoveryKdfHkdfSha256)
        {
            throw new VaultFormatException("The vault uses an unknown recovery key derivation algorithm.");
        }
        var recoverySalt = reader.ReadBytes(Kdf.SaltSize).ToArray();

        var wrappedByPassword = ReadWrappedKey(ref reader);
        var wrappedByRecovery = ReadWrappedKey(ref reader);
        var headerBytes = data[..reader.Position].ToArray();

        var nonce = reader.ReadBytes(Aead.NonceSize).ToArray();
        var tag = reader.ReadBytes(Aead.TagSize).ToArray();
        var length = reader.ReadUInt32();
        if (length > VaultFormat.MaxPayloadBytes || length != reader.Remaining)
        {
            throw new VaultFormatException("The vault file is truncated or malformed.");
        }
        var ciphertext = reader.ReadBytes((int)length).ToArray();

        var header = new VaultHeader(version, kdf, passwordSalt, recoverySalt, wrappedByPassword, wrappedByRecovery);
        return new VaultFile(header, headerBytes, new AeadBox(nonce, ciphertext, tag));
    }

    /// <summary>Serializes a header in the current format. The result is the payload AAD.</summary>
    public static byte[] WriteHeader(VaultHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (header.FormatVersion != VaultFormat.CurrentVersion)
        {
            throw new InvalidOperationException("Only the current format version can be written.");
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(VaultFormat.Magic);
            writer.Write((ushort)VaultFormat.CurrentVersion);
            writer.Write(VaultFormat.KdfArgon2id);
            writer.Write((uint)header.PasswordKdf.MemoryKiB);
            writer.Write((uint)header.PasswordKdf.Iterations);
            writer.Write((uint)header.PasswordKdf.Parallelism);
            writer.Write(header.PasswordSalt);
            writer.Write(VaultFormat.RecoveryKdfHkdfSha256);
            writer.Write(header.RecoverySalt);
            WriteWrappedKey(writer, header.WrappedDekByPassword);
            WriteWrappedKey(writer, header.WrappedDekByRecovery);
        }
        return stream.ToArray();
    }

    /// <summary>Concatenates header bytes and the payload section.</summary>
    public static byte[] WriteFile(ReadOnlySpan<byte> headerBytes, AeadBox payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Ciphertext.Length > VaultFormat.MaxPayloadBytes)
        {
            throw new VaultFormatException("The vault is too large.");
        }

        using var stream = new MemoryStream(headerBytes.Length + Aead.NonceSize + Aead.TagSize + 4 + payload.Ciphertext.Length);
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(headerBytes);
            writer.Write(payload.Nonce);
            writer.Write(payload.Tag);
            writer.Write((uint)payload.Ciphertext.Length);
            writer.Write(payload.Ciphertext);
        }
        return stream.ToArray();
    }

    /// <summary>
    /// AAD for the password-wrapped DEK: binds the wrap to its own KDF parameters and salt.
    /// Built logically (not from raw bytes) so it is identical across format versions.
    /// </summary>
    public static byte[] PasswordWrapAad(Argon2idParameters kdf, ReadOnlySpan<byte> salt)
    {
        ArgumentNullException.ThrowIfNull(kdf);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(PasswordWrapLabel);
            writer.Write(VaultFormat.KdfArgon2id);
            writer.Write((uint)kdf.MemoryKiB);
            writer.Write((uint)kdf.Iterations);
            writer.Write((uint)kdf.Parallelism);
            writer.Write(salt);
        }
        return stream.ToArray();
    }

    /// <summary>AAD for the recovery-wrapped DEK.</summary>
    public static byte[] RecoveryWrapAad(ReadOnlySpan<byte> salt)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(RecoveryWrapLabel);
            writer.Write(VaultFormat.RecoveryKdfHkdfSha256);
            writer.Write(salt);
        }
        return stream.ToArray();
    }

    private static int ReadInt32(ref SpanReader reader)
    {
        var value = reader.ReadUInt32();
        if (value > int.MaxValue)
        {
            throw new VaultFormatException("The key derivation parameters in the vault header are outside the accepted range.");
        }
        return (int)value;
    }

    private static AeadBox ReadWrappedKey(ref SpanReader reader)
    {
        var bytes = reader.ReadBytes(WrappedKeySize);
        return new AeadBox(
            bytes[..Aead.NonceSize].ToArray(),
            bytes.Slice(Aead.NonceSize, KeyWrap.DekSize).ToArray(),
            bytes[(Aead.NonceSize + KeyWrap.DekSize)..].ToArray());
    }

    private static void WriteWrappedKey(BinaryWriter writer, AeadBox box)
    {
        if (box.Nonce.Length != Aead.NonceSize || box.Ciphertext.Length != KeyWrap.DekSize || box.Tag.Length != Aead.TagSize)
        {
            throw new InvalidOperationException("Wrapped key has an unexpected size.");
        }
        writer.Write(box.Nonce);
        writer.Write(box.Ciphertext);
        writer.Write(box.Tag);
    }
}
