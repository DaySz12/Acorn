using System.Globalization;
using Acorn.Application.Abstractions;
using Acorn.Application.Services;
using Acorn.Application.ViewModels;
using Acorn.Core;
using Acorn.Core.Models;
using Acorn.Core.Security;

namespace Acorn.Application.Controllers;

/// <summary>
/// Entries: browse (filter/search/select), create/edit/delete, favourites, copy to the clipboard
/// (secrets never pass through the view) and timed reveal of secret values (SPEC R5, R7).
/// </summary>
public sealed class EntryController : IDisposable
{
    private const int MaxTags = 20;

    private readonly IVaultSession _session;
    private readonly AppState _state;
    private readonly INavigator _navigator;
    private readonly ClipboardGuard _clipboard;
    private readonly VaultController _vault;
    private readonly TimeProvider _time;
    private readonly VaultProjection _projection;
    private readonly object _timerGate = new();
    private readonly Dictionary<SecretRef, ITimer> _revealTimers = [];

    public EntryController(
        IVaultSession session,
        AppState state,
        INavigator navigator,
        ClipboardGuard clipboard,
        VaultController vault,
        TimeProvider time)
    {
        _session = session;
        _state = state;
        _navigator = navigator;
        _clipboard = clipboard;
        _vault = vault;
        _time = time;
        _projection = new VaultProjection(session, state);
        _vault.Locked += HideAllRevealed;
    }

    // ---------- Browsing ----------

    public void SetFilter(EntryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _state.Filter = filter;
        Refresh();
    }

    public void SetSearch(string? text)
    {
        _state.SearchText = text ?? "";
        Refresh();
    }

    /// <summary>Ctrl/Cmd+K: go to the vault screen and focus the search box.</summary>
    public void RequestQuickSearch()
    {
        if (_state.Status != VaultStatus.Unlocked || _state.Screen == Screen.RecoveryKey)
        {
            return;
        }
        if (_state.Screen != Screen.Vault)
        {
            _navigator.GoTo(Screen.Vault);
        }
        _state.SearchFocusRequest++;
        _state.NotifyChanged();
    }

    public void Select(Guid id)
    {
        if (_state.SelectedEntryId == id && _state.Editor is null)
        {
            return;
        }
        HideAllRevealed();
        _state.Editor = null;
        _state.SelectedEntryId = id;
        Refresh();
    }

    // ---------- Editing ----------

    public void BeginCreate(string? type = null)
    {
        if (!TryGetData(out var data))
        {
            return;
        }
        HideAllRevealed();
        var filter = _state.Filter;
        _state.Editor = new EntryEditViewModel
        {
            Type = EntryTypes.IsValid(type) ? type! : EntryTypes.Server,
            Env = filter.Kind == EntryFilterKind.Environment ? filter.Value ?? "" : "",
            TagsText = filter.Kind == EntryFilterKind.Tag ? filter.Value ?? "" : "",
            Favorite = filter.Kind == EntryFilterKind.Favorites,
            AvailableEnvironments = [.. data.Settings.Environments],
            AvailableTypes = EntryTypes.All,
        };
        _state.NotifyChanged();
    }

    /// <summary>Opens the editor. Stored secrets are not copied into the form.</summary>
    public void BeginEdit(Guid id)
    {
        if (!TryGetData(out var data) || data.Entries.FirstOrDefault(e => e.Id == id) is not { } entry)
        {
            return;
        }
        HideAllRevealed();
        _state.Editor = new EntryEditViewModel
        {
            Id = entry.Id,
            Type = entry.Type,
            Name = entry.Name,
            Env = entry.Env,
            Host = entry.Host,
            PortText = entry.Port?.ToString(CultureInfo.InvariantCulture) ?? "",
            Username = entry.Username,
            HasExistingPassword = entry.Password.Length > 0,
            SshKeyPath = entry.SshKeyPath,
            TagsText = string.Join(", ", entry.Tags),
            Notes = entry.Notes,
            Favorite = entry.Favorite,
            CustomFields = entry.CustomFields.Select(f => new CustomFieldEditViewModel
            {
                Id = f.Id,
                Label = f.Label,
                IsSecret = f.IsSecret,
                Value = f.IsSecret ? "" : f.Value,
                HasExistingSecret = f.IsSecret && f.Value.Length > 0,
            }).ToList(),
            AvailableEnvironments = [.. data.Settings.Environments],
            AvailableTypes = EntryTypes.All,
        };
        _state.NotifyChanged();
    }

