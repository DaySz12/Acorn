using System.Security.Cryptography;
using System.Text;
using Acorn.Application.Abstractions;
using Acorn.Application.Services;
using Acorn.Application.ViewModels;
using Acorn.Core;
using Acorn.Core.Crypto;
using Acorn.Core.Models;

namespace Acorn.Application.Controllers;

public enum LockReason
{
    Manual,
    Idle,
    ScreenLocked,
    SystemSuspend,
    WindowMinimized,
    SessionEnding,
    BackupRestored,
}

/// <summary>
/// Vault lifecycle while unlocked: locking (manual, idle, OS events), applying settings,
/// master-password and recovery-key changes, KDF upgrade, and the recovery-key confirmation step.
/// </summary>
public sealed class VaultController : IDisposable
{
    private readonly IVaultSession _session;
    private readonly AppState _state;
    private readonly INavigator _navigator;
    private readonly ClipboardGuard _clipboard;
    private readonly IAutoLockTimer _autoLock;
    private readonly ISessionEvents _sessionEvents;
    private readonly IScreenProtection _screenProtection;
    private readonly VaultProjection _projection;
    private readonly SemaphoreSlim _lockGate = new(1, 1);
    private volatile bool _lockOnMinimize;
    private TimeSpan _clipboardClearAfter = TimeSpan.FromSeconds(30);

    public VaultController(
        IVaultSession session,
        AppState state,
        INavigator navigator,
        ClipboardGuard clipboard,
        IAutoLockTimer autoLock,
        ISessionEvents sessionEvents,
        IScreenProtection screenProtection)
    {
        _session = session;
        _state = state;
        _navigator = navigator;
        _clipboard = clipboard;
        _autoLock = autoLock;
        _sessionEvents = sessionEvents;
        _screenProtection = screenProtection;
        _projection = new VaultProjection(session, state);

        _sessionEvents.LockRequested += OnLockRequested;
        _autoLock.Expired += OnAutoLockExpired;
    }

    /// <summary>Raised after the vault was locked (any reason) so other controllers can drop timers.</summary>
    internal event Action? Locked;

    /// <summary>How long copied secrets stay on the clipboard (from settings once unlocked).</summary>
    internal TimeSpan ClipboardClearAfter => _clipboardClearAfter;

