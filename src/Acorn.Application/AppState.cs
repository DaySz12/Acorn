using System.Collections.Immutable;
using Acorn.Application.ViewModels;

namespace Acorn.Application;

public enum Screen
{
    Starting,
    VaultInUse,
    CreateVault,
    Unlock,
    Recover,
    RecoveryKey,
    Vault,
    Settings,
    Backups,
}

public enum VaultStatus
{
    Unknown,
    InUse,
    NoVault,
    Locked,
    Unlocked,
}

public enum NoticeKind
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record Notice(string Message, NoticeKind Kind);

/// <summary>
/// Central UI state. Views read it and subscribe to <see cref="Changed"/>; only controllers
/// (same assembly) can modify it. <see cref="Changed"/> may be raised on any thread.
/// Collections are immutable snapshots so readers never see a half-updated list.
/// </summary>
public sealed class AppState
{
    public event Action? Changed;

    public VaultStatus Status { get; internal set; } = VaultStatus.Unknown;
    public Screen Screen { get; internal set; } = Screen.Starting;
    public Notice? Notice { get; internal set; }
    public bool IsBusy { get; internal set; }
    public string Theme { get; internal set; } = "system";
    public bool KdfUpgradeSuggested { get; internal set; }

    /// <summary>The latest recovery key was never confirmed (e.g. the app locked while it was shown).</summary>
    public bool RecoveryKeyUnconfirmed { get; internal set; }

    /// <summary>Shown once after create/recover/rekey. Cleared after confirmation or on lock.</summary>
    public RecoveryKeyViewModel? PendingRecoveryKey { get; internal set; }

    // Vault screen
    public EntryFilter Filter { get; internal set; } = EntryFilter.All;
    public string SearchText { get; internal set; } = "";
    public EntryListViewModel EntryList { get; internal set; } = EntryListViewModel.Empty;
    public SidebarViewModel Sidebar { get; internal set; } = SidebarViewModel.Empty;
    public Guid? SelectedEntryId { get; internal set; }
    public EntryDetailViewModel? SelectedEntry { get; internal set; }
    public EntryEditViewModel? Editor { get; internal set; }

    private ImmutableDictionary<SecretRef, string> _revealed = ImmutableDictionary<SecretRef, string>.Empty;

    /// <summary>Secrets the user chose to reveal; each one is removed by a timer (SPEC R7).</summary>
    public ImmutableDictionary<SecretRef, string> RevealedSecrets => Volatile.Read(ref _revealed);

    /// <summary>Password generator options (not secret; kept across locks).</summary>
    public PasswordGeneratorViewModel Generator { get; } = new();

    /// <summary>Incremented by Ctrl/Cmd+K so the search box can take focus.</summary>
    public int SearchFocusRequest { get; internal set; }

    // Settings / backups screens
    public SettingsViewModel? Settings { get; internal set; }
    public IReadOnlyList<BackupItemViewModel> Backups { get; internal set; } = [];

    public bool IsRevealed(SecretRef secret) => RevealedSecrets.ContainsKey(secret);

    public string? RevealedValue(SecretRef secret) => RevealedSecrets.GetValueOrDefault(secret);

    internal void NotifyChanged() => Changed?.Invoke();

    // Reveal state is updated from timer threads and the UI thread; swaps are atomic.
    internal void RevealSecret(SecretRef secret, string value) =>
        ImmutableInterlocked.AddOrUpdate(ref _revealed, secret, value, (_, _) => value);

    internal bool HideSecret(SecretRef secret) => ImmutableInterlocked.TryRemove(ref _revealed, secret, out _);

    internal void HideAllSecrets() => Interlocked.Exchange(ref _revealed, ImmutableDictionary<SecretRef, string>.Empty);

    /// <summary>Drops everything derived from decrypted data. Theme is kept.</summary>
    internal void ResetForLock(VaultStatus status, Notice? notice)
    {
        Status = status;
        Notice = notice;
        IsBusy = false;
        KdfUpgradeSuggested = false;
        RecoveryKeyUnconfirmed = false;
        PendingRecoveryKey = null;
        Filter = EntryFilter.All;
        SearchText = "";
        EntryList = EntryListViewModel.Empty;
        Sidebar = SidebarViewModel.Empty;
        SelectedEntryId = null;
        SelectedEntry = null;
        Editor = null;
        HideAllSecrets();
        SearchFocusRequest = 0;
        Settings = null;
        Backups = [];
    }
}