    public void CancelEdit()
    {
        _state.Editor = null;
        _state.NotifyChanged();
    }

    public void AddCustomField(bool isSecret = false)
    {
        _state.Editor?.CustomFields.Add(new CustomFieldEditViewModel { IsSecret = isSecret });
        _state.NotifyChanged();
    }

    public void RemoveCustomField(Guid fieldId)
    {
        _state.Editor?.CustomFields.RemoveAll(f => f.Id == fieldId);
        _state.NotifyChanged();
    }

    /// <summary>Fills the editor's new-password field from the generator options in <see cref="AppState.Generator"/>.</summary>
    public Result GeneratePasswordForEditor()
    {
        var editor = _state.Editor;
        if (editor is null)
        {
            return Result.Fail(Messages.NothingToEdit);
        }

        var options = _state.Generator;
        try
        {
            editor.NewPassword = PasswordGenerator.Generate(new PasswordGeneratorOptions(
                Math.Clamp(options.Length, PasswordGeneratorViewModel.MinLength, PasswordGeneratorViewModel.MaxLength),
                options.Lowercase, options.Uppercase, options.Digits, options.Symbols, options.ExcludeAmbiguous));
            editor.RemovePassword = false;
        }
        catch (ArgumentException)
        {
            return Result.Fail(Messages.GeneratorNeedsCharacterSet);
        }
        _state.NotifyChanged();
        return Result.Ok();
    }

    /// <summary>Validates the editor and writes the entry. On a write failure the in-memory change is rolled back.</summary>
    public Task<Result> SaveAsync()
    {
        var editor = _state.Editor;
        if (editor is null)
        {
            return Task.FromResult(Result.Fail(Messages.NothingToEdit));
        }
        if (!TryGetData(out var data))
        {
            return Task.FromResult(Result.Fail(Messages.VaultLocked));
        }

        var errors = Validate(editor, out var port);
        editor.Errors = errors;
        if (errors.Count > 0)
        {
            _state.NotifyChanged();
            return Task.FromResult(Result.Fail(errors[0]));
        }

        var entries = data.Entries;
        var now = _time.GetUtcNow().UtcDateTime;
        Entry? original = null;
        var index = -1;
        Entry target;
        if (editor.Id is { } id)
        {
            index = entries.FindIndex(e => e.Id == id);
            if (index < 0)
            {
                return Task.FromResult(Result.Fail(Messages.EntryNotFound));
            }
            original = entries[index];
            target = original.Clone();
        }
        else
        {
            target = new Entry { CreatedAt = now };
        }

        Apply(editor, target, port, now);
        if (index >= 0)
        {
            entries[index] = target;
        }
        else
        {
            entries.Add(target);
        }

        var result = SaveOrRollback(() =>
        {
            if (index >= 0)
            {
                entries[index] = original!;
            }
            else
            {
                entries.Remove(target);
            }
        });
        if (result.Succeeded)
        {
            _state.Editor = null;
            _state.SelectedEntryId = target.Id;
        }
        Refresh();
        return Task.FromResult(result);
    }