    /// <summary>
    /// Locks in the order required by SPEC R6: clear clipboard → zero key → clear UI state → Unlock screen.
    /// </summary>
    public async Task LockAsync(LockReason reason = LockReason.Manual)
    {
        await _lockGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await ClearClipboardSafeAsync().ConfigureAwait(false);
            // A restored backup always ends on the Unlock screen, even if the vault was locked before.
            if (!_session.IsUnlocked && _state.Status != VaultStatus.Unlocked && reason != LockReason.BackupRestored)
            {
                return;
            }

            // Locking while a new recovery key is on screen means the user may not have written it down.
            var notice = _state.PendingRecoveryKey is not null
                ? new Notice(Messages.RecoveryKeyLostOnLock, NoticeKind.Warning)
                : NoticeFor(reason);

            _session.Lock();
            _autoLock.Stop();
            _state.ResetForLock(_session.VaultExists ? VaultStatus.Locked : VaultStatus.NoVault, notice);
            _screenProtection.SetEnabled(true);
            _navigator.GoTo(Screen.Unlock);
            _state.NotifyChanged();
            Locked?.Invoke();
        }
        finally
        {
            _lockGate.Release();
        }
    }

    /// <summary>Window closing / process exit: clear clipboard and zero keys. No navigation.</summary>
    public async Task ShutdownAsync()
    {
        await ClearClipboardSafeAsync().ConfigureAwait(false);
        _session.Lock();
        _autoLock.Stop();
        _state.HideAllSecrets();
        Locked?.Invoke();
    }

    /// <summary>Called by the view shell on key presses/clicks to postpone auto-lock.</summary>
    public void NotifyActivity()
    {
        if (_state.Status == VaultStatus.Unlocked)
        {
            _autoLock.RegisterActivity();
        }
    }

    public void DismissNotice()
    {
        _state.Notice = null;
        _state.NotifyChanged();
    }

    public void DismissKdfSuggestion()
    {
        _state.KdfUpgradeSuggested = false;
        _state.NotifyChanged();
    }

    public async Task<Result> ChangePasswordAsync(string currentPassword, string newPassword, string confirmation)
    {
        if (string.IsNullOrEmpty(currentPassword))
        {
            return Result.Fail(Messages.PasswordRequired);
        }
        if (PasswordRules.ValidateNew(newPassword, confirmation) is { } error)
        {
            return Result.Fail(error);
        }
        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return Result.Fail(Messages.PasswordSameAsCurrent);
        }

        return await BusyRunner.RunAsync(_state, async () =>
        {
            using var recovery = await Task.Run(() => _session.ChangePassword(currentPassword, newPassword)).ConfigureAwait(false);
            ShowRecoveryKey(recovery, Screen.Settings);
            _state.Notice = new Notice(Messages.PasswordChanged, NoticeKind.Success);
            _state.KdfUpgradeSuggested = false;
            _navigator.GoTo(Screen.RecoveryKey);
        }, Messages.CurrentPasswordWrong).ConfigureAwait(false);
    }

    public async Task<Result> RegenerateRecoveryKeyAsync(string currentPassword)
    {
        if (string.IsNullOrEmpty(currentPassword))
        {
            return Result.Fail(Messages.PasswordRequired);
        }

        return await BusyRunner.RunAsync(_state, async () =>
        {
            using var recovery = await Task.Run(() => _session.RegenerateRecoveryKey(currentPassword)).ConfigureAwait(false);
            ShowRecoveryKey(recovery, Screen.Settings);
            _state.Notice = new Notice(Messages.RecoveryKeyRegenerated, NoticeKind.Success);
            _navigator.GoTo(Screen.RecoveryKey);
        }, Messages.CurrentPasswordWrong).ConfigureAwait(false);
    }

    public async Task<Result> UpgradeKdfAsync(string currentPassword, KdfPreset preset)
    {
        if (string.IsNullOrEmpty(currentPassword))
        {
            return Result.Fail(Messages.PasswordRequired);
        }
        var parameters = preset == KdfPreset.Strong ? Argon2idParameters.Strong : Argon2idParameters.Recommended;

        return await BusyRunner.RunAsync(_state, async () =>
        {
            await Task.Run(() => _session.UpgradeKdf(currentPassword, parameters)).ConfigureAwait(false);
            _state.KdfUpgradeSuggested = false;
            _state.Notice = new Notice(Messages.KdfUpgraded, NoticeKind.Success);
        }, Messages.CurrentPasswordWrong).ConfigureAwait(false);
    }

    /// <summary>Checks the groups the user typed back against the displayed recovery key.</summary>
    public Result ConfirmRecoveryKeySaved(IReadOnlyList<string> typedGroups)
    {
        ArgumentNullException.ThrowIfNull(typedGroups);
        var pending = _state.PendingRecoveryKey;
        if (pending is null)
        {
            return Result.Fail(Messages.NoPendingRecoveryKey);
        }

        var allMatch = typedGroups.Count == pending.ConfirmGroupIndexes.Count;
        for (var i = 0; i < pending.ConfirmGroupIndexes.Count && i < typedGroups.Count; i++)
        {
            var expected = Encoding.UTF8.GetBytes(pending.Groups[pending.ConfirmGroupIndexes[i]]);
            var typed = Encoding.UTF8.GetBytes(RecoveryKey.NormalizeInput(typedGroups[i] ?? ""));
            allMatch &= CryptographicOperations.FixedTimeEquals(expected, typed);
        }
        if (!allMatch)
        {
            return Result.Fail(Messages.RecoveryConfirmMismatch);
        }

        // Persist the confirmation so an interrupted confirmation can be detected on a later unlock.
        try
        {
            var data = _session.Data;
            if (!data.RecoveryKeyConfirmed)
            {
                data.RecoveryKeyConfirmed = true;
                try
                {
                    _session.Save();
                }
                catch
                {
                    data.RecoveryKeyConfirmed = false;
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            return Result.Fail(ErrorMapper.ToMessage(ex));
        }

        _state.PendingRecoveryKey = null;
        _state.RecoveryKeyUnconfirmed = false;
        _navigator.GoTo(pending.ReturnTo);
        return Result.Ok();
    }

    /// <summary>Copies the displayed recovery key; it is cleared from the clipboard like any secret.</summary>
    public async Task<Result> CopyRecoveryKeyAsync()
    {
        var pending = _state.PendingRecoveryKey;
        if (pending is null)
        {
            return Result.Fail(Messages.NoPendingRecoveryKey);
        }
        await _clipboard.CopySecretAsync(pending.Formatted, _clipboardClearAfter).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>Called after a successful create/unlock/recover: applies settings and shows the vault.</summary>
    internal void EnterUnlocked(UnlockResult result, Notice? notice, bool navigate = true)
    {
        var data = _session.Data;
        ApplySettings(data.Settings);
        _state.Status = VaultStatus.Unlocked;
        _state.KdfUpgradeSuggested = result.KdfBelowRecommended;
        // While a freshly issued key is on screen the confirmation step handles it; otherwise prompt.
        _state.RecoveryKeyUnconfirmed = !data.RecoveryKeyConfirmed && _state.PendingRecoveryKey is null;
        _state.Notice = notice;
        _state.Filter = EntryFilter.All;
        _state.SearchText = "";
        _state.SelectedEntryId = null;
        _projection.Refresh();
        if (navigate)
        {
            _navigator.GoTo(Screen.Vault);
        }
        _state.NotifyChanged();
    }

    /// <summary>Applies settings that affect app behaviour (theme, screen protection, timers).</summary>
    internal void ApplySettings(VaultSettings settings)
    {
        _state.Theme = settings.Theme;
        _lockOnMinimize = settings.LockOnMinimize;
        _clipboardClearAfter = TimeSpan.FromSeconds(settings.ClipboardClearSeconds);
        _screenProtection.SetEnabled(settings.ScreenCaptureProtection);
        _autoLock.Start(TimeSpan.FromMinutes(settings.AutoLockMinutes));
    }

    /// <summary>Stores a recovery key for one-time display with two random groups to confirm.</summary>
    internal void ShowRecoveryKey(RecoveryKey recoveryKey, Screen returnTo)
    {
        var formatted = recoveryKey.ToDisplayString();
        var groups = formatted.Split('-');
        var first = RandomNumberGenerator.GetInt32(groups.Length);
        int second;
        do
        {
            second = RandomNumberGenerator.GetInt32(groups.Length);
        }
        while (second == first);

        _state.PendingRecoveryKey = new RecoveryKeyViewModel(formatted, groups, [Math.Min(first, second), Math.Max(first, second)], returnTo);
    }

    public void Dispose()
    {
        _sessionEvents.LockRequested -= OnLockRequested;
        _autoLock.Expired -= OnAutoLockExpired;
        _lockGate.Dispose();
    }

    private void OnLockRequested(object? sender, SessionLockReason reason)
    {
        if (reason == SessionLockReason.WindowMinimized && !_lockOnMinimize)
        {
            return;
        }
        _ = LockSafeAsync(reason switch
        {
            SessionLockReason.ScreenLocked => LockReason.ScreenLocked,
            SessionLockReason.SystemSuspend => LockReason.SystemSuspend,
            SessionLockReason.SessionEnding => LockReason.SessionEnding,
            _ => LockReason.WindowMinimized,
        });
    }

    private void OnAutoLockExpired(object? sender, EventArgs e) => _ = LockSafeAsync(LockReason.Idle);

    private async Task LockSafeAsync(LockReason reason)
    {
        try
        {
            await LockAsync(reason).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Last resort for event-driven locks: make sure the key is gone even if UI updates failed.
            _session.Lock();
        }
    }

    private async Task ClearClipboardSafeAsync()
    {
        try
        {
            await _clipboard.ClearIfOwnedAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A clipboard failure must never prevent the key from being zeroed.
        }
    }

    private static Notice NoticeFor(LockReason reason) => reason switch
    {
        LockReason.Idle => new Notice(Messages.LockedIdle, NoticeKind.Info),
        LockReason.ScreenLocked => new Notice(Messages.LockedScreen, NoticeKind.Info),
        LockReason.SystemSuspend => new Notice(Messages.LockedSuspend, NoticeKind.Info),
        LockReason.WindowMinimized => new Notice(Messages.LockedMinimized, NoticeKind.Info),
        LockReason.SessionEnding => new Notice(Messages.LockedSessionEnding, NoticeKind.Info),
        LockReason.BackupRestored => new Notice(Messages.LockedRestored, NoticeKind.Success),
        _ => new Notice(Messages.LockedManual, NoticeKind.Info),
    };
}
