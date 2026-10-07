using Acorn.Application.Abstractions;
using Acorn.Application.Services;
using Acorn.Application.ViewModels;
using Acorn.Core;

namespace Acorn.Application.Controllers;

/// <summary>Start-up, vault creation, unlock with password and recovery with the recovery key.</summary>
public sealed class UnlockController
{
    private readonly IVaultSession _session;
    private readonly AppState _state;
    private readonly INavigator _navigator;
    private readonly VaultController _vault;

    public UnlockController(IVaultSession session, AppState state, INavigator navigator, VaultController vault)
    {
        _session = session;
        _state = state;
        _navigator = navigator;
        _vault = vault;
    }

    /// <summary>
    /// Acquires the process lock (SPEC R4), quarantines a temp file left by an interrupted save (R3)
    /// and picks the first screen. Safe to call again from the "vault in use" screen.
    /// </summary>
    public void Initialize()
    {
        if (!_session.TryAcquireProcessLock())
        {
            _state.Status = VaultStatus.InUse;
            _state.Notice = new Notice(Messages.VaultInUse, NoticeKind.Error);
            _navigator.GoTo(Screen.VaultInUse);
            return;
        }

        Notice? notice = null;
        try
        {
            if (_session.RecoverInterruptedSave() is { } leftover)
            {
                notice = new Notice(Messages.LeftoverTemp(leftover), NoticeKind.Warning);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notice = new Notice(Messages.StorageError, NoticeKind.Warning);
        }

        var exists = _session.VaultExists;
        _state.Status = exists ? VaultStatus.Locked : VaultStatus.NoVault;
        _state.Notice = notice;
        _navigator.GoTo(exists ? Screen.Unlock : Screen.CreateVault);
    }

    /// <summary>Strength meter for a password being typed. Pure; holds nothing.</summary>
    public PasswordStrengthViewModel EvaluatePassword(string? password) => PasswordRules.Describe(password);

    public async Task<Result> CreateVaultAsync(string password, string confirmation)
    {
        if (PasswordRules.ValidateNew(password, confirmation) is { } error)
        {
            return Result.Fail(error);
        }
        if (_session.VaultExists)
        {
            return Result.Fail(Messages.VaultAlreadyExists);
        }

        return await BusyRunner.RunAsync(_state, async () =>
        {
            using var recovery = await Task.Run(() => _session.Create(password)).ConfigureAwait(false);
            _vault.ShowRecoveryKey(recovery, Screen.Vault);
            _vault.EnterUnlocked(new UnlockResult(Migrated: false, KdfBelowRecommended: false), notice: null, navigate: false);
            _navigator.GoTo(Screen.RecoveryKey);
        }, Messages.UnlockFailed, ResetStatusAfterFailure).ConfigureAwait(false);
    }

    public async Task<Result> UnlockWithPasswordAsync(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return Result.Fail(Messages.PasswordRequired);
        }

        return await BusyRunner.RunAsync(_state, async () =>
        {
            var result = await Task.Run(() => _session.UnlockWithPassword(password)).ConfigureAwait(false);
            _vault.EnterUnlocked(result, result.Migrated ? new Notice(Messages.Migrated, NoticeKind.Success) : null);
        }, Messages.UnlockFailed, ResetStatusAfterFailure).ConfigureAwait(false);
    }

    public async Task<Result> RecoverAsync(string recoveryKey, string newPassword, string confirmation)
    {
        if (string.IsNullOrWhiteSpace(recoveryKey))
        {
            return Result.Fail(Messages.RecoveryKeyRequired);
        }
        if (PasswordRules.ValidateNew(newPassword, confirmation) is { } error)
        {
            return Result.Fail(error);
        }

        return await BusyRunner.RunAsync(_state, async () =>
        {
            using var replacement = await Task.Run(() => _session.RecoverWithKey(recoveryKey, newPassword)).ConfigureAwait(false);
            _vault.ShowRecoveryKey(replacement, Screen.Vault);
            _vault.EnterUnlocked(new UnlockResult(Migrated: false, KdfBelowRecommended: false),
                new Notice(Messages.Recovered, NoticeKind.Success), navigate: false);
            _navigator.GoTo(Screen.RecoveryKey);
        }, Messages.RecoveryFailed, ResetStatusAfterFailure).ConfigureAwait(false);
    }

    public void ShowUnlock()
    {
        _state.Notice = null;
        _navigator.GoTo(_session.VaultExists ? Screen.Unlock : Screen.CreateVault);
    }

    public void ShowRecover()
    {
        _state.Notice = null;
        _navigator.GoTo(Screen.Recover);
    }

    private void ResetStatusAfterFailure()
    {
        // The session has already dropped any partial state.
        if (!_session.IsUnlocked)
        {
            _state.Status = _session.VaultExists ? VaultStatus.Locked : VaultStatus.NoVault;
        }
    }
}
