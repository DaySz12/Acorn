using System.Globalization;
using Acorn.Application.Abstractions;
using Acorn.Application.Services;
using Acorn.Application.ViewModels;
using Acorn.Core;

namespace Acorn.Application.Controllers;

/// <summary>"Restore from backup" (SPEC R2). Available from the lock screens and from Settings.</summary>
public sealed class BackupController
{
    private readonly IVaultSession _session;
    private readonly AppState _state;
    private readonly INavigator _navigator;
    private readonly VaultController _vault;
    private Screen _returnTo = Screen.Unlock;

    public BackupController(IVaultSession session, AppState state, INavigator navigator, VaultController vault)
    {
        _session = session;
        _state = state;
        _navigator = navigator;
        _vault = vault;
    }

    public void Open()
    {
        _returnTo = _state.Screen is Screen.Backups ? _returnTo : _state.Screen;
        Refresh();
        _navigator.GoTo(Screen.Backups);
    }

    public void Refresh()
    {
        try
        {
            _state.Backups = _session.ListBackups()
                .Select(b => new BackupItemViewModel(b.FileName, b.CreatedAtUtc.ToLocalTime(), FormatSize(b.SizeBytes)))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _state.Backups = [];
            _state.Notice = new Notice(Messages.StorageError, NoticeKind.Error);
        }
        _state.NotifyChanged();
    }

    public void Back()
    {
        var target = _returnTo;
        if (target is Screen.Vault or Screen.Settings && _state.Status != VaultStatus.Unlocked)
        {
            target = Screen.Unlock;
        }
        if (target is Screen.Unlock && !_session.VaultExists)
        {
            target = Screen.CreateVault;
        }
        _navigator.GoTo(target);
    }

    /// <summary>
    /// Replaces the vault with the chosen backup (the current file is backed up first) and locks.
    /// The user then unlocks with the master password that was valid for that backup.
    /// </summary>
    public async Task<Result> RestoreAsync(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || _state.Backups.All(b => b.FileName != fileName))
        {
            return Result.Fail(Messages.BackupNotFound);
        }

        var result = await BusyRunner.RunAsync(_state, async () =>
        {
            await Task.Run(() => _session.RestoreBackup(fileName)).ConfigureAwait(false);
        }, Messages.UnlockFailed).ConfigureAwait(false);

        if (result.Succeeded)
        {
            await _vault.LockAsync(LockReason.BackupRestored).ConfigureAwait(false);
        }
        return result;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0 / 1024.0:0.#} MB"),
    };
}