    public Task<Result> DeleteAsync(Guid id)
    {
        if (!TryGetData(out var data))
        {
            return Task.FromResult(Result.Fail(Messages.VaultLocked));
        }
        var entries = data.Entries;
        var index = entries.FindIndex(e => e.Id == id);
        if (index < 0)
        {
            return Task.FromResult(Result.Fail(Messages.EntryNotFound));
        }

        var removed = entries[index];
        entries.RemoveAt(index);
        var result = SaveOrRollback(() => entries.Insert(index, removed));
        if (result.Succeeded)
        {
            HideAllRevealed();
            if (_state.SelectedEntryId == id)
            {
                _state.SelectedEntryId = null;
            }
            if (_state.Editor?.Id == id)
            {
                _state.Editor = null;
            }
        }
        Refresh();
        return Task.FromResult(result);
    }

    public Task<Result> ToggleFavoriteAsync(Guid id)
    {
        if (!TryGetData(out var data) || data.Entries.FirstOrDefault(e => e.Id == id) is not { } entry)
        {
            return Task.FromResult(Result.Fail(Messages.EntryNotFound));
        }

        entry.Favorite = !entry.Favorite;
        var result = SaveOrRollback(() => entry.Favorite = !entry.Favorite);
        Refresh();
        return Task.FromResult(result);
    }

    // ---------- Clipboard and reveal ----------

    /// <summary>
    /// Copies a field to the clipboard. Secrets (password, secret custom fields) get the auto-clear
    /// timer. The value is never returned to the caller.
    /// </summary>
    public async Task<Result> CopyAsync(Guid id, CopyField field, Guid? customFieldId = null)
    {
        if (!TryGetData(out var data) || data.Entries.FirstOrDefault(e => e.Id == id) is not { } entry)
        {
            return Result.Fail(Messages.EntryNotFound);
        }

        string? value;
        bool secret;
        switch (field)
        {
            case CopyField.Host:
                (value, secret) = (entry.Host, false);
                break;
            case CopyField.Username:
                (value, secret) = (entry.Username, false);
                break;
            case CopyField.Password:
                (value, secret) = (entry.Password, true);
                break;
            case CopyField.SshCommand:
                (value, secret) = (SshCommand.Build(entry), false);
                break;
            default:
                var custom = entry.CustomFields.FirstOrDefault(f => f.Id == customFieldId);
                (value, secret) = (custom?.Value, custom?.IsSecret ?? false);
                break;
        }
        if (string.IsNullOrEmpty(value))
        {
            return Result.Fail(Messages.NothingToCopy);
        }

        try
        {
            if (secret)
            {
                await _clipboard.CopySecretAsync(value, _vault.ClipboardClearAfter).ConfigureAwait(false);
            }
            else
            {
                await _clipboard.CopyTextAsync(value).ConfigureAwait(false);
            }
            return Result.Ok();
        }
        catch (Exception)
        {
            return Result.Fail(Messages.CopyFailed);
        }
    }

    public Task<Result> CopyPasswordAsync(Guid id) => CopyAsync(id, CopyField.Password);

    public Task<Result> CopySecretAsync(SecretRef secret) =>
        CopyAsync(secret.EntryId, secret.CustomFieldId is null ? CopyField.Password : CopyField.CustomField, secret.CustomFieldId);

    /// <summary>
    /// Puts one secret into <see cref="AppState.RevealedSecrets"/> for the configured number of
    /// seconds. Only works while unlocked.
    /// </summary>
    public Task<Result<string>> RevealSecretAsync(SecretRef secret)
    {
        if (_state.Status != VaultStatus.Unlocked || !TryGetData(out var data))
        {
            return Task.FromResult(Result<string>.Fail(Messages.VaultLocked));
        }

        var entry = data.Entries.FirstOrDefault(e => e.Id == secret.EntryId);
        var value = secret.CustomFieldId is { } fieldId
            ? entry?.CustomFields.FirstOrDefault(f => f.Id == fieldId && f.IsSecret)?.Value
            : entry?.Password;
        if (string.IsNullOrEmpty(value))
        {
            return Task.FromResult(Result<string>.Fail(Messages.NothingToReveal));
        }

        _state.RevealSecret(secret, value);
        var hideAfter = TimeSpan.FromSeconds(data.Settings.RevealSeconds);
        lock (_timerGate)
        {
            if (_revealTimers.Remove(secret, out var previous))
            {
                previous.Dispose();
            }
            _revealTimers[secret] = _time.CreateTimer(_ => HideSecret(secret), null, hideAfter, Timeout.InfiniteTimeSpan);
        }
        _state.NotifyChanged();
        return Task.FromResult(Result<string>.Ok(value));
    }

