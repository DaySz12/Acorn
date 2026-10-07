namespace Acorn.Core.Format;

/// <summary>Constants of the vault container format. See docs/FORMAT.md.</summary>
public static class VaultFormat
{
    public static ReadOnlySpan<byte> Magic => "ACORN"u8;

    /// <summary>Version written by this build.</summary>
    public const int CurrentVersion = 2;

    /// <summary>Oldest version this build can read and migrate.</summary>
    public const int OldestSupportedVersion = 1;

    /// <summary>Upper bound for the encrypted payload, to reject corrupted length fields early.</summary>
    public const int MaxPayloadBytes = 64 * 1024 * 1024;

    public const byte KdfArgon2id = 1;
    public const byte RecoveryKdfHkdfSha256 = 1;
}
