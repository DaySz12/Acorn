namespace Acorn.Application.ViewModels;

/// <summary>Editable settings form plus read-only information for the Settings screen.</summary>
public sealed class SettingsViewModel
{
    public int ClipboardClearSeconds { get; set; }
    public int AutoLockMinutes { get; set; }
    public bool LockOnMinimize { get; set; }
    public bool ScreenCaptureProtection { get; set; }
    public int RevealSeconds { get; set; }
    public int BackupKeep { get; set; }
    public string Theme { get; set; } = "system";
    public string EnvironmentsText { get; set; } = "";

    public bool ScreenProtectionSupported { get; init; }
    public string KdfDescription { get; init; } = "";
    public bool KdfBelowRecommended { get; init; }
    public bool KdfIsStrongest { get; init; }
    public string StorageLocation { get; init; } = "";
    public IReadOnlyList<string> Errors { get; internal set; } = [];
}

public sealed record BackupItemViewModel(string FileName, DateTime CreatedAtLocal, string SizeText);
