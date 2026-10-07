using System.Text.RegularExpressions;
using Acorn.Application.Abstractions;
using Acorn.Application.Services;
using Acorn.Application.ViewModels;
using Acorn.Core;
using Acorn.Core.Crypto;
using Acorn.Core.Models;

namespace Acorn.Application.Controllers;

/// <summary>Settings screen: preferences stored inside the encrypted vault, and theme switching.</summary>
public sealed partial class SettingsController
{
    private const int MaxEnvironments = 12;

    private readonly IVaultSession _session;
    private readonly AppState _state;
    private readonly INavigator _navigator;
    private readonly VaultController _vault;
    private readonly IScreenProtection _screenProtection;
    private readonly VaultProjection _projection;

    public SettingsController(IVaultSession session, AppState state, INavigator navigator, VaultController vault, IScreenProtection screenProtection)
    {
        _session = session;
        _state = state;
        _navigator = navigator;
        _vault = vault;
        _screenProtection = screenProtection;
        _projection = new VaultProjection(session, state);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,23}$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentName();

    public void Open()
    {
        if (_state.Status != VaultStatus.Unlocked)
        {
            return;
        }
        _state.Settings = Build();
        _navigator.GoTo(Screen.Settings);
    }

    public void Close()
    {
        _state.Settings = null;
        _navigator.GoTo(_state.Status == VaultStatus.Unlocked ? Screen.Vault : Screen.Unlock);
    }

    /// <summary>Validates and saves the form in <see cref="AppState.Settings"/>, then applies it.</summary>
    public Task<Result> SaveAsync()
    {
        var form = _state.Settings;
        if (form is null || !TryGetData(out var data))
        {
            return Task.FromResult(Result.Fail(Messages.VaultLocked));
        }

        var errors = Validate(form, out var environments);
        form.Errors = errors;
        if (errors.Count > 0)
        {
            _state.NotifyChanged();
            return Task.FromResult(Result.Fail(errors[0]));
        }

        var previous = data.Settings;
        var updated = previous.Clone();
        updated.ClipboardClearSeconds = form.ClipboardClearSeconds;
        updated.AutoLockMinutes = form.AutoLockMinutes;
        updated.LockOnMinimize = form.LockOnMinimize;
        updated.ScreenCaptureProtection = form.ScreenCaptureProtection;
        updated.RevealSeconds = form.RevealSeconds;
        updated.BackupKeep = form.BackupKeep;
        updated.Theme = form.Theme;
        updated.Environments = environments;

        var result = Persist(data, updated, previous);
        if (result.Succeeded)
        {
            _state.Settings = Build();
            _state.Notice = new Notice(Messages.SettingsSaved, NoticeKind.Success);
        }
        _state.NotifyChanged();
        return Task.FromResult(result);
    }

    /// <summary>Theme switch from the sidebar; saved immediately.</summary>
    public Task<Result> SetThemeAsync(string theme)
    {
        if (!VaultSettings.Themes.Contains(theme))
        {
            return Task.FromResult(Result.Fail(Messages.SettingsInvalid));
        }
        if (!TryGetData(out var data))
        {
            return Task.FromResult(Result.Fail(Messages.VaultLocked));
        }

        var previous = data.Settings;
        var updated = previous.Clone();
        updated.Theme = theme;
        var result = Persist(data, updated, previous);
        if (result.Succeeded && _state.Settings is { } form)
        {
            form.Theme = theme;
        }
        _state.NotifyChanged();
        return Task.FromResult(result);
    }

    private Result Persist(VaultData data, VaultSettings updated, VaultSettings previous)
    {
        data.Settings = updated;
        try
        {
            _session.Save();
        }
        catch (Exception ex)
        {
            data.Settings = previous;
            return Result.Fail(ErrorMapper.ToMessage(ex));
        }
        _vault.ApplySettings(updated);
        _projection.Refresh();
        return Result.Ok();
    }

    private SettingsViewModel Build()
    {
        var settings = _session.Data.Settings;
        var kdf = _session.CurrentKdf;
        return new SettingsViewModel
        {
            ClipboardClearSeconds = settings.ClipboardClearSeconds,
            AutoLockMinutes = settings.AutoLockMinutes,
            LockOnMinimize = settings.LockOnMinimize,
            ScreenCaptureProtection = settings.ScreenCaptureProtection,
            RevealSeconds = settings.RevealSeconds,
            BackupKeep = settings.BackupKeep,
            Theme = settings.Theme,
            EnvironmentsText = string.Join(", ", settings.Environments),
            ScreenProtectionSupported = _screenProtection.IsSupported,
            KdfDescription = $"Argon2id · {kdf.MemoryKiB / 1024} MiB · {kdf.Iterations} รอบ · {kdf.Parallelism} lanes",
            KdfBelowRecommended = kdf.IsWeakerThan(Argon2idParameters.Recommended),
            KdfIsStrongest = !kdf.IsWeakerThan(Argon2idParameters.Strong),
            StorageLocation = _session.StorageLocation,
        };
    }

    private static List<string> Validate(SettingsViewModel form, out List<string> environments)
    {
        var errors = new List<string>();
        if (form.ClipboardClearSeconds is < VaultSettings.MinClipboardClearSeconds or > VaultSettings.MaxClipboardClearSeconds)
        {
            errors.Add(string.Format(Messages.RangeError, "ล้าง clipboard", VaultSettings.MinClipboardClearSeconds, VaultSettings.MaxClipboardClearSeconds, "วินาที"));
        }
        if (form.AutoLockMinutes is < VaultSettings.MinAutoLockMinutes or > VaultSettings.MaxAutoLockMinutes)
        {
            errors.Add(string.Format(Messages.RangeError, "ล็อกอัตโนมัติ", VaultSettings.MinAutoLockMinutes, VaultSettings.MaxAutoLockMinutes, "นาที"));
        }
        if (form.RevealSeconds is < VaultSettings.MinRevealSeconds or > VaultSettings.MaxRevealSeconds)
        {
            errors.Add(string.Format(Messages.RangeError, "แสดงรหัสผ่าน", VaultSettings.MinRevealSeconds, VaultSettings.MaxRevealSeconds, "วินาที"));
        }
        if (form.BackupKeep is < VaultSettings.MinBackupKeep or > VaultSettings.MaxBackupKeep)
        {
            errors.Add(string.Format(Messages.RangeError, "จำนวน backup", VaultSettings.MinBackupKeep, VaultSettings.MaxBackupKeep, "ไฟล์"));
        }
        if (!VaultSettings.Themes.Contains(form.Theme))
        {
            errors.Add(Messages.SettingsInvalid);
        }

        environments = (form.EnvironmentsText ?? "")
            .Split([',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (environments.Count > MaxEnvironments || environments.Any(e => !EnvironmentName().IsMatch(e)))
        {
            errors.Add(Messages.EnvironmentsInvalid);
        }
        return errors;
    }

    private bool TryGetData(out VaultData data)
    {
        try
        {
            data = _session.Data;
            return true;
        }
        catch (VaultLockedException)
        {
            data = null!;
            return false;
        }
    }
}