    public void HideSecret(SecretRef secret)
    {
        lock (_timerGate)
        {
            if (_revealTimers.Remove(secret, out var timer))
            {
                timer.Dispose();
            }
        }
        if (_state.HideSecret(secret))
        {
            _state.NotifyChanged();
        }
    }

    public void Dispose()
    {
        _vault.Locked -= HideAllRevealed;
        HideAllRevealed();
    }

    // ---------- Helpers ----------

    private void HideAllRevealed()
    {
        lock (_timerGate)
        {
            foreach (var timer in _revealTimers.Values)
            {
                timer.Dispose();
            }
            _revealTimers.Clear();
        }
        _state.HideAllSecrets();
    }

    private void Refresh()
    {
        _projection.Refresh();
        _state.NotifyChanged();
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

    private Result SaveOrRollback(Action rollback)
    {
        try
        {
            _session.Save();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            try
            {
                rollback();
            }
            catch (Exception)
            {
                // The vault may have been locked meanwhile; nothing left to roll back.
            }
            return Result.Fail(ErrorMapper.ToMessage(ex));
        }
    }

    private static List<string> Validate(EntryEditViewModel editor, out int? port)
    {
        var errors = new List<string>();
        port = null;
        if (!EntryTypes.IsValid(editor.Type))
        {
            errors.Add(Messages.EntryTypeInvalid);
        }
        if (string.IsNullOrWhiteSpace(editor.Name))
        {
            errors.Add(Messages.EntryNameRequired);
        }
        if (!string.IsNullOrWhiteSpace(editor.PortText))
        {
            if (int.TryParse(editor.PortText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 1 and <= 65535)
            {
                port = parsed;
            }
            else
            {
                errors.Add(Messages.PortInvalid);
            }
        }
        if (editor.CustomFields.Any(f => f.Label.Trim().Length == 0 && (f.Value.Length > 0 || f.HasExistingSecret)))
        {
            errors.Add(Messages.CustomFieldLabelRequired);
        }
        return errors;
    }

    private static void Apply(EntryEditViewModel editor, Entry entry, int? port, DateTime now)
    {
        var previousFields = entry.CustomFields;
        entry.Type = editor.Type;
        entry.Name = editor.Name.Trim();
        entry.Env = editor.Env.Trim().ToLowerInvariant();
        entry.Host = editor.Host.Trim();
        entry.Port = port;
        entry.Username = editor.Username.Trim();
        if (editor.RemovePassword)
        {
            entry.Password = "";
        }
        else if (editor.NewPassword.Length > 0)
        {
            entry.Password = editor.NewPassword;
        }
        entry.SshKeyPath = editor.SshKeyPath.Trim();
        entry.Tags = ParseTags(editor.TagsText);
        entry.Notes = editor.Notes;
        entry.Favorite = editor.Favorite;
        entry.CustomFields = editor.CustomFields
            .Where(f => f.Label.Trim().Length > 0)
            .Select(f => new CustomField
            {
                Id = f.Id,
                Label = f.Label.Trim(),
                IsSecret = f.IsSecret,
                // An existing secret left empty in the form keeps its stored value.
                Value = f.HasExistingSecret && f.Value.Length == 0
                    ? previousFields.FirstOrDefault(p => p.Id == f.Id)?.Value ?? ""
                    : f.Value,
            })
            .ToList();
        entry.UpdatedAt = now;
    }

    private static List<string> ParseTags(string text) => text
        .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(MaxTags)
        .ToList();
}
