namespace Acorn.Core.Models;

/// <summary>The decrypted payload. Serialized as JSON inside the AES-GCM payload only.</summary>
public sealed class VaultData
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<Entry> Entries { get; set; } = [];
    public VaultSettings Settings { get; set; } = new();

    /// <summary>
    /// False after a recovery key was issued until the user confirmed writing it down. If the app
    /// locked in between, the UI can prompt for a new key.
    /// </summary>
    public bool RecoveryKeyConfirmed { get; set; } = true;
}

/// <summary>User preferences stored inside the encrypted vault.</summary>
public sealed class VaultSettings
{
    public const int MinClipboardClearSeconds = 5;
    public const int MaxClipboardClearSeconds = 300;
    public const int MinAutoLockMinutes = 1;
    public const int MaxAutoLockMinutes = 120;
    public const int MinRevealSeconds = 5;
    public const int MaxRevealSeconds = 60;
    public const int MinBackupKeep = 5;
    public const int MaxBackupKeep = 50;

    public static IReadOnlyList<string> Themes { get; } = ["system", "light", "dark"];

    public int ClipboardClearSeconds { get; set; } = 30;
    public int AutoLockMinutes { get; set; } = 5;
    public bool LockOnMinimize { get; set; } = true;
    public bool ScreenCaptureProtection { get; set; } = true;
    public int RevealSeconds { get; set; } = 12;
    public int BackupKeep { get; set; } = 10;
    public string Theme { get; set; } = "system";
    public List<string> Environments { get; set; } = ["dev", "staging", "prod"];

    public VaultSettings Clone() => new()
    {
        ClipboardClearSeconds = ClipboardClearSeconds,
        AutoLockMinutes = AutoLockMinutes,
        LockOnMinimize = LockOnMinimize,
        ScreenCaptureProtection = ScreenCaptureProtection,
        RevealSeconds = RevealSeconds,
        BackupKeep = BackupKeep,
        Theme = Theme,
        Environments = [.. Environments],
    };

    /// <summary>Clamps every value into its allowed range (used after loading untrusted JSON).</summary>
    public void Normalize()
    {
        ClipboardClearSeconds = Math.Clamp(ClipboardClearSeconds, MinClipboardClearSeconds, MaxClipboardClearSeconds);
        AutoLockMinutes = Math.Clamp(AutoLockMinutes, MinAutoLockMinutes, MaxAutoLockMinutes);
        RevealSeconds = Math.Clamp(RevealSeconds, MinRevealSeconds, MaxRevealSeconds);
        BackupKeep = Math.Clamp(BackupKeep, MinBackupKeep, MaxBackupKeep);
        if (!Themes.Contains(Theme))
        {
            Theme = "system";
        }
        Environments = (Environments ?? [])
            .Select(e => e?.Trim().ToLowerInvariant() ?? "")
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
